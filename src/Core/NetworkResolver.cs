using System;
using System.Collections.Generic;
using System.Linq;

namespace CraftingStationNetwork.Core
{
    internal sealed class NetworkResolver<TStation>
    {
        private readonly float _linkRangeSquared;
        private readonly float _maxRadiusSquared;

        internal NetworkResolver(float linkRange, float maxNetworkRadius)
        {
            if (linkRange <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(linkRange));
            }

            if (maxNetworkRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxNetworkRadius));
            }

            _linkRangeSquared = linkRange * linkRange;
            _maxRadiusSquared = maxNetworkRadius * maxNetworkRadius;
        }

        internal IReadOnlyList<StationNode<TStation>> Resolve(
            StationNode<TStation> origin,
            IReadOnlyCollection<StationNode<TStation>> stations)
        {
            if (origin == null)
            {
                throw new ArgumentNullException(nameof(origin));
            }

            if (stations == null)
            {
                throw new ArgumentNullException(nameof(stations));
            }

            if (string.IsNullOrEmpty(origin.TypeKey))
            {
                return new[] { origin };
            }

            var candidates = stations
                .Where(node =>
                    node != null &&
                    string.Equals(node.TypeKey, origin.TypeKey, StringComparison.Ordinal) &&
                    origin.Position.DistanceSquaredTo(node.Position) <= _maxRadiusSquared)
                .ToArray();

            var visited = new HashSet<int>();
            var result = new List<StationNode<TStation>>();
            var queue = new Queue<StationNode<TStation>>();

            visited.Add(origin.InstanceId);
            result.Add(origin);
            queue.Enqueue(origin);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                foreach (var candidate in candidates)
                {
                    if (visited.Contains(candidate.InstanceId))
                    {
                        continue;
                    }

                    if (current.Position.DistanceSquaredTo(candidate.Position) > _linkRangeSquared)
                    {
                        continue;
                    }

                    visited.Add(candidate.InstanceId);
                    result.Add(candidate);
                    queue.Enqueue(candidate);
                }
            }

            return result;
        }
    }
}
