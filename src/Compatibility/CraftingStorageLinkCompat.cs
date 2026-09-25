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
        private static MethodInfo _ensureContainerRegistry;
        private static FieldInfo _containerRegistryField;
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
                Plugin.DebugLog("CraftingStorageLink not detected; optional compatibility is inactive.");
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
                _ensureContainerRegistry = AccessTools.Method(pluginType, "EnsureContainerRegistry");
                _containerRegistryField = AccessTools.Field(pluginType, "_containerRegistry");

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
                    Plugin.DebugLog("CraftingStorageLink integration disabled: required internal methods or ContainerCandidate members were not found.");
                    return;
                }

                _harmony = new Harmony(Plugin.PluginGuid + ".compat.craftingstoragelink");
                _harmony.Patch(
                    findContainers,
                    postfix: new HarmonyMethod(typeof(CraftingStorageLinkCompat), nameof(FindContainersPostfix)));
                _harmony.Patch(
                    pullFromContainer,
                    prefix: new HarmonyMethod(typeof(CraftingStorageLinkCompat), nameof(PullFromContainerPrefix)));

                if (Plugin.Settings?.VerboseDiagnostics.Value == true)
                {
                    PatchDiagnosticMethods(pluginType.Assembly);
                }

                IsActive = true;
                string registryMode = _containerRegistryField != null && _ensureContainerRegistry != null
                    ? "native loaded-container registry"
                    : "Unity container scan fallback";
                log.LogInfo($"CraftingStorageLink {pluginInfo.Metadata.Version} detected. Station-network storage integration enabled ({registryMode}).");
                Plugin.DebugLog($"CraftingStorageLink {pluginInfo.Metadata.Version} compatibility active; container mode={registryMode}.");
            }
            catch (Exception ex)
            {
                IsActive = false;
                log.LogWarning($"CraftingStorageLink compatibility could not be enabled: {ex.GetType().Name}: {ex.Message}");
                Plugin.DebugLog($"CraftingStorageLink compatibility initialization failed: {ex.GetType().Name}: {ex.Message}");
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
            if (!IsActive || player == null || __result == null)
            {
                return;
            }

            string pieceName = ValheimStationNetwork.GetSelectedPieceName(player);
            CraftingStation networkOrigin = station ?? ValheimStationNetwork.ResolvePlacementAnchor(player);
            if (networkOrigin == null)
            {
                if (Plugin.Settings?.VerboseDiagnostics.Value == true)
                {
                    Plugin.DebugLogOnce(
                        $"find:no-origin:{pieceName}",
                        $"CSL FindContainers: piece='{pieceName}', explicitStation={ValheimStationNetwork.DescribeStation(station)}, derivedAnchor=<none>. Network expansion skipped.");
                }

                return;
            }

            int stationId = networkOrigin.GetInstanceID();
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
                Plugin.DebugLogOnce(
                    "find:unexpected-result:" + __result.GetType().FullName,
                    $"CSL FindContainers returned unsupported result type '{__result.GetType().FullName}'.");
                return;
            }

            int originalCandidateCount = candidates.Count;
            IReadOnlyList<CraftingStation> network = ValheimStationNetwork.Resolve(networkOrigin);
            if (network.Count == 0)
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

            List<Container> containers = SnapshotLoadedContainers();
            int added = 0;

            for (int i = 0; i < containers.Count; i++)
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

            if (Plugin.Settings?.VerboseDiagnostics.Value == true)
            {
                string mode = station == null ? "derived build anchor" : "explicit station";
                Plugin.DebugLogOnce(
                    $"find:{pieceName}:{stationId}:{network.Count}:{originalCandidateCount}:{added}",
                    $"CSL FindContainers: piece='{pieceName}', mode={mode}, origin={ValheimStationNetwork.DescribeStation(networkOrigin)}, networkNodes={network.Count}, radius={radius:0.##}m, originalCandidates={originalCandidateCount}, addedByNetwork={added}, finalCandidates={candidates.Count}.");
            }
        }

        private static void PullFromContainerPrefix(
            Player player,
            CraftingStation station,
            ref Vector3 origin,
            Container container)
        {
            if (!IsActive || player == null || container == null)
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

            CraftingStation networkOrigin = station ?? ValheimStationNetwork.ResolvePlacementAnchor(player);
            if (networkOrigin == null)
            {
                return;
            }

            IReadOnlyList<CraftingStation> network = ValheimStationNetwork.Resolve(networkOrigin);
            for (int i = 0; i < network.Count; i++)
            {
                CraftingStation linkedStation = network[i];
                if (linkedStation == null)
                {
                    continue;
                }

                Vector3 linkedOrigin = linkedStation.transform.position;
                if ((containerPosition - linkedOrigin).sqrMagnitude <= radiusSquared)
                {
                    Vector3 previousOrigin = origin;
                    origin = linkedOrigin;

                    if (Plugin.Settings?.VerboseDiagnostics.Value == true)
                    {
                        Plugin.DebugLogOnce(
                            $"pull-redirect:{container.GetInstanceID()}:{linkedStation.GetInstanceID()}",
                            $"CSL pull redirected for container '{container.name}': from ({previousOrigin.x:0.0},{previousOrigin.y:0.0},{previousOrigin.z:0.0}) to linked station {ValheimStationNetwork.DescribeStation(linkedStation)}.");
                    }

                    return;
                }
            }
        }

        private static void PatchDiagnosticMethods(Assembly assembly)
        {
            if (_harmony == null || assembly == null)
            {
                return;
            }

            int patched = 0;
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            if (types == null)
            {
                return;
            }

            for (int t = 0; t < types.Length; t++)
            {
                Type type = types[t];
                if (type == null)
                {
                    continue;
                }

                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (method.Name == "SupportsPiece" && method.ReturnType == typeof(bool))
                        {
                            _harmony.Patch(
                                method,
                                postfix: new HarmonyMethod(typeof(CraftingStorageLinkCompat), nameof(SupportsPieceDiagnosticPostfix)));
                            patched++;
                        }
                        else if (method.Name == "GetBuildStation" && typeof(CraftingStation).IsAssignableFrom(method.ReturnType))
                        {
                            _harmony.Patch(
                                method,
                                postfix: new HarmonyMethod(typeof(CraftingStorageLinkCompat), nameof(GetBuildStationDiagnosticPostfix)));
                            patched++;
                        }
                        else if (method.Name == "BeginBuildRequirementCheck" || method.Name == "BeginPlacement")
                        {
                            _harmony.Patch(
                                method,
                                prefix: new HarmonyMethod(typeof(CraftingStorageLinkCompat), nameof(BuildFlowDiagnosticPrefix)));
                            patched++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.DebugLogOnce(
                            $"diag-patch-error:{type.FullName}:{method.Name}",
                            $"Could not attach diagnostic patch to CSL {type.FullName}.{method.Name}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            Plugin.DebugLog($"Attached {patched} CraftingStorageLink diagnostic hook(s) for build-context tracing.");
        }

        private static void SupportsPieceDiagnosticPostfix(
            MethodBase __originalMethod,
            object[] __args,
            ref bool __result)
        {
            if (Plugin.Settings?.VerboseDiagnostics.Value != true)
            {
                return;
            }

            string piece = DescribePieceFromArgs(__args);
            Plugin.DebugLogOnce(
                $"supports-piece:{piece}:{__result}",
                $"CSL {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}: piece='{piece}', result={__result}.");
        }

        private static void GetBuildStationDiagnosticPostfix(
            MethodBase __originalMethod,
            object[] __args,
            CraftingStation __result)
        {
            if (Plugin.Settings?.VerboseDiagnostics.Value != true)
            {
                return;
            }

            string piece = DescribePieceFromArgs(__args);
            Plugin.DebugLogOnce(
                $"get-build-station:{piece}:{__result?.GetInstanceID() ?? 0}",
                $"CSL {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}: piece='{piece}', result={ValheimStationNetwork.DescribeStation(__result)}.");
        }

        private static void BuildFlowDiagnosticPrefix(MethodBase __originalMethod, object[] __args)
        {
            if (Plugin.Settings?.VerboseDiagnostics.Value != true)
            {
                return;
            }

            string piece = DescribePieceFromArgs(__args);
            Plugin.DebugLogOnce(
                $"build-flow:{__originalMethod.DeclaringType?.FullName}:{__originalMethod.Name}:{piece}",
                $"CSL build flow entered {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} for piece='{piece}'.");
        }

        private static string DescribePieceFromArgs(object[] args)
        {
            if (args == null)
            {
                return "<unknown>";
            }

            Player player = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is Piece piece)
                {
                    string name = string.IsNullOrEmpty(piece.m_name) ? piece.name : piece.m_name;
                    return string.IsNullOrEmpty(name) ? "<unnamed-piece>" : name;
                }

                if (args[i] is Player candidatePlayer)
                {
                    player = candidatePlayer;
                }
            }

            return player == null ? "<unknown>" : ValheimStationNetwork.GetSelectedPieceName(player);
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
                            Container container = value as Container;
                            if (container != null)
                            {
                                result.Add(container);
                            }
                        }

                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.DebugLog($"Could not read CraftingStorageLink container registry; falling back to Unity scan: {ex.GetType().Name}: {ex.Message}");
                    result.Clear();
                }
            }

            try
            {
                Container[] containers = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None);
                result.AddRange(containers);
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"CraftingStorageLink network container discovery failed: {ex.GetType().Name}: {ex.Message}");
                Plugin.DebugLog($"Unity container scan failed: {ex.GetType().Name}: {ex.Message}");
            }

            return result;
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
