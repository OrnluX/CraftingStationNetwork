namespace CraftingStationNetwork.Core
{
    internal sealed class StationNode<TStation>
    {
        internal StationNode(int instanceId, string typeKey, StationPosition position, TStation instance)
        {
            InstanceId = instanceId;
            TypeKey = typeKey;
            Position = position;
            Instance = instance;
        }

        internal int InstanceId { get; }
        internal string TypeKey { get; }
        internal StationPosition Position { get; }
        internal TStation Instance { get; }
    }
}
