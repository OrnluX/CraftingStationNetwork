# CraftingStationNetwork

BepInEx mod for Valheim that links crafting stations of the **same logical type** into a bounded radial network.

## Design goals

- Workbench links only to workbenches, forge only to forges, stonecutter only to stonecutters, and so on.
- Station level does **not** affect connectivity and is never shared by the network.
- Neighboring stations link when they are within **20 m** by default.
- Network traversal is radial and may branch freely.
- A network is bounded to **100 m from the station being used**.
- Destroyed or unloaded stations disappear naturally because the runtime network is resolved from Valheim's currently loaded `CraftingStation` registry; no permanent ghost links are stored.
- Compatibility with other mods is a first-class concern. Integrations are optional adapters rather than hard dependencies.
- Initial compatibility target: **CraftingStorageLink 1.7.1** (`com.custom.valheim.craftingstoragelink`).

## Current status

The first runtime integration is implemented on the development branch:

- Valheim stations are identified by their logical `CraftingStation.m_name` key;
- loaded stations are read from Valheim's `CraftingStation.m_allStations` registry;
- same-type stations are resolved with radial BFS using `LinkRange` and the 100 m network cap;
- `CraftingStorageLink` is detected as a soft dependency;
- its container search is extended to storage near every reachable same-type station;
- its final pull-distance validation is redirected to the linked station that makes the container valid, while leaving CraftingStorageLink's own access, ownership and transaction safeguards intact.

This does **not** share station levels, recipes or upgrades. A level-1 workbench stays level 1 even when connected to a level-5 workbench.

## Example

```text
                chest
                  |
             Workbench C
                 / \
                /   \
Workbench A -- B     D
     ^
   player
```

If every workbench-to-workbench edge is within `LinkRange` and every participating station is within 100 m of Workbench A, CraftingStorageLink may use eligible containers near A, B, C and D while the player crafts at A.

Forge networks, stonecutter networks and other station types remain completely separate.

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
