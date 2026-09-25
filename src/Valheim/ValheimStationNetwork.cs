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

            return result;
        }

        internal static CraftingStation ResolvePlacementAnchor(Player player)
        {
            if (player == null)
            {
                return null;
            }

            Piece selectedPiece;
            try
            {
                selectedPiece = player.GetSelectedPiece();
            }
            catch (Exception ex)
            {
                Plugin.DebugLog($"Could not read selected build piece: {ex.GetType().Name}: {ex.Message}");
                return null;
            }

            if (selectedPiece == null)
            {
                return null;
            }

            CraftingStation stationPrefab = selectedPiece.GetComponent<CraftingStation>();
            if (stationPrefab == null)
            {
                stationPrefab = selectedPiece.GetComponentInChildren<CraftingStation>(true);
            }

            if (stationPrefab == null)
            {
                return null;
            }

            string typeKey = GetTypeKey(stationPrefab);
            if (string.IsNullOrEmpty(typeKey))
            {
                return null;
            }

            Vector3 placementPoint = player.transform.position;
            if (PlacementGhostField != null)
            {
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
                    Plugin.DebugLog($"Could not read placement ghost position: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return FindClosestCompatibleStation(typeKey, placementPoint, Plugin.Settings.LinkRange.Value);
        }

        internal static string GetTypeKey(CraftingStation station)
        {
            if (station == null)
            {
                return string.Empty;
            }

            return station.m_name ?? string.Empty;
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
    }
}
