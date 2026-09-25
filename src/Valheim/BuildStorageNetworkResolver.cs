using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CraftingStationNetwork.Valheim
{
    /// <summary>
    /// Resolves the storage networks that are physically available to the player while
    /// placing a hammer piece that requires a crafting station.
    ///
    /// This intentionally does not merge station graphs. Each CraftingStation type is
    /// still resolved independently by ValheimStationNetwork. The returned collection
    /// is only the union of those independent networks for one build-storage query.
    /// </summary>
    internal static class BuildStorageNetworkResolver
    {
        private static readonly FieldInfo AllStationsField = AccessTools.Field(typeof(CraftingStation), "m_allStations");
        private static readonly FieldInfo RangeBuildField = AccessTools.Field(typeof(CraftingStation), "m_rangeBuild");

        internal static Piece GetSelectedPiece(Player player)
        {
            if (player == null)
            {
                return null;
            }

            try
            {
                return player.GetSelectedPiece();
            }
            catch (Exception ex)
            {
                Plugin.DebugLogOnce(
                    "build-storage:selected-piece:" + ex.GetType().FullName,
                    $"Build storage context could not read selected piece: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        internal static bool IsRequiredStationBuildContext(Player player)
        {
            Piece piece = GetSelectedPiece(player);
            return piece != null && piece.m_craftingStation != null;
        }

        internal static IReadOnlyList<CraftingStation> Resolve(
            Player player,
            CraftingStation explicitRequiredStation)
        {
            if (player == null)
            {
                return Array.Empty<CraftingStation>();
            }

            Piece piece = GetSelectedPiece(player);
            if (piece == null || piece.m_craftingStation == null)
            {
                // This resolver is deliberately limited to hammer pieces that have an
                // explicit station requirement. Station placement and stationless pieces
                // keep the already-tested single-network behavior.
                return Array.Empty<CraftingStation>();
            }

            var result = new List<CraftingStation>();
            var addedStationIds = new HashSet<int>();

            // Preserve the required station network selected by CraftingStorageLink.
            AddNetwork(explicitRequiredStation, result, addedStationIds);

            // Storage availability is separate from the station requirement itself.
            // Any crafting station whose vanilla build radius currently covers the player
            // contributes its own independent same-type network to the storage query.
            Vector3 point = player.transform.position;
            List<CraftingStation> loadedStations = SnapshotLoadedStations();

            for (int i = 0; i < loadedStations.Count; i++)
            {
                CraftingStation station = loadedStations[i];
                if (station == null || string.IsNullOrEmpty(ValheimStationNetwork.GetTypeKey(station)))
                {
                    continue;
                }

                float buildRange = GetBuildRange(station);
                if (buildRange <= 0f)
                {
                    continue;
                }

                float distanceSquared = (station.transform.position - point).sqrMagnitude;
                if (distanceSquared > buildRange * buildRange)
                {
                    continue;
                }

                AddNetwork(station, result, addedStationIds);
            }

            if (Plugin.Settings?.VerboseDiagnostics.Value == true)
            {
                string pieceName = string.IsNullOrEmpty(piece.m_name) ? piece.name : piece.m_name;
                Plugin.DebugLogOnce(
                    $"build-storage-context:{pieceName}:{DescribeNetworkSet(result)}",
                    $"Hammer storage context: piece='{pieceName}', requiredStation={ValheimStationNetwork.DescribeStation(explicitRequiredStation)}, storageNodes={result.Count}, networks={DescribeNetworkSet(result)}.");
            }

            return result;
        }

        private static void AddNetwork(
            CraftingStation origin,
            List<CraftingStation> result,
            HashSet<int> addedStationIds)
        {
            if (origin == null)
            {
                return;
            }

            int originId = origin.GetInstanceID();
            if (addedStationIds.Contains(originId))
            {
                return;
            }

            IReadOnlyList<CraftingStation> network = ValheimStationNetwork.Resolve(origin);
            for (int i = 0; i < network.Count; i++)
            {
                CraftingStation station = network[i];
                if (station == null)
                {
                    continue;
                }

                int stationId = station.GetInstanceID();
                if (addedStationIds.Add(stationId))
                {
                    result.Add(station);
                }
            }
        }

        private static float GetBuildRange(CraftingStation station)
        {
            if (station == null || RangeBuildField == null)
            {
                return 0f;
            }

            try
            {
                object value = RangeBuildField.GetValue(station);
                return value is float range ? Mathf.Max(0f, range) : 0f;
            }
            catch (Exception ex)
            {
                Plugin.DebugLogOnce(
                    "build-storage:range:" + ex.GetType().FullName,
                    $"Build storage context could not read CraftingStation.m_rangeBuild: {ex.GetType().Name}: {ex.Message}");
                return 0f;
            }
        }

        private static List<CraftingStation> SnapshotLoadedStations()
        {
            var result = new List<CraftingStation>();

            if (AllStationsField == null)
            {
                return result;
            }

            try
            {
                IEnumerable stations = AllStationsField.GetValue(null) as IEnumerable;
                if (stations == null)
                {
                    return result;
                }

                foreach (object value in stations)
                {
                    if (value is CraftingStation station && station != null)
                    {
                        result.Add(station);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugLogOnce(
                    "build-storage:stations:" + ex.GetType().FullName,
                    $"Build storage context could not read CraftingStation.m_allStations: {ex.GetType().Name}: {ex.Message}");
            }

            return result;
        }

        private static string DescribeNetworkSet(IReadOnlyList<CraftingStation> stations)
        {
            if (stations == null || stations.Count == 0)
            {
                return "<none>";
            }

            var types = new SortedSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < stations.Count; i++)
            {
                string type = ValheimStationNetwork.GetTypeKey(stations[i]);
                if (!string.IsNullOrEmpty(type))
                {
                    types.Add(type);
                }
            }

            return types.Count == 0 ? "<none>" : string.Join(",", types);
        }
    }
}
