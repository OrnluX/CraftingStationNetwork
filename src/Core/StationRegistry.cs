using System.Collections.Generic;
using System.Linq;

namespace CraftingStationNetwork.Core
{
    internal sealed class StationRegistry<TStation>
    {
        private readonly Dictionary<int, StationNode<TStation>> _nodes = new Dictionary<int, StationNode<TStation>>();

        internal int Count => _nodes.Count;

        internal void Upsert(StationNode<TStation> node)
        {
            _nodes[node.InstanceId] = node;
        }

        internal bool Remove(int instanceId)
        {
            return _nodes.Remove(instanceId);
        }

        internal bool TryGet(int instanceId, out StationNode<TStation> node)
        {
            return _nodes.TryGetValue(instanceId, out node);
        }

        internal IReadOnlyCollection<StationNode<TStation>> Snapshot()
        {
            return _nodes.Values.ToArray();
        }

        internal void Clear()
        {
            _nodes.Clear();
        }
    }
}
