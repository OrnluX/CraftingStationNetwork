using BepInEx;
using BepInEx.Logging;
using CraftingStationNetwork.Compatibility;
using CraftingStationNetwork.Configuration;
using CraftingStationNetwork.Diagnostics;

namespace CraftingStationNetwork
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(CraftingStorageLinkCompat.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.ornlux.valheim.craftingstationnetwork";
        public const string PluginName = "CraftingStationNetwork";
        public const string PluginVersion = "0.1.6";

        internal static ManualLogSource Log { get; private set; }
        internal static PluginConfig Settings { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Settings = PluginConfig.Bind(Config);
            DevelopmentConsole.Initialize(Settings.DevelopmentConsole.Value);
            DebugLog($"Booting {PluginName} {PluginVersion}.");

            CraftingStorageLinkCompat.Initialize(Logger);
            HammerBuildStorageCompat.Initialize(Logger);

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. LinkRange={Settings.LinkRange.Value:0.##}m; MaxNetworkRadius={Settings.MaxNetworkRadius.Value:0.##}m.");
            DebugLog($"Network defaults active: LinkRange={Settings.LinkRange.Value:0.##}m; MaxNetworkRadius={Settings.MaxNetworkRadius.Value:0.##}m.");
        }

        private void OnGUI()
        {
            DevelopmentConsole.Draw();
        }

        private void OnDestroy()
        {
            HammerBuildStorageCompat.Shutdown();
            CraftingStorageLinkCompat.Shutdown();
            DevelopmentConsole.Shutdown();
        }

        internal static void DebugLog(string message)
        {
            DevelopmentConsole.Trace(message);

            if (Settings?.MirrorDevelopmentLogToBepInEx.Value == true)
            {
                Log?.LogDebug(message);
            }
        }

        internal static void DebugLogOnce(string key, string message)
        {
            DevelopmentConsole.TraceOnce(key, message);

            if (Settings?.MirrorDevelopmentLogToBepInEx.Value == true)
            {
                Log?.LogDebug(message);
            }
        }
    }
}
