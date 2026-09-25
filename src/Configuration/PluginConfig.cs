using BepInEx.Configuration;

namespace CraftingStationNetwork.Configuration
{
    internal sealed class PluginConfig
    {
        private const float HardMaxNetworkRadius = 100f;

        private PluginConfig(
            ConfigEntry<float> linkRange,
            ConfigEntry<float> maxNetworkRadius)
        {
            LinkRange = linkRange;
            MaxNetworkRadius = maxNetworkRadius;
        }

        internal ConfigEntry<float> LinkRange { get; }
        internal ConfigEntry<float> MaxNetworkRadius { get; }

        internal static PluginConfig Bind(ConfigFile config)
        {
            var linkRange = config.Bind(
                "Network",
                "LinkRange",
                20f,
                new ConfigDescription(
                    "Maximum distance in metres for two stations of the same logical type to become direct neighbours.",
                    new AcceptableValueRange<float>(1f, 50f)));

            var maxNetworkRadius = config.Bind(
                "Network",
                "MaxNetworkRadius",
                HardMaxNetworkRadius,
                new ConfigDescription(
                    "Maximum radial distance in metres from the origin station. The hard cap is 100 m.",
                    new AcceptableValueRange<float>(1f, HardMaxNetworkRadius)));

            return new PluginConfig(linkRange, maxNetworkRadius);
        }
    }
}
