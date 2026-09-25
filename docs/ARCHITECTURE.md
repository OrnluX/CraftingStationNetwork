# Architecture

## Core invariant

A crafting network is resolved from an **origin station**. Only stations with the same stable logical station identity may participate.

Examples:

- workbench -> workbench: allowed;
- forge -> forge: allowed;
- stonecutter -> stonecutter: allowed;
- workbench -> forge: never allowed.

Station level is deliberately ignored for connectivity and is never propagated across the network.

## Spatial rules

Default values:

```text
LinkRange = 20 m
MaxNetworkRadius = 100 m
```

The graph may branch freely. `MaxNetworkRadius` is measured from the origin station, not by hop count.

A station participates only when:

1. it has the same logical type key as the origin;
2. its physical distance from the origin is <= 100 m;
3. there is a continuous path through same-type neighbors where every edge is <= LinkRange.

The resolver uses breadth-first traversal.

## Valheim adapter

The current Valheim assembly exposes the primitives needed by the adapter:

- `CraftingStation.m_name` is used as the logical station identity;
- `CraftingStation.m_allStations` is the source of currently loaded station instances;
- `CraftingStation.m_rangeBuild` is used when resolving hammer build coverage;
- station positions come from the Unity `Transform`.

Using Valheim's own loaded-station registry intentionally avoids maintaining a second persistent lifecycle database. When a station is destroyed or its zone unloads, it disappears from `m_allStations`; the next network resolution therefore cannot traverse through it.

No station links are persisted independently.

## Build-origin and storage-context resolution

Build permission and storage availability are treated as separate concerns.

### Station placement and stationless pieces

The existing single-network behavior remains unchanged:

1. **Explicit station**: use it directly as the network origin.
2. **Placing a station piece** (for example, a new Workbench): inspect the selected piece's `CraftingStation` component and choose the closest loaded station of the same logical type within `LinkRange` of the placement ghost. The new station may borrow materials only from a network it can actually join.
3. **Placing a non-station piece with no explicit required station**: choose the closest loaded station whose vanilla `m_rangeBuild` covers the player. Only that station's same-type network is used.
4. **No valid anchor**: do not expand CraftingStorageLink's normal behavior.

### Hammer pieces with an explicit required station

A piece such as `$piece_woodironpole` may require a Forge. Valheim / CraftingStorageLink still decide whether a valid Forge is in range; CraftingStationNetwork does not bypass that requirement.

For **storage discovery only**, `BuildStorageNetworkResolver` collects:

- the network of the explicit required station, when one exists; and
- each independent station network whose vanilla `m_rangeBuild` currently covers the player.

Those network node sets are temporarily unioned for the storage query. Their graphs are never linked to each other.

Example:

```text
Workbench A ---- Workbench B
      |               |
   chest A          player
                      |
                   Forge B ---- Forge A
                                  |
                               chest B
```

If the selected piece requires Forge, a valid Forge is still mandatory. Once that requirement is satisfied, eligible storage may come from both the Workbench network and Forge network because both cover the player's build context. `Workbench A` and `Forge A` never become graph neighbors.

This separation prevents the required-station selector from accidentally replacing the storage network context.

## CraftingStorageLink integration

Target plugin:

```text
GUID: com.custom.valheim.craftingstoragelink
Compatibility target inspected: 1.7.1
```

The dependency is soft. CraftingStationNetwork loads normally when CraftingStorageLink is absent.

The primary adapter patches two CraftingStorageLink operations:

1. `FindContainers(Player, CraftingStation, Vector3)`
   - CraftingStorageLink performs its normal local lookup first.
   - CraftingStationNetwork appends eligible containers within CraftingStorageLink's effective radius of reachable same-type station nodes.
   - Eligibility remains delegated to CraftingStorageLink's own `IsEligibleContainer` implementation.

2. `PullFromContainer(...)`
   - CraftingStorageLink revalidates distance before inventory mutation.
   - If a selected container is valid through a linked station node, the origin for that one invocation is redirected to the station that makes the container valid.
   - CraftingStorageLink still performs ownership, access, inventory mutation, persistence and transaction logic.

For hammer pieces with an explicit station requirement, `HammerBuildStorageCompat` supplements those same operations with the temporary union of independent covering station networks described above.

## Removed HUD recount

An earlier development-only `BuildHudVisualFix` attempted to reconstruct blank hammer requirement counters by independently recounting inventories and linked containers. It was removed because it duplicated CraftingStorageLink's resource-accounting logic and could disagree with the real build context.

CraftingStationNetwork no longer owns a second HUD-specific resource count. The functional container context is fixed instead, and Valheim / CraftingStorageLink remain authoritative for requirement display.

## Development diagnostics

Development diagnostics are intentionally separate from the main BepInEx console.

The plugin exposes an in-game IMGUI window and a dedicated file:

```text
BepInEx\plugins\CraftingStationNetwork\CraftingStationNetwork.dev.log
```

Verbose diagnostics trace:

- the selected hammer piece;
- explicit vs derived station origins;
- resolved network size and station type;
- CraftingStorageLink `SupportsPiece` decisions when available;
- CraftingStorageLink `GetBuildStation` decisions when available;
- entry into build-requirement / placement flows;
- local and network-added container candidates;
- the station-network set used by station-required hammer builds;
- pull-origin redirection to linked stations.

Diagnostic Harmony hooks are best-effort and never change CraftingStorageLink return values.

## Failure behavior

Compatibility fails closed.

If CraftingStorageLink is installed but its private runtime layout no longer exposes the expected methods/property/nested candidate type, the corresponding compatibility adapter logs a warning and disables itself safely. The core station graph remains loaded.

Unknown or malformed stations do not become cross-type links.

## Performance

The resolver operates on Valheim's currently loaded crafting stations only. It first restricts candidates to the origin's type and 100 m radial cap, then runs BFS across those candidates.

The station-required hammer resolver evaluates only loaded stations whose vanilla build radius covers the player, then resolves their independent networks. Duplicate station nodes are removed before container discovery.

Development diagnostics de-duplicate repeated decision messages so common per-frame checks do not flood the dedicated console.

If profiling later shows a need, a spatial index or longer-lived station graph cache can be added without changing network semantics.
