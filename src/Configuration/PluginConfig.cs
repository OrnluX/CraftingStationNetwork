using BepInEx.Configuration;

namespace CraftingStationNetwork.Configuration
{
    internal sealed class PluginConfig
    {
        private const float HardMaxNetworkRadius = 150f;

        private PluginConfig(
            ConfigEntry<float> linkRange,
            ConfigEntry<float> maxNetworkRadius,
            ConfigEntry<bool> developmentConsole,
            ConfigEntry<bool> verboseDiagnostics,
            ConfigEntry<bool> mirrorDevelopmentLogToBepInEx)
        {
            LinkRange = linkRange;
            MaxNetworkRadius = maxNetworkRadius;
            DevelopmentConsole = developmentConsole;
            VerboseDiagnostics = verboseDiagnostics;
            MirrorDevelopmentLogToBepInEx = mirrorDevelopmentLogToBepInEx;
        }

        internal ConfigEntry<float> LinkRange { get; }
        internal ConfigEntry<float> MaxNetworkRadius { get; }
        internal ConfigEntry<bool> DevelopmentConsole { get; }
        internal ConfigEntry<bool> VerboseDiagnostics { get; }
        internal ConfigEntry<bool> MirrorDevelopmentLogToBepInEx { get; }

        internal static PluginConfig Bind(ConfigFile config)
        {
            var linkRange = config.Bind(
                "Network",
                "LinkRange",
                30f,
                new ConfigDescription(
                    "Maximum horizontal distance in metres for two stations of the same logical type to become direct neighbours. Vertical separation is ignored.",
                    new AcceptableValueRange<float>(1f, 50f)));

            var maxNetworkRadius = config.Bind(
                "Network",
                "MaxNetworkRadius",
                HardMaxNetworkRadius,
                new ConfigDescription(
                    "Maximum horizontal radial distance in metres from the origin station. Vertical separation is ignored. The hard cap is 150 m.",
                    new AcceptableValueRange<float>(1f, HardMaxNetworkRadius)));

            var developmentConsole = config.Bind(
                "Development",
                "ConsoleEnabled",
                true,
                "Show CraftingStationNetwork's dedicated in-game development console and write its own development log file.");

            var verboseDiagnostics = config.Bind(
                "Development",
                "VerboseDiagnostics",
                true,
                "Log build-context, station-origin and CraftingStorageLink compatibility decisions to the dedicated development console.");

            var mirrorDevelopmentLogToBepInEx = config.Bind(
                "Development",
                "MirrorToBepInEx",
                false,
                "Also mirror CraftingStationNetwork development diagnostics to the main BepInEx console.");

            return new PluginConfig(
                linkRange,
                maxNetworkRadius,
                developmentConsole,
                verboseDiagnostics,
                mirrorDevelopmentLogToBepInEx);
        }
    }
}
