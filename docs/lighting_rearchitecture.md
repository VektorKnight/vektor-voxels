# Lighting System Rearchitecture

**Goal:** Replace hybrid legacy/Unity Jobs lighting with fully coordinated Unity Jobs system.

## Current Problem

The hybrid system has race conditions:
1. Unity Jobs first pass runs synchronously
2. Legacy passes 2-3 run async on thread pool
3. Event-driven neighbor waiting is unreliable
4. Current chunk can complete before neighbor starts, using stale data

## New Architecture

### Key Insight

Instead of each chunk independently waiting for neighbors, **the scheduler orchestrates all chunks together**:

```
Pass 1: All dirty chunks run internal lighting (parallel)
        ↓ (barrier - wait for all)
Pass 2: All dirty chunks run border propagation (parallel, reads pass 1 data)
        ↓ (barrier - wait for all)
Pass 3: All dirty chunks run border propagation (parallel, reads pass 2 data)
        ↓ (barrier - wait for all)
Mesh:   All dirty chunks generate meshes
```

This eliminates race conditions because:
- All chunks complete pass N before ANY chunk starts pass N+1
- No locks needed - scheduler guarantees ordering
- No event subscriptions - explicit coordination

### New Jobs

#### 1. BorderSeedJob (new)
Reads neighbor chunk light arrays at boundaries, produces propagation seeds.

```csharp
[BurstCompile]
public struct BorderSeedJob : IJob {
    [ReadOnly] public NativeArray<VoxelData> Voxels;
    [ReadOnly] public NativeArray<VoxelColor> CurrentLight;

    // Neighbor border slices (16x256 each, pre-extracted)
    [ReadOnly] public NativeArray<VoxelColor> NeighborNorthBorder;
    [ReadOnly] public NativeArray<VoxelColor> NeighborEastBorder;
    [ReadOnly] public NativeArray<VoxelColor> NeighborSouthBorder;
    [ReadOnly] public NativeArray<VoxelColor> NeighborWestBorder;

    public NativeQueue<LightNodeNative> Seeds;

    // Scans 4 borders, creates seed nodes where neighbor light > current
}
```

#### 2. Modified LightingJobScheduler

```csharp
public class LightingJobScheduler {
    // Existing: ExecuteFirstPass (internal only)

    // NEW: Execute all passes for multiple chunks
    public void ExecuteFullLighting(List<int2> dirtyChunks) {
        // Pass 1: Internal lighting (all chunks)
        foreach (var chunk in dirtyChunks) {
            ExecuteFirstPass(chunk);
        }

        // Pass 2: Border propagation (all chunks)
        foreach (var chunk in dirtyChunks) {
            ExecuteBorderPass(chunk);
        }

        // Pass 3: Border propagation again (convergence)
        foreach (var chunk in dirtyChunks) {
            ExecuteBorderPass(chunk);
        }
    }

    private void ExecuteBorderPass(int2 chunkId) {
        // Extract neighbor border light values
        // Run BorderSeedJob
        // Run LightPropagationJob with seeds
    }
}
```

### Chunk.cs Simplification

Remove:
- `_lightPass` tracking
- `CheckForNeighborState()` polling
- `OnNeighborLightPassCompleted` event handler
- `SubscribeToNeighborEvents` / `UnsubscribeFromNeighborEvents`
- Complex `OnLateTick` state machine

Replace with:
```csharp
private void Reload() {
    // Simple: mark dirty, scheduler handles the rest
    VoxelWorld.Instance.QueueChunkForLighting(this);
}
```

### VoxelWorld Changes

Add dirty chunk tracking:
```csharp
private HashSet<Chunk> _chunksNeedingLighting;

public void QueueChunkForLighting(Chunk chunk) {
    _chunksNeedingLighting.Add(chunk);
}

void Update() {
    if (_chunksNeedingLighting.Count > 0) {
        var chunks = _chunksNeedingLighting.ToList();
        _chunksNeedingLighting.Clear();

        _lightingScheduler.ExecuteFullLighting(chunks);

        foreach (var chunk in chunks) {
            chunk.QueueMeshPass();
        }
    }
}
```

## Files to Modify

1. **LightingJobs.cs** - Add `BorderSeedJob`
2. **LightingJobScheduler.cs** - Add `ExecuteFullLighting`, `ExecuteBorderPass`
3. **Chunk.cs** - Simplify state machine, remove neighbor waiting
4. **VoxelWorld.cs** - Add dirty chunk coordination

## Files to Delete

1. `Threading/GlobalThreadPool.cs`
2. `Threading/ThreadPool.cs`
3. `Threading/WorkerThread.cs`
4. `Threading/Jobs/VektorJob.cs`
5. `Generation/GenerationJob.cs`
6. `Lighting/LightJob.cs`
7. `Meshing/MeshJob.cs`

## Migration Steps

1. Implement `BorderSeedJob` and `ExecuteBorderPass`
2. Add `ExecuteFullLighting` to scheduler
3. Update `VoxelWorld` with dirty chunk coordination
4. Simplify `Chunk.cs` state machine
5. Test lighting works
6. Delete legacy files
7. Clean up unused code (locks, callbacks, etc.)
