
# Chunk Pipeline Technical Audit

**Date:** 2025-11-27
**Purpose:** Deep technical analysis of the chunk pipeline for collaborative debugging of boundary propagation issues
**Audience:** Developers experienced with voxel engines and concurrent systems

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Architecture Overview](#architecture-overview)
3. [State Machine Analysis](#state-machine-analysis)
4. [Lighting System Deep Dive](#lighting-system-deep-dive)
5. [Boundary Propagation Analysis](#boundary-propagation-analysis)
6. [Known Issues & Edge Cases](#known-issues--edge-cases)
7. [Recommendations](#recommendations)

---

## Executive Summary

The Vektor Voxels chunk pipeline has undergone significant rearchitecture, moving from a custom thread pool with lock-based synchronization to Unity's Job System with Burst compilation. The current system uses **coordinated multi-pass lighting** where VoxelWorld orchestrates all dirty chunks through synchronized passes, eliminating the race conditions that plagued the previous hybrid approach.

**Key architectural decisions:**
- Chunks are 16x256x16 voxels (65,536 voxels per chunk)
- Finite world: up to 64x64 chunks resident in memory
- Three-pass lighting with barrier synchronization between passes
- No GPU lightmaps; lighting baked into vertex data (40-byte vertices)

**Current stability:** Core pipeline is functional. The legacy threading system has been fully removed. Remaining issues are concentrated in edge cases around light removal and partial chunk loading at view boundaries.

---

## Architecture Overview

### Core Components

```
VoxelWorld (singleton)
├── ChunkDataStore         - NativeArray storage for Unity Jobs
├── TerrainJobScheduler    - Burst-compiled terrain generation
├── LightingJobScheduler   - Coordinated multi-pass lighting
├── MeshingJobScheduler    - Burst-compiled mesh generation
└── Chunks[64,64]          - Managed chunk array
```

### Data Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              VoxelWorld.Update()                         │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │
                    ┌────────────────▼────────────────┐
                    │     ProcessLightingQueue()       │
                    │                                  │
                    │  1. Collect dirty chunk IDs      │
                    │  2. ExecuteFullLighting(batch)   │
                    │  3. Trigger meshing for all      │
                    └────────────────┬────────────────┘
                                     │
         ┌───────────────────────────┼───────────────────────────┐
         ▼                           ▼                           ▼
┌─────────────────┐       ┌─────────────────┐       ┌─────────────────┐
│   Pass 1:       │       │   Pass 2:       │       │   Pass 3:       │
│   Internal      │──────►│   Border        │──────►│   Border        │
│   Lighting      │barrier│   Propagation   │barrier│   Propagation   │
└─────────────────┘       └─────────────────┘       └─────────────────┘
```

### Coordinate Systems

| Term | Range | Description |
|------|-------|-------------|
| **Chunk ID** | `[0, MaxChunks)` | Array index in `_chunks[,]` |
| **Chunk Pos** | `[-MaxChunks/2, +MaxChunks/2)` | World-centered coordinate |
| **Voxel Index** | `x + 16*(y + 256*z)` | Flat 1D index into voxel arrays |

**Conversion:**
```csharp
ChunkIdFromPos(pos) = pos + MaxChunks/2
ChunkPosFromId(id)  = id - MaxChunks/2
```

---

## State Machine Analysis

### Chunk States

```
ChunkState {
    Uninitialized,      // Default, pre-initialization
    TerrainGeneration,  // Burst job generating voxels
    Lighting,           // Internal light pass executing
    WaitingForNeighbors,// Waiting for neighbor light passes
    Meshing,            // Burst job generating mesh
    Ready,              // Visible, accepting edits
    Inactive            // Out of view, rendering disabled
}
```

### Transition Diagram

```
                                ┌──────────────────┐
                                │  Uninitialized   │
                                └────────┬─────────┘
                                         │ Initialize() or InitializeWithData()
                                         ▼
                                ┌──────────────────┐
                                │TerrainGeneration │
                                └────────┬─────────┘
                                         │ OnGenerationPassComplete()
                                         │ Queues to VoxelWorld._lightingQueue
                                         ▼
                    ┌───────────────────────────────────┐
                    │         VoxelWorld                │
                    │    ProcessLightingQueue()         │
                    │    (coordinated batch lighting)   │
                    └───────────────────┬───────────────┘
                                        │ QueueMeshPassFromWorld()
                                        ▼
                                ┌──────────────────┐
                                │     Meshing      │
                                └────────┬─────────┘
                                         │ OnMeshPassComplete()
                                         ▼
                                ┌──────────────────┐
                 ┌──────────────│      Ready       │◄───────────────┐
                 │              └────────┬─────────┘                │
                 │                       │                          │
          out of view               voxel edit              back in view
                 │                       │                     │
                 ▼                       ▼                     │
        ┌──────────────────┐    ┌──────────────────┐          │
        │     Inactive     │    │   QueueChunk-    │          │
        └────────┬─────────┘    │   ForLighting()  │──────────┘
                 │              └──────────────────┘
                 │ Reload() when back in view
                 └───────────────────────────────────────────►
```

### Key Methods

| Method | Location | Responsibility |
|--------|----------|----------------|
| `Initialize()` | Chunk:128 | Fresh chunk setup, queues terrain gen |
| `InitializeWithData()` | Chunk:191 | Load from persistence, skip terrain gen |
| `OnGenerationPassComplete()` | Chunk:489 | Syncs to native, queues for lighting |
| `QueueMeshPassFromWorld()` | Chunk:480 | Entry point from VoxelWorld after lighting |
| `OnMeshPassComplete()` | Chunk:589 | Applies mesh, enables rendering |
| `Reload()` | Chunk:383 | Re-light and re-mesh (dirty or partial) |

---

## Lighting System Deep Dive

### Overview

The lighting system has two light types propagated through separate arrays:

| Type | Source | Array | Initial Value |
|------|--------|-------|---------------|
| **Sunlight** | Sky above heightmap | `SunLight[]` | White (255,255,255) above surface |
| **Block Light** | Voxels with `LightSource` flag | `BlockLight[]` | Voxel's `ColorData` |

Both use identical propagation mechanics but are stored and processed separately.

### Light Data Format

`VoxelColor` (16-bit RGB565):
- R: 5 bits (0-31, scaled to 0-255 for API)
- G: 6 bits (0-63, scaled to 0-255 for API)
- B: 5 bits (0-31, scaled to 0-255 for API)

Effective precision: ~32-64 levels per channel. Sufficient for smooth gradients, minimal banding.

### Propagation Algorithm

**BFS wavefront using `NativeQueue<LightNodeNative>`:**

```
1. Initialize seeds (cavern openings for sun, emitters for block)
2. While queue not empty:
   a. Dequeue node
   b. If node doesn't improve current light, skip (prevents revisits)
   c. Update lightmap with max(current, node)
   d. If below threshold (16), stop propagating
   e. Apply exit-tinting if source voxel is translucent
   f. For each of 6 neighbors:
      - Skip if opaque
      - Apply attenuation: value * 220/256 (~0.859x per step)
      - Apply entry-tinting if dest voxel is translucent
      - If attenuated > existing light, enqueue
```

**Attenuation math:** `255 * 0.859^15 ≈ 26`, giving ~15 blocks propagation range.

### The Three Passes

**Pass 1: Internal Lighting** (`ExecuteFirstPass`)

Jobs executed per-chunk:
1. `SunlightColumnJob` - Sets full light above heightmap, finds cavern entries
2. `BlockLightSourceJob` - Finds voxels with `LightSource` flag
3. `LightPropagationJob` - Propagates from all seeds within chunk bounds

**Pass 2 & 3: Border Propagation** (`ExecuteBorderPass`)

Jobs executed per-chunk:
1. `BorderSeedJob` - Reads neighbor light at boundaries, creates seeds where neighbor > current
2. `LightPropagationJob` - Propagates border seeds into chunk interior

**Why two border passes?**

Consider this cross-chunk scenario:
```
     Chunk A          Chunk B
┌───────────────┐ ┌───────────────┐
│               │ │               │
│       ☀       │ │               │
│       │       │ │               │
│       ▼       │ │               │
│   [light]─────┼─┼───►[shadow]   │
│               │ │               │
└───────────────┘ └───────────────┘
```

- After Pass 1: Chunk A has light, Chunk B has none
- After Pass 2: Chunk B reads A's border, propagates inward
- After Pass 3: Chunk A can read B's updated border for convergence

In practice, Pass 3 may be redundant for most scenarios but ensures convergence for corner cases where light bounces between multiple chunks.

---

## Boundary Propagation Analysis

### BorderSeedJob Implementation

The `BorderSeedJob` (`LightingJobs.cs:469-611`) is the core of cross-chunk propagation:

```csharp
// For each of 4 cardinal borders:
for each (x, y) along border:
    homeIdx = our border voxel
    neighborIdx = their adjacent border voxel

    if home voxel opaque: skip
    if neighbor light < threshold: skip
    if home light >= neighbor light: skip  // Already lit

    // Create seed with attenuated neighbor value
    seed = neighbor_light * 220/256
    Seeds.Enqueue(seed)
```

### Border Coordinate Mapping

| Direction | Our Border | Their Border | Notes |
|-----------|------------|--------------|-------|
| **North** | Z = 15 | Z = 0 | +Z direction |
| **East** | X = 15 | X = 0 | +X direction |
| **South** | Z = 0 | Z = 15 | -Z direction |
| **West** | X = 0 | X = 15 | -X direction |

### Placeholder Array Handling

Unity Jobs validates all arrays at schedule time, even if they won't be accessed. For missing neighbors:

```csharp
// LightingJobScheduler.cs:360-372
private NativeArray<VoxelColor> GetNeighborLightOrPlaceholder(...) {
    if (!store.IsAllocated(neighborId)) {
        return placeholder;  // Won't be read due to NeighborFlags check
    }
    return neighborData.SunLight or BlockLight;
}
```

The `BorderSeedJob` checks `NeighborFlags` before accessing any neighbor array, so placeholders are never actually read.

### Partial Load Handling

Chunks at the view boundary may have missing neighbors. The `_partialLoad` flag tracks this:

```csharp
// Chunk.cs:614-617
if (!VoxelWorld.Instance.IsChunkInView(neighborId)) {
    _partialLoad = true;
    continue;
}
```

When `_partialLoad` is true:
1. Chunk completes with potentially incorrect edge lighting
2. Chunk subscribes to neighbor load events
3. When neighbors become available, chunk triggers reload
4. Reload re-runs lighting with full neighbor data

---

## Known Issues & Edge Cases

### 1. Ghost Light on Light Source Removal - FIXED

**Symptom:** Removing a torch leaves residual light that doesn't decay.

**Root Cause (original hypothesis was wrong):** The issue was NOT in `LightRemovalJob` - that code is dead (never called). The actual issue: light propagates ~30 blocks (spanning 2 chunks), but only 1-hop neighbors were queued for relighting. Chunks 2 hops away kept stale light values.

**Fix implemented:** `UpdateAffectedNeighbors` now queues extended neighbors for light-affecting changes:
- 4 cardinal 1-hop neighbors (as before)
- 4 cardinal 2-hop neighbors (new)
- 4 diagonal 1-hop neighbors (new)

Up to 13 chunks can be queued per light change. HashSet dedupes if multiple changes happen.

**Dead code identified:** `LightRemovalJob`, `RemoveLight()`, and `AddLight()` in `LightingJobScheduler` are never called. The actual flow uses `ExecuteFullLighting` which clears via Pass 1's `BlockLightSourceJob` (clears all block light) and `SunlightColumnJob` (resets sunlight per column).

### 2. Three Passes May Be Redundant - TOGGLE ADDED

**Observation:** Passes 2 and 3 execute identical code (`ExecuteBorderPass`).

**Analysis:** Pass 3 IS needed for correctness when light from multiple sources converges through an intermediate chunk:
```
Chunk A (light) → Chunk B → Chunk C (light)
```
- Pass 2: B reads A and C borders, propagates inward
- Pass 3: A reads B's updated border (may now have C's light), C reads B's border (may have A's light)

**Toggle added:** `UseTwoPassLighting` flag on `LightingJobScheduler`. Press F6 in-game to toggle. Two-pass is ~33% faster but may have subtle artifacts in multi-source scenarios.

**Testing status:** Toggle implemented, needs real-world testing to determine if artifacts are noticeable.

### 3. Translucent Block Exit-Tinting at Boundaries

**Scenario:** Red glass at chunk boundary. Light exits glass, crosses boundary, enters neighbor.

**Current handling:**
- Exit-tinting applied during propagation within chunk
- Border seeds are already attenuated values, not raw exit values
- May cause slight color discrepancy at exact boundary

**Code path:**
```csharp
// LightPropagationJob.cs:281-288
if (!sourceVoxel.IsEmpty() && (sourceVoxel.Flags & VoxelFlags.AlphaRender) != 0) {
    // Apply exit tint
    sr = (nr * mr) >> 8;  // Colored light exits
}
// Propagates to neighbors with tinted values
```

### 4. Race Between Voxel Update and Lighting Queue

**Scenario:**
1. Player breaks block in Chunk A
2. `UpdateAffectedNeighbors()` queues Chunk A and neighbor B for lighting
3. Before `ProcessLightingQueue()` runs, player breaks block in Chunk B
4. Chunk B queued again (HashSet dedupes, but now has two pending changes)

**Current behavior:** HashSet prevents duplicate entries, but the lighting pass may not see the second change if it was made after the chunk was added to the queue.

**Mitigation:** The chunk's native data is synced (`SyncVoxelsToNativeData()`) before queuing, so changes are captured. However, timing-dependent issues could still occur.

### 5. Heightmap Stale After Voxel Update

**Location:** `Chunk.cs:837-838`

```csharp
_voxelData[VoxelUtility.VoxelIndex(update.Position, d)] = update.Data;
UpdateHeightMapColumn(new Vector2Int(update.Position.x, update.Position.z));
```

The heightmap is updated after the voxel, which is correct. However, `SyncVoxelsToNativeData()` at line 848 syncs voxels and heightmap together, so order is preserved.

---

## Recommendations

### Short-term (Bug Fixes)

1. **Light Removal Cascade**
   - Extend `LightRemovalJob` to output boundary invalidation requests
   - Add `RemovalBorderPropagation` pass that runs after local removal
   - Or: simpler approach of full re-light for affected + neighbor chunks

2. **Test Pass 3 Removal**
   - Add flag to skip Pass 3
   - Profile and validate with stress tests
   - If equivalent, remove for 33% lighting performance gain

### Medium-term (Architecture)

3. **Event-Based Dirty Tracking**
   - Replace polling-based partial load check with proper event subscription
   - `VoxelWorld.OnChunkLoaded` event for newly loaded chunks
   - Chunk subscribes only when in partial state

4. **Incremental Light Updates**
   - Track modified positions, not just dirty chunks
   - `LightUpdateJob` that only re-propagates from changed positions
   - Significant perf win for single-voxel edits

### Long-term (Optimization)

5. **Batched Job Scheduling**
   - Current: Sequential `Complete()` calls per pass
   - Better: Schedule all Pass 1 jobs, combine handles, single `Complete()`
   - Use `JobHandle.CombineDependencies()` for parallel execution

6. **Border Array Pre-extraction**
   - Currently reading full 65K arrays to access 4K border values
   - Extract borders once, pass 16x256 slices to jobs
   - Reduces memory bandwidth ~15x for border passes

---

## Appendix: Key File Reference

| File | Lines | Purpose |
|------|-------|---------|
| `Chunks/Chunk.cs` | ~1000 | State machine, tick updates, native bridge |
| `World/VoxelWorld.cs` | ~750 | World management, lighting queue coordination |
| `Jobs/LightingJobs.cs` | ~615 | Burst jobs: column, source, propagation, removal, border |
| `Jobs/LightingJobScheduler.cs` | ~390 | Multi-pass orchestration |
| `Lighting/LightMapper.cs` | ~480 | Legacy propagation (still used for some paths) |
| `Data/ChunkDataStore.cs` | ~300 | NativeArray storage management |

---

## Changelog

| Date | Author | Notes |
|------|--------|-------|
| 2025-11-27 | Claude | Initial audit for boundary propagation discussion |
| 2025-11-27 | Arya   | Review and for consistency and accuracy           |