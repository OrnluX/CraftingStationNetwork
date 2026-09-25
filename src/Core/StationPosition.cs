namespace CraftingStationNetwork.Core
{
    internal struct StationPosition
    {
        internal StationPosition(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal float X { get; }
        internal float Y { get; }
        internal float Z { get; }

        internal float DistanceSquaredTo(StationPosition other)
        {
            var dx = X - other.X;
            var dy = Y - other.Y;
            var dz = Z - other.Z;
            return (dx * dx) + (dy * dy) + (dz * dz);
        }
    }
}
