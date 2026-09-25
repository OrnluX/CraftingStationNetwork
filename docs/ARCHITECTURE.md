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
- station positions come from the Unity `Transform`.

Using Valheim's own loaded-station registry intentionally avoids maintaining a second persistent lifecycle database. When a station is destroyed or its zone unloads, it disappears from `m_allStations`; the next network resolution therefore cannot traverse through it.

No station links are persisted independently.

## CraftingStorageLink integration

Target plugin:

```text
GUID: com.custom.valheim.craftingstoragelink
Compatibility target inspected: 1.7.1
```

The dependency is soft. CraftingStationNetwork loads normally when CraftingStorageLink is absent.

When present, the adapter patches two internal CraftingStorageLink operations at runtime:

1. `FindContainers(Player, CraftingStation, Vector3)`
   - CraftingStorageLink first performs its normal local container lookup.
   - The postfix resolves the station network and appends eligible containers that are within CraftingStorageLink's own effective radius of any reachable same-type station.
   - Container eligibility is delegated back to CraftingStorageLink's own `IsEligibleContainer` implementation so its access, ward, ownership and in-use rules remain authoritative.

2. `PullFromContainer(...)`
   - CraftingStorageLink revalidates distance before actually removing items.
   - If the selected container is not close enough to the original station but is close enough to another reachable same-type station, the prefix substitutes that linked station's position only for this invocation's origin argument.
   - CraftingStorageLink still performs the actual ownership, inventory mutation, persistence and transaction logic.

This keeps CraftingStationNetwork out of third-party inventory accounting and minimizes compatibility risk.

## Failure behavior

Compatibility fails closed.

If CraftingStorageLink is installed but its private runtime layout no longer exposes the expected methods/property/nested candidate type, the adapter logs a warning and disables only the CraftingStorageLink integration. The core mod remains loaded.

Unknown or malformed stations likewise do not become cross-type links.

## Performance

The network resolver operates on Valheim's currently loaded crafting stations only. It first restricts candidates to the origin's type and 100 m radial cap, then runs BFS across those candidates.

CraftingStorageLink already caches repeated container discovery within a frame. The compatibility adapter also avoids repeating its network expansion when the same cached result object is returned for the same station in the same frame.

If profiling later shows a need, a spatial index or longer-lived station graph cache can be added without changing network semantics.
