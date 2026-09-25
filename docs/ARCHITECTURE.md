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
- `CraftingStation.m_rangeBuild` is used when resolving stationless hammer-placement contexts;
- station positions come from the Unity `Transform`.

Using Valheim's own loaded-station registry intentionally avoids maintaining a second persistent lifecycle database. When a station is destroyed or its zone unloads, it disappears from `m_allStations`; the next network resolution therefore cannot traverse through it.

No station links are persisted independently.

### Build-origin resolution

CraftingStorageLink sometimes supplies an explicit `CraftingStation` and sometimes legitimately supplies `null` while a hammer piece is being placed. The adapter resolves those cases conservatively:

1. **Explicit station**: use it directly as the origin.
2. **Placing a station piece** (for example, a new Workbench): inspect the selected piece's `CraftingStation` component and choose the closest loaded station of the same logical type within `LinkRange` of the placement ghost. The new station therefore borrows materials only from a network it can actually join.
3. **Placing a non-station piece with no explicit required station**: choose the closest loaded station whose vanilla `m_rangeBuild` covers the player. Only that station's same-type network is used; overlapping station types are not merged.
4. **No valid anchor**: do not expand CraftingStorageLink's normal behavior.

## CraftingStorageLink integration

Target plugin:

```text
GUID: com.custom.valheim.craftingstoragelink
Compatibility target inspected: 1.7.1
```

The dependency is soft. CraftingStationNetwork loads normally when CraftingStorageLink is absent.

When present, the adapter patches two functional CraftingStorageLink operations at runtime:

1. `FindContainers(Player, CraftingStation, Vector3)`
   - CraftingStorageLink first performs its normal local container lookup.
   - The postfix resolves the station network and appends eligible containers that are within CraftingStorageLink's own effective radius of any reachable same-type station.
   - Container eligibility is delegated back to CraftingStorageLink's own `IsEligibleContainer` implementation so its access, ward, ownership and in-use rules remain authoritative.

2. `PullFromContainer(...)`
   - CraftingStorageLink revalidates distance before actually removing items.
   - If the selected container is not close enough to the original station but is close enough to another reachable same-type station, the prefix substitutes that linked station's position only for this invocation's origin argument.
   - CraftingStorageLink still performs the actual ownership, inventory mutation, persistence and transaction logic.

This keeps CraftingStationNetwork out of third-party inventory accounting and minimizes compatibility risk.

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
- CraftingStorageLink `SupportsPiece` decisions when the method is available;
- CraftingStorageLink `GetBuildStation` decisions when the method is available;
- entry into build-requirement / placement flows;
- number of local and network-added container candidates;
- pull-origin redirection to linked stations.

Diagnostic Harmony hooks are best-effort and never change CraftingStorageLink return values. If a private method shape changes, only that diagnostic hook is skipped.

## Failure behavior

Compatibility fails closed.

If CraftingStorageLink is installed but its private runtime layout no longer exposes the expected methods/property/nested candidate type, the adapter logs a warning and disables only the CraftingStorageLink integration. The core mod remains loaded.

Unknown or malformed stations likewise do not become cross-type links.

## Performance

The network resolver operates on Valheim's currently loaded crafting stations only. It first restricts candidates to the origin's type and 100 m radial cap, then runs BFS across those candidates.

CraftingStorageLink already caches repeated container discovery within a frame. The compatibility adapter also avoids repeating its network expansion when the same cached result object is returned for the same station in the same frame.

Development diagnostics de-duplicate repeated decision messages so common per-frame checks do not flood the dedicated console.

If profiling later shows a need, a spatial index or longer-lived station graph cache can be added without changing network semantics.
