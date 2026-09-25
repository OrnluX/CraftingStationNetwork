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

The playable runtime integration currently provides:

- Valheim stations identified by their logical `CraftingStation.m_name` key;
- loaded stations read from Valheim's `CraftingStation.m_allStations` registry;
- same-type radial BFS using `LinkRange` and the 100 m network cap;
- soft-dependency integration with `CraftingStorageLink`;
- container search extended to storage near reachable same-type station nodes;
- final pull-distance validation redirected to the linked station that makes a selected container valid, while leaving CraftingStorageLink's own access, ownership and transaction safeguards intact;
- station pieces such as a new Workbench may borrow materials only from an existing same-type network that the new station is close enough to join;
- hammer pieces with no explicit crafting-station requirement keep the tested single-network anchor behavior;
- hammer pieces that **do require a station** keep that station requirement intact, but their storage query may use the independent station networks whose vanilla build ranges currently cover the player. Those networks are queried together for storage only; Workbench, Forge, Stonecutter and other station graphs remain separate.

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

For hammer pieces that require a station, storage availability and build permission are intentionally separated. For example, while standing in overlapping Workbench and Forge coverage, a Forge-required piece still needs a valid Forge, but eligible storage may be discovered through both the Workbench network and Forge network without linking those two graphs together.

## Development diagnostics

Development builds expose a dedicated in-game diagnostics window, separate from the normal BepInEx console. It traces station-origin resolution, CraftingStorageLink build-context decisions, container expansion and pull redirection.

It also writes a dedicated file at runtime:

```text
BepInEx\plugins\CraftingStationNetwork\CraftingStationNetwork.dev.log
```

Default development settings:

```ini
[Development]
ConsoleEnabled = true
VerboseDiagnostics = true
MirrorToBepInEx = false
```

`MirrorToBepInEx=false` keeps the verbose CraftingStationNetwork trace out of the main mod-manager console unless explicitly enabled.

The development console includes **Copy All** so a complete diagnostic run can be copied directly to the clipboard.

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
