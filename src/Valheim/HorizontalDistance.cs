using UnityEngine;

namespace CraftingStationNetwork.Valheim
{
    internal static class HorizontalDistance
    {
        internal static float Squared(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return (dx * dx) + (dz * dz);
        }

        internal static Vector3 ProjectOriginToTargetHeight(Vector3 origin, Vector3 target)
        {
            return new Vector3(origin.x, target.y, origin.z);
        }
    }
}
