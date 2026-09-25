using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CraftingStationNetwork.Core;
using HarmonyLib;
using UnityEngine;

namespace CraftingStationNetwork.Valheim
{
    internal static class ValheimStationNetwork
    {
        private static readonly FieldInfo AllStationsField = AccessTools.Field(typeof(CraftingStation), "m_allStations");
        private static readonly FieldInfo PlacementGhostField = AccessTools.Field(typeof(Player), "m_placementGhost");
        private static readonly FieldInfo RangeBuildField = AccessTools.Field(typeof(CraftingStation), "m_rangeBuild");

        internal static IReadOnlyList<CraftingStation> Resolve(CraftingStation origin)
        {
            if (origin == null)
            {
                return Array.Empty<CraftingStation>();
            }

            string typeKey = GetTypeKey(origin);
            if (string.IsNullOrEmpty(typeKey))
            {
                return new[] { origin };
            }

            var stations = SnapshotLoadedStations();
            var nodes = new List<StationNode<CraftingStation>>(stations.Count + 1);
            StationNode<CraftingStation> originNode = null;
            int originId = origin.GetInstanceID();

            for (int i = 0; i < stations.Count; i++)
            {
                CraftingStation station = stations[i];
                if (station == null)
                {
                    continue;
                }

                string stationTypeKey = GetTypeKey(station);
                if (string.IsNullOrEmpty(stationTypeKey))
                {
                    continue;
                }

                Vector3 position = station.transform.position;
                var node = new StationNode<CraftingStation>(
                    station.GetInstanceID(),
                    stationTypeKey,
                    new StationPosition(position.x, position.y, position.z),
                    station);

                nodes.Add(node);

                if (node.InstanceId == originId)
                {
                    originNode = node;
                }
            }

            if (originNode == null)
            {
                Vector3 position = origin.transform.position;
                originNode = new StationNode<CraftingStation>(
                    originId,
                    typeKey,
                    new StationPosition(position.x, position.y, position.z),
                    origin);
                nodes.Add(originNode);
            }

            var resolver = new NetworkResolver<CraftingStation>(
                Plugin.Settings.LinkRange.Value,
                Plugin.Settings.MaxNetworkRadius.Value);

            IReadOnlyList<StationNode<CraftingStation>> resolved = resolver.Resolve(originNode, nodes);
            var result = new List<CraftingStation>(resolved.Count);

            for (int i = 0; i < resolved.Count; i++)
            {
                CraftingStation station = resolved[i].Instance;
                if (station != null)
                {
                    result.Add(station);
                }
            }

            if (Plugin.Settings?.VerboseDiagnostics.Value == true)
            {
                Plugin.DebugLogOnce(
                    $"network:{originId}:{result.Count}",
                    $"Resolved station network: origin={DescribeStation(origin)}, nodes={result.Count}, type={typeKey}.");
            }

            return result;
        }

        internal static CraftingStation ResolvePlacementAnchor(Player player)
        {
            if (player == null)
            {
                return null;
            }

            Piece selectedPiece = GetSelectedPiece(player);
            if (selectedPiece == null)
            {
                return null;
            }

            string pieceName = DescribePiece(selectedPiece);
            CraftingStation stationPrefab = selectedPiece.GetComponent<CraftingStation>();
            if (stationPrefab == null)
            {
                stationPrefab = selectedPiece.GetComponentInChildren<CraftingStation>(true);
            }

            if (stationPrefab != null)
            {
                string typeKey = GetTypeKey(stationPrefab);
                if (string.IsNullOrEmpty(typeKey))
                {
                    return null;
                }

                Vector3 placementPoint = GetPlacementPoint(player);
                CraftingStation stationAnchor = FindClosestCompatibleStation(
                    typeKey,
                    placementPoint,
                    Plugin.Settings.LinkRange.Value);

                if (Plugin.Settings?.VerboseDiagnostics.Value == true)
                {
                    Plugin.DebugLogOnce(
                        $"placement-anchor:station-piece:{pieceName}:{stationAnchor?.GetInstanceID() ?? 0}",
                        $"Placement anchor for station piece '{pieceName}': requestedType={typeKey}, placement={FormatVector(placementPoint)}, anchor={DescribeStation(stationAnchor)}.");
                }

                // A station piece may only borrow materials from a network it can actually join.
                // Do not fall back to a different station type here.
                return stationAnchor;
            }

            // A non-station hammer piece with an explicit crafting-station requirement
            // must never fall back to another nearby station type when the required
            // station is missing. In that state CraftingStorageLink supplies a null
            // explicit station and Valheim should remain authoritative for the invalid
            // requirement state.
            if (selectedPiece.m_craftingStation != null)
            {
                if (Plugin.Settings?.VerboseDiagnostics.Value == true)
                {
                    Plugin.DebugLogOnce(
                        $"placement-anchor:required-station-missing:{pieceName}",
                        $"Placement anchor skipped for required-station piece '{pieceName}': no explicit required station is available.");
                }

                return null;
            }

            // Some hammer pieces have no required crafting station at all. In that case
            // CraftingStorageLink legitimately supplies a null station. We still allow the
            // piece to use one existing network when the player is physically inside that
            // station's vanilla build radius. The nearest covering station wins, keeping
            // station types isolated rather than merging overlapping networks.
            CraftingStation nearbyAnchor = FindClosestStationCoveringPoint(player.transform.position);

            if (Plugin.Settings?.VerboseDiagnostics.Value == true)
            {
                Plugin.DebugLogOnce(
                    $"placement-anchor:generic:{pieceName}:{nearbyAnchor?.GetInstanceID() ?? 0}",
                    $"Placement anchor for stationless piece '{pieceName}': player={FormatVector(player.transform.position)}, anchor={DescribeStation(nearbyAnchor)}.");
            }

            return nearbyAnchor;
        }

        internal static string GetTypeKey(CraftingStation station)
        {
            if (station == null)
            {
                return string.Empty;
            }

            return station.m_name ?? string.Empty;
        }

        internal static string GetSelectedPieceName(Player player)
        {
            Piece piece = GetSelectedPiece(player);
            return DescribePiece(piece);
        }

        internal static string DescribeStation(CraftingStation station)
        {
            if (station == null)
            {
                return "<none>";
            }

            return $"{GetTypeKey(station)}#{station.GetInstanceID()}@{FormatVector(station.transform.position)}";
        }

        private static Piece GetSelectedPiece(Player player)
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
                    "selected-piece-error:" + ex.GetType().FullName,
                    $"Could not read selected build piece: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static Vector3 GetPlacementPoint(Player player)
        {
            Vector3 placementPoint = player.transform.position;

            if (PlacementGhostField == null)
            {
                return placementPoint;
            }

            try
            {
                GameObject placementGhost = PlacementGhostField.GetValue(player) as GameObject;
                if (placementGhost != null && placementGhost.activeInHierarchy)
                {
                    placementPoint = placementGhost.transform.position;
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugLogOnce(
                    "placement-ghost-error:" + ex.GetType().FullName,
                    $"Could not read placement ghost position: {ex.GetType().Name}: {ex.Message}");
            }

            return placementPoint;
        }

        private static CraftingStation FindClosestCompatibleStation(string typeKey, Vector3 point, float maxDistance)
        {
            if (string.IsNullOrEmpty(typeKey) || maxDistance <= 0f)
            {
                return null;
            }

            float maxDistanceSquared = maxDistance * maxDistance;
            float bestDistanceSquared = float.MaxValue;
            CraftingStation best = null;
            List<CraftingStation> stations = SnapshotLoadedStations();

            for (int i = 0; i < stations.Count; i++)
            {
                CraftingStation station = stations[i];
                if (station == null || !string.Equals(GetTypeKey(station), typeKey, StringComparison.Ordinal))
                {
                    continue;
                }

                float distanceSquared = (station.transform.position - point).sqrMagnitude;
                if (distanceSquared > maxDistanceSquared || distanceSquared >= bestDistanceSquared)
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                best = station;
            }

            return best;
        }

        private static CraftingStation FindClosestStationCoveringPoint(Vector3 point)
        {
            float bestDistanceSquared = float.MaxValue;
            CraftingStation best = null;
            List<CraftingStation> stations = SnapshotLoadedStations();

            for (int i = 0; i < stations.Count; i++)
            {
                CraftingStation station = stations[i];
                if (station == null || string.IsNullOrEmpty(GetTypeKey(station)))
                {
                    continue;
                }

                float buildRange = GetBuildRange(station);
                if (buildRange <= 0f)
                {
                    continue;
                }

                float distanceSquared = (station.transform.position - point).sqrMagnitude;
                if (distanceSquared > buildRange * buildRange || distanceSquared >= bestDistanceSquared)
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                best = station;
            }

            return best;
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
                if (value is float range)
                {
                    return Mathf.Max(0f, range);
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugLogOnce(
                    "build-range-error:" + ex.GetType().FullName,
                    $"Could not read CraftingStation.m_rangeBuild: {ex.GetType().Name}: {ex.Message}");
            }

            return 0f;
        }

        private static List<CraftingStation> SnapshotLoadedStations()
        {
            var result = new List<CraftingStation>();

            if (AllStationsField == null)
            {
                Plugin.Log?.LogWarning("Could not resolve CraftingStation.m_allStations; station networking will be limited to the active station.");
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
                    CraftingStation station = value as CraftingStation;
                    if (station != null)
                    {
                        result.Add(station);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"Could not read CraftingStation.m_allStations: {ex.GetType().Name}: {ex.Message}");
            }

            return result;
        }

        private static string DescribePiece(Piece piece)
        {
            if (piece == null)
            {
                return "<none>";
            }

            string displayName = string.IsNullOrEmpty(piece.m_name) ? piece.name : piece.m_name;
            return string.IsNullOrEmpty(displayName) ? "<unnamed-piece>" : displayName;
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.0},{value.y:0.0},{value.z:0.0})";
        }
    }
}
