using BepInEx.Bootstrap;
using BepInEx.Logging;

namespace CraftingStationNetwork.Compatibility
{
    internal static class CraftingStorageLinkCompat
    {
        internal const string PluginGuid = "com.custom.valheim.craftingstoragelink";

        internal static bool IsInstalled { get; private set; }

        internal static void Initialize(ManualLogSource log)
        {
            IsInstalled = Chainloader.PluginInfos.ContainsKey(PluginGuid);

            if (IsInstalled)
            {
                log.LogInfo("CraftingStorageLink detected. Compatibility adapter is available for implementation.");
            }
            else
            {
                log.LogDebug("CraftingStorageLink not detected; continuing without optional integration.");
            }
        }
    }
}
