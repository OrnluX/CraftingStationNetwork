# CraftingStationNetwork

BepInEx mod for Valheim that links crafting stations of the **same logical type** into a bounded radial network.

## Design goals

- Workbench links only to workbenches, forge only to forges, stonecutter only to stonecutters, and so on.
- Station level does **not** affect connectivity and is never shared by the network.
- Neighboring stations link when they are within **20 m** by default.
- Network traversal is radial and may branch freely.
- A network is bounded to **100 m from the station being used**.
- Destroying or unloading a station must invalidate it; no permanent ghost links are stored.
- Compatibility with other mods is a first-class concern. Integrations are optional adapters rather than hard dependencies.
- Initial compatibility target: **CraftingStorageLink 1.7.1** (`com.custom.valheim.craftingstoragelink`).

## Current status

Early development scaffold. The pure network model and plugin shell are being established before patching Valheim's current `CraftingStation` implementation.

The first playable milestone will:

1. identify loaded crafting stations by stable logical identity;
2. build same-type links within the configured link range;
3. resolve the reachable radial network within 100 m of the origin station;
4. recalculate safely when stations appear, disappear, or unload;
5. expose the resolved network to optional compatibility adapters.

`CraftingStorageLink` integration will be implemented only after validating the exact Valheim 1.0 method signatures and the mod's runtime hooks.

## Development requirements

- Valheim
- BepInEx 5 for Valheim
- .NET SDK capable of building `net472`

The project does not commit game DLLs. Point MSBuild at your local Valheim installation:

```powershell
dotnet build -p:ValheimDir="D:\SteamLibrary\steamapps\common\Valheim"
```

To copy the compiled DLL directly into the local BepInEx plugins folder:

```powershell
dotnet build `
  -p:ValheimDir="D:\SteamLibrary\steamapps\common\Valheim" `
  -p:DeployToValheim=true
```

The deployment target is:

```text
BepInEx\plugins\CraftingStationNetwork\CraftingStationNetwork.dll
```

## Default network rules

```ini
[Network]
LinkRange = 20
MaxNetworkRadius = 100
```

`LinkRange` controls whether two same-type stations are neighbors. `MaxNetworkRadius` limits participating stations by their physical distance from the station that originated the query; it is not a hop count and does not prevent branching.
