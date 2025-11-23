# Vektor Voxels - Architectural Knowledge

<!-- Last Updated: 2025-11-22 -->
<!-- Source: Code audit, threading improvements, memory optimizations -->

Critical knowledge for understanding and modifying the codebase.

---

## Threading Model [VERIFIED, HIGH]

### Job System Architecture

Custom thread pool with async/await support via `VektorJob<T>`.

**Key components:**
- `GlobalThreadPool` - Singleton managing worker threads (3/4 CPU cores, min 2)
- `ThreadPool` - Work queue distribution via `BlockingCollection`
- `WorkerThread` - Progressive power states (Spinning → Yielding → Napping → Sleeping)
- `VektorJob<T>` - Captures `SynchronizationContext` for main-thread continuations

**Main thread callbacks:**
- `QueueType.Default` - Processed immediately in `Update()`
- `QueueType.Throttled` - Rate-limited in `FixedUpdate()` via `ThrottledUpdatesPerTick`

**Source:** `Threading/GlobalThreadPool.cs`, `Threading/ThreadPool.cs`

### Thread Safety

Chunks use `ReaderWriterLockSlim` for concurrent access:
- Generation/Lighting jobs acquire **write** locks
- Meshing jobs acquire **read** locks
- Jobs timeout after 10 seconds (`GlobalConstants.JOB_LOCK_TIMEOUT_MS`)

**Thread-shared flags:** State flags (`_state`, `_waitingForJob`, `_isDirty`, etc.) are marked `volatile` to ensure memory visibility across threads.

**Neighbor lock acquisition:** `LightMapper.AcquireNeighborLocks()` now uses `TryEnterReadLock()` with timeout and properly releases acquired locks on failure. Returns false if locks cannot be acquired, allowing job retry.

**Source:** `Chunks/Chunk.cs:58-68`, `Lighting/LightMapper.cs:38-97`

---

## Chunk State Machine [VERIFIED, HIGH]

Chunks progress through ordered states:

```
Uninitialized → TerrainGeneration → Lighting → WaitingForNeighbors → Meshing → Ready
                                                      ↓
                                                   Inactive (out of view)
```

### State Transitions

1. **Uninitialized → TerrainGeneration**: `Initialize()` queues `GenerationJob`
2. **TerrainGeneration → Lighting**: Generation callback queues first `LightJob`
3. **Lighting → WaitingForNeighbors**: Light pass complete, waiting for neighbors
4. **WaitingForNeighbors → Meshing**: All 8 neighbors at same light pass, queue `MeshJob`
5. **Meshing → Ready**: Mesh applied, chunk visible
6. **Ready → Inactive**: Out of view distance, rendering disabled

### Neighbor Synchronization Gate [VERIFIED, HIGH]

`WaitingForNeighbors` prevents meshing until all neighbors complete their lighting pass. This ensures edge lighting is consistent across chunk boundaries.

**Event-driven system:** Chunks now subscribe to neighbor `OnLightPassCompleted` events instead of polling every frame:
1. On entering `WaitingForNeighbors`, chunk subscribes to all loaded neighbors
2. When neighbor completes a light pass, it fires `OnLightPassCompleted`
3. Waiting chunks receive notification and check if all dependencies met
4. Fallback polling every 10 frames catches newly loaded neighbors

**Key methods:**
- `Chunk.SubscribeToNeighborEvents()` - subscribes on state entry
- `Chunk.OnNeighborLightPassCompleted()` - handles notifications
- `Chunk.CheckForNeighborState()` - validates dependencies and proceeds

**Source:** `Chunks/Chunk.cs:416-542`

### Job Counter Invalidation [VERIFIED, HIGH]

`_jobSetCounter` increments on reload. Jobs compare their captured counter against current value - mismatch means job is orphaned and should abort.

**Source:** `Chunks/Chunk.cs:62, 288`

---

## Lighting System [VERIFIED, HIGH]

### Three-Pass Architecture

1. **First pass**: Initialize sun/block light, propagate within chunk
2. **Second pass**: Propagate light spilling from neighbors (N/E boundaries)
3. **Third pass**: Propagate light spilling from neighbors (S/W boundaries)

### Propagation Algorithm

BFS flood-fill using stacks (`_sunNodes`, `_blockNodes`):
- Multiplicative attenuation via LIGHT_MULTIPLIER (220/256 per step, ~15 block range)
- Apply voxel tint from `ColorData` as pass-through multiplier (255 = full pass, 0 = block)
- Stop when all RGB channels ≤ threshold (16)
- Reject backwards propagation (node light < existing light)

**Light format:** `VoxelColor` with RGB565 (16-bit total: 5/6/5 bits per channel)

**Source:** `Lighting/LightMapper.cs:186-256`

### Sun vs Block Light

- **Sunlight**: Full intensity (255, 255, 255) above heightmap, propagates downward into caverns
- **Block light**: Point sources from voxels with `LightSource` flag, emits from `ColorData`

### Translucent Tinting [VERIFIED, HIGH]

Colored glass uses `ColorData` as direct pass-through multiplier:
- `(255, 255, 255)` = full pass (clear glass)
- `(255, 64, 64)` = pass red, block most green/blue (red glass)
- Air voxels use `VoxelColor.White()` for full pass-through

No attenuation inversion; values are intuitive (higher = more light passes).

### Data Structures

- `VoxelColor` - 16-bit RGB565 packed struct (5/6/5 bits per channel, ~32-64 levels)
- `LightNode` - Position + VoxelColor for propagation queue
- `LightData` - Combined sun + block light at a voxel (4 bytes total)
- `VoxelData.ColorData` - Uses VoxelColor for light emission and tinting
- `Color16` - Deprecated, use VoxelColor instead

---

## Meshing System [VERIFIED, HIGH]

### Vertex Layout (40 bytes)

```
Position     - float32 × 3 (12 bytes)
Normal       - float32 × 3 (12 bytes)
TexCoord0    - float32 × 2 (8 bytes)  - UV atlas coordinates
TexCoord1    - UNorm8 × 4  (4 bytes)  - Sun light as Color32
TexCoord2    - UNorm8 × 4  (4 bytes)  - Block light as Color32
```

### Texture Atlas

- 256×256 pixels total
- 16×16 per block texture
- 16 textures per row
- UV width = 1/16 = 0.0625

### Smooth Lighting [VERIFIED, HIGH]

When enabled, averages 4 corner light samples per vertex. Produces ambient occlusion as side effect.

**Source:** `Meshing/VisualMesher.cs:77, 166-191`

### Two Submeshes

- Index 0: Opaque voxels
- Index 1: Alpha/translucent voxels (water, glass)

---

## Coordinate Systems [VERIFIED, HIGH]

### Chunk ID vs Chunk Pos

- **Chunk ID**: Array index in `_chunks[,]` (0 to MaxChunks)
- **Chunk Pos**: World-centered coordinate (-MaxChunks/2 to +MaxChunks/2)

**Conversion:**
```csharp
ChunkIdFromPos(pos) = pos + MaxChunks/2
ChunkPosFromId(id)  = id - MaxChunks/2
```

### Voxel Indexing

1D array index from 3D position:
```csharp
index = x + chunkWidth * (y + chunkHeight * z)
```

Loop order for cache efficiency should be X → Y → Z (inner to outer).

**Current issue:** `VisualMesher` loops Y → Z → X, causing cache-unfriendly strided access.

---

## Critical Flags [VERIFIED, HIGH]

### Chunk._isDirty

Set true after voxel updates or generation. Triggers full reload (lighting + mesh) in `OnLateTick()`.

### Chunk._partialLoad

Set true when any neighbor is missing during `CheckForNeighborState()`. Indicates edges won't blend properly. Forces reload when chunk re-enters full view.

### Chunk._waitingForJob

Set true when job is dispatched, false on completion. Prevents overlapping jobs on same chunk.

---

## Known Critical Issues [VERIFIED, HIGH]

### Fixed Issues (2025-11-21)

1. ~~**LightMapper neighbor locks** - Blocking acquisition without timeout~~ → Now uses `TryEnterReadLock()` with timeout
2. ~~**MeshJob exception path** - Lock not released~~ → Already had proper try-finally
3. ~~**Boolean flags without volatile**~~ → State flags now marked `volatile`
4. ~~**Frame-by-frame neighbor polling**~~ → Now event-driven with fallback polling

### Remaining Deadlock Risks

1. **ThreadPool.Shutdown()** - Infinite loop with no timeout

### Memory Leaks

1. ~~**Event handler not unsubscribed**~~ → Now unsubscribes in `OnDestroy()`
2. **ReaderWriterLockSlim**: Now disposed in `OnDestroy()`

### Thread Safety

1. `Queue<VoxelUpdate>` is not thread-safe but accessed from multiple contexts

### Performance

1. `Queue.Contains()` is O(n) in hot path (`VoxelWorld.cs:240`)
2. `List.Sort()` on loaded chunks every frame
3. Cache-unfriendly loop order in meshing
4. **Improved:** Neighbor polling reduced from every-frame to every 10 frames (events handle most cases)

**Source:** `docs/initial_report.md` for full details

---

## VoxelTrace DDA Algorithm [VERIFIED, HIGH]

Digital Differential Analyzer for voxel raycasting. More efficient than PhysX for voxel grids.

**Algorithm:**
1. Calculate step direction per axis (+1, 0, -1)
2. Calculate step size (reciprocal of ray direction)
3. Initialize delta to first grid boundary on each axis
4. Loop: step along axis with smallest delta
5. Check voxel at new position

**Source:** `VoxelPhysics/VoxelTrace.cs:20-115`

---

## Memory Layout [VERIFIED, HIGH]

### Per-Chunk Memory (16×256×16 = 65,536 voxels)

| Data | Size/Voxel | Per Chunk |
|------|------------|-----------|
| VoxelData (ID + Flags + Orientation + ColorData) | 6 bytes | 384 KB |
| LightData (Sun + Block VoxelColor) | 4 bytes | 256 KB |
| HeightData | - | ~1 KB |
| **Total** | **10 bytes** | **~640 KB** |

### Memory Optimizations (2025-11-22)

1. **VoxelColor RGB565** - Reduced from 32-bit to 16-bit per color
   - Lighting memory halved: 512 KB → 256 KB per chunk
   - Quality: 32/64/32 levels per channel (sufficient, minimal banding)

2. **Color16 Deprecated** - Consolidated to single color type
   - Better precision than old 4-bit Color16 (16 levels)
   - Same memory footprint (16-bit)

3. **Future optimizations** (not yet implemented):
   - Palette compression for VoxelData (~12x reduction possible)
   - Y-section chunking (60-80% reduction for typical terrain)
   - Morton coding for cache locality (modest gains)
