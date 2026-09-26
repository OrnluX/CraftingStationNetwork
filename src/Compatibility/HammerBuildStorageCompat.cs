using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using CraftingStationNetwork.Valheim;
using HarmonyLib;
using UnityEngine;

namespace CraftingStationNetwork.Compatibility
{
    internal static class HammerBuildStorageCompat
    {
        private static Harmony _harmony;
        private static ManualLogSource _log;
        private static MethodInfo _isEligibleContainer;
        private static MethodInfo _effectiveRadiusGetter;
        private static MethodInfo _ensureContainerRegistry;
        private static FieldInfo _containerRegistryField;
        private static ConstructorInfo _candidateCtor;
        private static FieldInfo _candidateContainerField;

        internal static bool IsActive { get; private set; }

        internal static void Initialize(ManualLogSource log)
        {
            _log = log;
            if (!CraftingStorageLinkCompat.IsActive || !Chainloader.PluginInfos.TryGetValue(CraftingStorageLinkCompat.PluginGuid, out var info))
            {
                return;
            }

            try
            {
                Type pluginType = info.Instance.GetType();
                MethodInfo findContainers = AccessTools.Method(pluginType, "FindContainers", new[] { typeof(Player), typeof(CraftingStation), typeof(Vector3) });
                MethodInfo pullFromContainer = AccessTools.Method(pluginType, "PullFromContainer", new[] { typeof(Player), typeof(CraftingStation), typeof(Vector3), typeof(Container), typeof(string), typeof(int), typeof(int) });
                _isEligibleContainer = AccessTools.Method(pluginType, "IsEligibleContainer", new[] { typeof(Player), typeof(Container) });
                _ensureContainerRegistry = AccessTools.Method(pluginType, "EnsureContainerRegistry");
                _containerRegistryField = AccessTools.Field(pluginType, "_containerRegistry");
                _effectiveRadiusGetter = pluginType.GetProperty("EffectiveRadius", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);

                Type candidateType = pluginType.GetNestedType("ContainerCandidate", BindingFlags.Public | BindingFlags.NonPublic);
                _candidateCtor = candidateType?.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Container), typeof(float) }, null);
                _candidateContainerField = candidateType == null ? null : AccessTools.Field(candidateType, "Container");

                if (findContainers == null || pullFromContainer == null || _isEligibleContainer == null || _effectiveRadiusGetter == null || _candidateCtor == null || _candidateContainerField == null)
                {
                    log.LogWarning("Supplemental hammer storage compatibility could not match CraftingStorageLink internals and was disabled safely.");
                    return;
                }

                _harmony = new Harmony(Plugin.PluginGuid + ".compat.craftingstoragelink.hammerstorage");
                _harmony.Patch(findContainers, postfix: new HarmonyMethod(typeof(HammerBuildStorageCompat), nameof(FindContainersPostfix)) { priority = Priority.Last });
                _harmony.Patch(pullFromContainer, prefix: new HarmonyMethod(typeof(HammerBuildStorageCompat), nameof(PullFromContainerPrefix)) { priority = Priority.Last });
                IsActive = true;
                Plugin.DebugLog("Hammer build storage context active: covering station networks contribute storage independently.");
            }
            catch (Exception ex)
            {
                IsActive = false;
                log.LogWarning($"Hammer build storage compatibility failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static void Shutdown()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            IsActive = false;
        }

        private static void FindContainersPostfix(Player player, CraftingStation station, Vector3 origin, object __result)
        {
            if (!IsActive || player == null || __result == null || !BuildStorageNetworkResolver.IsRequiredStationBuildContext(player))
            {
                return;
            }

            IList candidates = __result as IList;
            if (candidates == null)
            {
                return;
            }

            IReadOnlyList<CraftingStation> storageStations = BuildStorageNetworkResolver.Resolve(player, station);
            if (storageStations.Count == 0)
            {
                return;
            }

            float radius = GetEffectiveRadius();
            float radiusSquared = radius * radius;
            var existing = new HashSet<Container>();
            for (int i = 0; i < candidates.Count; i++)
            {
                object candidate = candidates[i];
                Container container = candidate == null ? null : _candidateContainerField.GetValue(candidate) as Container;
                if (container != null)
                {
                    existing.Add(container);
                }
            }

            int before = candidates.Count;
            int added = 0;
            List<Container> containers = SnapshotLoadedContainers();
            for (int i = 0; i < containers.Count; i++)
            {
                Container container = containers[i];
                if (container == null || existing.Contains(container) || !IsEligibleContainer(player, container))
                {
                    continue;
                }

                float distanceSquared = ClosestDistanceSquared(container.transform.position, storageStations);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                candidates.Add(_candidateCtor.Invoke(new object[] { container, distanceSquared }));
                existing.Add(container);
                added++;
            }

            if (Plugin.Settings?.VerboseDiagnostics.Value == true)
            {
                string piece = ValheimStationNetwork.GetSelectedPieceName(player);
                Plugin.DebugLogOnce($"hammer-storage-find:{piece}:{storageStations.Count}:{before}:{added}", $"CSL hammer multi-network storage: piece='{piece}', requiredStation={ValheimStationNetwork.DescribeStation(station)}, storageNodes={storageStations.Count}, originalCandidates={before}, added={added}, finalCandidates={candidates.Count}.");
            }
        }

        private static void PullFromContainerPrefix(Player player, CraftingStation station, ref Vector3 origin, Container container)
        {
            if (!IsActive || player == null || container == null || !BuildStorageNetworkResolver.IsRequiredStationBuildContext(player))
            {
                return;
            }

            float radius = GetEffectiveRadius();
            float radiusSquared = radius * radius;
            Vector3 position = container.transform.position;
            if ((position - origin).sqrMagnitude <= radiusSquared)
            {
                return;
            }

            IReadOnlyList<CraftingStation> storageStations = BuildStorageNetworkResolver.Resolve(player, station);
            for (int i = 0; i < storageStations.Count; i++)
            {
                CraftingStation linked = storageStations[i];
                if (linked == null || (position - linked.transform.position).sqrMagnitude > radiusSquared)
                {
                    continue;
                }

                Vector3 oldOrigin = origin;
                origin = linked.transform.position;
                if (Plugin.Settings?.VerboseDiagnostics.Value == true)
                {
                    Plugin.DebugLogOnce($"hammer-storage-pull:{container.GetInstanceID()}:{linked.GetInstanceID()}", $"CSL hammer pull redirected from ({oldOrigin.x:0.0},{oldOrigin.y:0.0},{oldOrigin.z:0.0}) to {ValheimStationNetwork.DescribeStation(linked)} for container '{container.name}'.");
                }
                return;
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
            catch (Exception ex)
            {
                _log?.LogWarning($"Hammer build storage container discovery failed: {ex.GetType().Name}: {ex.Message}");
            }
            return result;
        }

        private static bool IsEligibleContainer(Player player, Container container)
        {
            try
            {
                object result = _isEligibleContainer.Invoke(null, new object[] { player, container });
                return result is bool value && value;
            }
            catch
            {
                return false;
            }
        }

        private static float GetEffectiveRadius()
        {
            try
            {
                object result = _effectiveRadiusGetter.Invoke(null, null);
                if (result is float value)
                {
                    return Mathf.Max(1f, value);
                }
            }
            catch
            {
            }
            return 20f;
        }

        private static float ClosestDistanceSquared(Vector3 point, IReadOnlyList<CraftingStation> stations)
        {
            float best = float.MaxValue;
            for (int i = 0; i < stations.Count; i++)
            {
                CraftingStation station = stations[i];
                if (station == null)
                {
                    continue;
                }
                float distance = (point - station.transform.position).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                }
            }
            return best;
        }
    }
}
