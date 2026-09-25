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

The resolver uses breadth-first traversal over the loaded station registry.

## Lifecycle

The runtime adapter will register loaded stations and remove them when destroyed or unloaded. The core network is derived from the current registry; links are not persisted independently.

This guarantees that removing a bridge station can split one network into multiple networks without leaving ghost connections.

## Valheim adapter

The Valheim-facing layer still needs to be implemented against the exact current `assembly_valheim.dll`.

Its responsibilities will be:

- derive a stable logical identity for each `CraftingStation` instance;
- map Unity positions to `StationPosition`;
- register/unregister loaded stations;
- expose `NetworkResolver` results to Harmony patches and compatibility adapters.

Unknown station types should fail closed: if a station cannot be identified safely, it should not be linked automatically.

## Compatibility policy

Compatibility code lives outside the core graph implementation.

A third-party mod must remain optional; CraftingStationNetwork must load without it. Detection is performed through BepInEx plugin metadata, and integration code is activated only when the expected plugin is installed.

### CraftingStorageLink

Initial target:

```text
GUID: com.custom.valheim.craftingstoragelink
Version tested/planned: 1.7.1
```

The intended integration is to preserve CraftingStorageLink's own inventory accounting, permissions, transactions, validation and rollback behavior while broadening its valid station origins from one station to all reachable same-type stations in the resolved network.

No third-party DLL is committed to this repository and no hard compile-time dependency should be introduced unless there is a compelling reason.

## Performance

The initial resolver is intentionally simple and deterministic. For normal base sizes, scanning loaded same-type stations during a network query is cheap. If profiling later shows a need, a spatial index can be introduced without changing the public network semantics.
