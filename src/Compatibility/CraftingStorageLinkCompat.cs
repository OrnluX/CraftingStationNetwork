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
    internal static class CraftingStorageLinkCompat
    {
        internal const string PluginGuid = "com.custom.valheim.craftingstoragelink";

        private static Harmony _harmony;
        private static ManualLogSource _log;
        private static MethodInfo _isEligibleContainer;
        private static MethodInfo _effectiveRadiusGetter;
        private static ConstructorInfo _containerCandidateConstructor;
        private static FieldInfo _containerCandidateContainerField;

        private static int _lastExpandedFrame = -1;
        private static int _lastExpandedStationId;
        private static object _lastExpandedResult;

        internal static bool IsInstalled { get; private set; }
        internal static bool IsActive { get; private set; }

        internal static void Initialize(ManualLogSource log)
        {
            _log = log;
            IsInstalled = Chainloader.PluginInfos.TryGetValue(PluginGuid, out var pluginInfo);

            if (!IsInstalled)
            {
                log.LogDebug("CraftingStorageLink not detected; continuing without optional integration.");
                return;
            }

            try
            {
                Type pluginType = pluginInfo.Instance.GetType();
                MethodInfo findContainers = AccessTools.Method(
                    pluginType,
                    "FindContainers",
                    new[] { typeof(Player), typeof(CraftingStation), typeof(Vector3) });
                MethodInfo pullFromContainer = AccessTools.Method(
                    pluginType,
                    "PullFromContainer",
                    new[]
                    {
                        typeof(Player),
                        typeof(CraftingStation),
                        typeof(Vector3),
                        typeof(Container),
                        typeof(string),
                        typeof(int),
                        typeof(int)
                    });

                _isEligibleContainer = AccessTools.Method(
                    pluginType,
                    "IsEligibleContainer",
                    new[] { typeof(Player), typeof(Container) });

                PropertyInfo effectiveRadius = pluginType.GetProperty(
                    "EffectiveRadius",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _effectiveRadiusGetter = effectiveRadius?.GetGetMethod(true);

                Type candidateType = pluginType.GetNestedType(
                    "ContainerCandidate",
                    BindingFlags.Public | BindingFlags.NonPublic);

                _containerCandidateConstructor = candidateType?.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Container), typeof(float) },
                    null);
                _containerCandidateContainerField = candidateType == null
                    ? null
                    : AccessTools.Field(candidateType, "Container");

                if (findContainers == null ||
                    pullFromContainer == null ||
                    _isEligibleContainer == null ||
                    _effectiveRadiusGetter == null ||
                    _containerCandidateConstructor == null ||
                    _containerCandidateContainerField == null)
                {
                    log.LogWarning("CraftingStorageLink was detected, but its internal API does not match the supported layout. Network storage integration was disabled safely.");
                    return;
                }

                _harmony = new Harmony(Plugin.PluginGuid + ".compat.craftingstoragelink");
                _harmony.Patch(
                    findContainers,
                    postfix: new HarmonyMethod(typeof(CraftingStorageLinkCompat), nameof(FindContainersPostfix)));
                _harmony.Patch(
                    pullFromContainer,
                    prefix: new HarmonyMethod(typeof(CraftingStorageLinkCompat), nameof(PullFromContainerPrefix)));

                IsActive = true;
                log.LogInfo($"CraftingStorageLink {pluginInfo.Metadata.Version} detected. Station-network storage integration enabled.");
            }
            catch (Exception ex)
            {
                IsActive = false;
                log.LogWarning($"CraftingStorageLink compatibility could not be enabled: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static void Shutdown()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }

            IsActive = false;
        }

        private static void FindContainersPostfix(
            Player player,
            CraftingStation station,
            Vector3 origin,
            object __result)
        {
            if (!IsActive || station == null || player == null || __result == null)
            {
                return;
            }

            int stationId = station.GetInstanceID();
            if (_lastExpandedFrame == Time.frameCount &&
                _lastExpandedStationId == stationId &&
                ReferenceEquals(_lastExpandedResult, __result))
            {
                return;
            }

            _lastExpandedFrame = Time.frameCount;
            _lastExpandedStationId = stationId;
            _lastExpandedResult = __result;

            IList candidates = __result as IList;
            if (candidates == null)
            {
                return;
            }

            IReadOnlyList<CraftingStation> network = ValheimStationNetwork.Resolve(station);
            if (network.Count <= 1)
            {
                return;
            }

            float radius = GetEffectiveRadius();
            float radiusSquared = radius * radius;
            var existingContainers = new HashSet<Container>();

            for (int i = 0; i < candidates.Count; i++)
            {
                object candidate = candidates[i];
                Container existing = candidate == null
                    ? null
                    : _containerCandidateContainerField.GetValue(candidate) as Container;

                if (existing != null)
                {
                    existingContainers.Add(existing);
                }
            }

            Container[] containers;
            try
            {
                containers = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None);
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"CraftingStorageLink network container discovery failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            int added = 0;
            for (int i = 0; i < containers.Length; i++)
            {
                Container container = containers[i];
                if (container == null || existingContainers.Contains(container))
                {
                    continue;
                }

                float distanceSquared = GetClosestNetworkDistanceSquared(container.transform.position, network);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                if (!IsEligibleContainer(player, container))
                {
                    continue;
                }

                object candidate = _containerCandidateConstructor.Invoke(new object[] { container, distanceSquared });
                candidates.Add(candidate);
                existingContainers.Add(container);
                added++;
            }

            if (added > 0)
            {
                Plugin.DebugLog($"CraftingStorageLink: added {added} container(s) through {ValheimStationNetwork.GetTypeKey(station)} network ({network.Count} loaded station nodes).");
            }
        }

        private static void PullFromContainerPrefix(
            CraftingStation station,
            ref Vector3 origin,
            Container container)
        {
            if (!IsActive || station == null || container == null)
            {
                return;
            }

            float radius = GetEffectiveRadius();
            float radiusSquared = radius * radius;
            Vector3 containerPosition = container.transform.position;

            if ((containerPosition - origin).sqrMagnitude <= radiusSquared)
            {
                return;
            }

            IReadOnlyList<CraftingStation> network = ValheimStationNetwork.Resolve(station);
            for (int i = 0; i < network.Count; i++)
            {
                CraftingStation linkedStation = network[i];
                if (linkedStation == null || linkedStation == station)
                {
                    continue;
                }

                Vector3 linkedOrigin = linkedStation.transform.position;
                if ((containerPosition - linkedOrigin).sqrMagnitude <= radiusSquared)
                {
                    // CraftingStorageLink revalidates container distance during the actual pull.
                    // Point only this invocation at the linked station that makes the container valid;
                    // all of CraftingStorageLink's access, ownership and transaction checks still run.
                    origin = linkedOrigin;
                    return;
                }
            }
        }

        private static float GetClosestNetworkDistanceSquared(
            Vector3 point,
            IReadOnlyList<CraftingStation> network)
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

        private static bool IsEligibleContainer(Player player, Container container)
        {
            try
            {
                object result = _isEligibleContainer.Invoke(null, new object[] { player, container });
                return result is bool value && value;
            }
            catch (Exception ex)
            {
                Plugin.DebugLog($"CraftingStorageLink eligibility check failed for '{container?.name ?? "<null>"}': {ex.GetType().Name}: {ex.Message}");
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
            catch (Exception ex)
            {
                Plugin.DebugLog($"Could not read CraftingStorageLink EffectiveRadius: {ex.GetType().Name}: {ex.Message}");
            }

            return 20f;
        }
    }
}
