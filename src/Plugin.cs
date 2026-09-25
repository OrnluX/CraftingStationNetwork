using BepInEx;
using BepInEx.Logging;
using CraftingStationNetwork.Compatibility;
using CraftingStationNetwork.Configuration;

namespace CraftingStationNetwork
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(CraftingStorageLinkCompat.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.ornlux.valheim.craftingstationnetwork";
        public const string PluginName = "CraftingStationNetwork";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log { get; private set; }
        internal static PluginConfig Settings { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Settings = PluginConfig.Bind(Config);
            CraftingStorageLinkCompat.Initialize(Logger);

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. LinkRange={Settings.LinkRange.Value:0.##}m; MaxNetworkRadius={Settings.MaxNetworkRadius.Value:0.##}m.");
        }

        private void OnDestroy()
        {
            CraftingStorageLinkCompat.Shutdown();
        }

        internal static void DebugLog(string message)
        {
            Log?.LogDebug(message);
        }
    }
}
