using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using CraftingStationNetwork.Valheim;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace CraftingStationNetwork.Compatibility
{
    internal static class BuildHudVisualFix
    {
        private static Harmony _harmony;
        private static ManualLogSource _log;
        private static MethodInfo _isEligibleContainer;
        private static MethodInfo _effectiveRadiusGetter;
        private static MethodInfo _effectiveEnabledGetter;
        private static MethodInfo _effectiveHammerBuildingEnabledGetter;
        private static MethodInfo _ensureContainerRegistry;
        private static MethodInfo _getLoadedInventory;
        private static FieldInfo _containerRegistryField;

        internal static void Initialize(ManualLogSource log)
        {
            _log = log;

            if (!CraftingStorageLinkCompat.IsActive ||
                !Chainloader.PluginInfos.TryGetValue(CraftingStorageLinkCompat.PluginGuid, out var pluginInfo))
            {
                return;
            }

            try
            {
                Type pluginType = pluginInfo.Instance.GetType();
                BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

                _isEligibleContainer = AccessTools.Method(
                    pluginType,
                    "IsEligibleContainer",
                    new[] { typeof(Player), typeof(Container) });
                _ensureContainerRegistry = AccessTools.Method(pluginType, "EnsureContainerRegistry");
                _getLoadedInventory = AccessTools.Method(
                    pluginType,
                    "GetLoadedInventory",
                    new[] { typeof(Container) });
                _containerRegistryField = AccessTools.Field(pluginType, "_containerRegistry");

                _effectiveRadiusGetter = pluginType
                    .GetProperty("EffectiveRadius", staticFlags)?
                    .GetGetMethod(true);
                _effectiveEnabledGetter = pluginType
                    .GetProperty("EffectiveEnabled", staticFlags)?
                    .GetGetMethod(true);
                _effectiveHammerBuildingEnabledGetter = pluginType
                    .GetProperty("EffectiveHammerBuildingEnabled", staticFlags)?
                    .GetGetMethod(true);

                if (_isEligibleContainer == null || _effectiveRadiusGetter == null)
                {
                    Plugin.DebugLog("Hammer HUD visual fallback disabled: required CraftingStorageLink helpers were not found.");
                    return;
                }

                _harmony = new Harmony(Plugin.PluginGuid + ".compat.craftingstoragelink.hudvisual");
                int patched = PatchSetupPieceInfoMethods(_harmony);
                Plugin.DebugLog($"Hammer HUD visual fallback active; patched {patched} Hud.SetupPieceInfo overload(s).");
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"CraftingStationNetwork HUD visual fallback could not start: {ex.GetType().Name}: {ex.Message}");
                Plugin.DebugLog($"HUD visual fallback initialization failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static void Shutdown()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }

        private static int PatchSetupPieceInfoMethods(Harmony harmony)
        {
            int patched = 0;
            MethodInfo[] methods = typeof(Hud).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || method.Name != "SetupPieceInfo")
                {
                    continue;
                }

                var postfix = new HarmonyMethod(typeof(BuildHudVisualFix), nameof(SetupPieceInfoPostfix))
                {
                    priority = Priority.Last
                };

                harmony.Patch(method, postfix: postfix);
                patched++;
            }

            return patched;
        }

        private static void SetupPieceInfoPostfix(Hud __instance, object[] __args)
        {
            if (!CraftingStorageLinkCompat.IsActive || __instance == null)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            Piece piece = FindPiece(__args);

            if (player == null || piece == null || piece.m_resources == null || __instance.m_requirementItems == null)
            {
                return;
            }

            int rowCount = Math.Min(piece.m_resources.Length, __instance.m_requirementItems.Length);

            for (int i = 0; i < rowCount; i++)
            {
                Piece.Requirement requirement = piece.m_resources[i];
                if (requirement == null || requirement.m_resItem == null || requirement.m_amount <= 0)
                {
                    continue;
                }

                GameObject row = __instance.m_requirementItems[i];
                if (row == null)
                {
                    continue;
                }

                Transform amountTransform = row.transform.Find("res_amount");
                TMP_Text amountText = amountTransform == null ? null : amountTransform.GetComponent<TMP_Text>();
                if (amountText == null || !string.IsNullOrWhiteSpace(amountText.text))
                {
                    continue;
                }

                int available = CountAvailable(player, piece, requirement);
                amountText.text = $"{available}/{requirement.m_amount}";

                if (Plugin.Settings?.VerboseDiagnostics.Value == true)
                {
                    string itemName = GetRequirementItemName(requirement);
                    Plugin.DebugLogOnce(
                        $"hud-blank-fix:{piece.GetInstanceID()}:{i}:{available}:{requirement.m_amount}",
                        $"HUD visual fallback restored blank requirement count for piece='{piece.m_name}', item='{itemName}', display={available}/{requirement.m_amount}.");
                }
            }
        }

        private static int CountAvailable(Player player, Piece piece, Piece.Requirement requirement)
        {
            string itemName = GetRequirementItemName(requirement);
            if (string.IsNullOrEmpty(itemName) || player == null)
            {
                return 0;
            }

            int total = CountRawInventory(player.GetInventory(), itemName);

            if (!IsHammerLinkingEnabled())
            {
                return total;
            }

            CraftingStation explicitStation = null;
            Vector3 directOrigin = player.transform.position;

            if (piece.m_craftingStation != null)
            {
                explicitStation = CraftingStation.HaveBuildStationInRange(
                    piece.m_craftingStation.m_name,
                    player.transform.position);

                if (explicitStation == null)
                {
                    return total;
                }

                directOrigin = explicitStation.transform.position;
            }

            float radius = GetEffectiveRadius();
            float radiusSquared = radius * radius;
            CraftingStation networkOrigin = explicitStation ?? ValheimStationNetwork.ResolvePlacementAnchor(player);
            IReadOnlyList<CraftingStation> network = networkOrigin == null
                ? Array.Empty<CraftingStation>()
                : ValheimStationNetwork.Resolve(networkOrigin);

            List<Container> containers = SnapshotLoadedContainers();
            var counted = new HashSet<Container>();

            for (int i = 0; i < containers.Count; i++)
            {
                Container container = containers[i];
                if (container == null || counted.Contains(container) || !IsEligibleContainer(player, container))
                {
                    continue;
                }

                Vector3 containerPosition = container.transform.position;
                bool inScope = (containerPosition - directOrigin).sqrMagnitude <= radiusSquared;

                if (!inScope && network.Count > 0)
                {
                    inScope = GetClosestNetworkDistanceSquared(containerPosition, network) <= radiusSquared;
                }

                if (!inScope)
                {
                    continue;
                }

                Inventory inventory = GetLoadedInventory(container);
                if (inventory == null)
                {
                    continue;
                }

                total += CountRawInventory(inventory, itemName);
                counted.Add(container);
            }

            return total;
        }

        private static bool IsHammerLinkingEnabled()
        {
            if (!ReadBoolGetter(_effectiveEnabledGetter, true))
            {
                return false;
            }

            return ReadBoolGetter(_effectiveHammerBuildingEnabledGetter, true);
        }

        private static bool ReadBoolGetter(MethodInfo getter, bool fallback)
        {
            if (getter == null)
            {
                return fallback;
            }

            try
            {
                object value = getter.Invoke(null, null);
                return value is bool enabled ? enabled : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static float GetEffectiveRadius()
        {
            try
            {
                object value = _effectiveRadiusGetter.Invoke(null, null);
                if (value is float radius)
                {
                    return Mathf.Max(1f, radius);
                }
            }
            catch
            {
            }

            return 20f;
        }

        private static bool IsEligibleContainer(Player player, Container container)
        {
            try
            {
                object result = _isEligibleContainer.Invoke(null, new object[] { player, container });
                return result is bool eligible && eligible;
            }
            catch
            {
                return false;
            }
        }

        private static List<Container> SnapshotLoadedContainers()
        {
            var result = new List<Container>();

            if (_containerRegistryField != null && _ensureContainerRegistry != null)
            {
                try
                {
                    _ensureContainerRegistry.Invoke(null, null);
                    IEnumerable registry = _containerRegistryField.GetValue(null) as IEnumerable;
                    if (registry != null)
                    {
                        foreach (object value in registry)
                        {
                            if (value is Container container && container != null)
                            {
                                result.Add(container);
                            }
                        }

                        return result;
                    }
                }
                catch
                {
                    result.Clear();
                }
            }

            try
            {
                result.AddRange(UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None));
            }
            catch
            {
            }

            return result;
        }

        private static Inventory GetLoadedInventory(Container container)
        {
            if (container == null)
            {
                return null;
            }

            if (_getLoadedInventory != null)
            {
                try
                {
                    return _getLoadedInventory.Invoke(null, new object[] { container }) as Inventory;
                }
                catch
                {
                }
            }

            try
            {
                return container.GetInventory();
            }
            catch
            {
                return null;
            }
        }

        private static int CountRawInventory(Inventory inventory, string itemName)
        {
            if (inventory == null || string.IsNullOrEmpty(itemName))
            {
                return 0;
            }

            int total = 0;

            try
            {
                List<ItemDrop.ItemData> items = inventory.GetAllItems();
                if (items == null)
                {
                    return 0;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    ItemDrop.ItemData item = items[i];
                    if (item == null || item.m_shared == null || item.m_shared.m_name != itemName)
                    {
                        continue;
                    }

                    total += Math.Max(0, item.m_stack);
                }
            }
            catch
            {
                return 0;
            }

            return total;
        }

        private static float GetClosestNetworkDistanceSquared(Vector3 point, IReadOnlyList<CraftingStation> network)
        {
            float best = float.MaxValue;

            for (int i = 0; i < network.Count; i++)
            {
                CraftingStation station = network[i];
                if (station == null)
                {
                    continue;
                }

                float distanceSquared = (point - station.transform.position).sqrMagnitude;
                if (distanceSquared < best)
                {
                    best = distanceSquared;
                }
            }

            return best;
        }

        private static string GetRequirementItemName(Piece.Requirement requirement)
        {
            if (requirement == null || requirement.m_resItem == null || requirement.m_resItem.m_itemData == null)
            {
                return string.Empty;
            }

            return requirement.m_resItem.m_itemData.m_shared?.m_name ?? string.Empty;
        }

        private static Piece FindPiece(object[] args)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] is Piece piece)
                    {
                        return piece;
                    }
                }
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return null;
            }

            try
            {
                PieceTable table = player.GetBuildTool();
                return table == null ? null : table.GetSelectedPiece();
            }
            catch
            {
                return null;
            }
        }
    }
}
