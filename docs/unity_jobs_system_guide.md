# Unity Jobs & Burst System Guide

This document explains the Unity Jobs and Burst-based systems implemented in VektorVoxels during the November 2025 rearchitecture. It's designed to help you understand both the Unity technologies used and how they're applied in this codebase.

---

## Table of Contents

1. [Overview](#overview)
2. [Unity Jobs System Primer](#unity-jobs-system-primer)
3. [Burst Compiler Primer](#burst-compiler-primer)
4. [Native Collections](#native-collections)
5. [Architecture Overview](#architecture-overview)
6. [Data Layer: ChunkDataStore](#data-layer-chunkdatastore)
7. [Terrain Generation Jobs](#terrain-generation-jobs)
8. [Lighting Jobs](#lighting-jobs)
9. [Meshing Jobs](#meshing-jobs)
10. [The Dual-Write Bridge Pattern](#the-dual-write-bridge-pattern)
11. [Key Patterns and Gotchas](#key-patterns-and-gotchas)
12. [Performance Considerations](#performance-considerations)

---

## Overview

### What Was Built

The rearchitecture replaced the custom thread pool (`GlobalThreadPool`) with Unity's Job System for three core systems:

| System | Old Approach | New Approach |
|--------|-------------|--------------|
| Terrain Generation | `GenerationJob` on thread pool | `TerrainGenerationJob` (Burst IJobParallelFor) |
| Lighting (First Pass) | `LightJob` on thread pool | `SunlightColumnJob`, `BlockLightSourceJob`, `LightPropagationJob` (Burst) |
| Lighting (Passes 2-3) | `LightJob` on thread pool | Still uses legacy system (boundary propagation) |
| Meshing | `MeshJob` on thread pool | `VisualMeshingJob` (Burst IJob) |

### Why This Matters

1. **Performance**: Burst compiles C# to highly optimized native code with SIMD vectorization
2. **Safety**: The Job System prevents race conditions at compile time
3. **Scheduling**: Unity handles thread management and work stealing
4. **GC Pressure**: Native collections don't trigger garbage collection

---

## Unity Jobs System Primer

### What Is a Job?

A job is a unit of work that runs on a worker thread. Unity provides several job interfaces:

```csharp
// Single unit of work (runs once)
public struct MyJob : IJob {
    public void Execute() { /* work */ }
}

// Parallel work over an array (runs N times in parallel)
public struct MyParallelJob : IJobParallelFor {
    public void Execute(int index) { /* work for index */ }
}
```

### Scheduling Jobs

Jobs are scheduled, not executed immediately:

```csharp
var job = new MyJob { /* parameters */ };
JobHandle handle = job.Schedule();  // Returns immediately

// Later...
handle.Complete();  // Blocks until job finishes
```

For parallel jobs:

```csharp
var job = new MyParallelJob { /* parameters */ };
// Schedule 256 iterations, batched in groups of 16
JobHandle handle = job.Schedule(256, 16);
```

### Job Dependencies

Jobs can depend on other jobs:

```csharp
JobHandle first = jobA.Schedule();
JobHandle second = jobB.Schedule(first);  // Waits for jobA
JobHandle combined = JobHandle.CombineDependencies(handleA, handleB);
```

### Thread Safety Rules

The Job System enforces safety at compile time:

1. **No Reference Types**: Jobs can't contain classes, only structs
2. **No Static Access**: Can't access static fields (except readonly)
3. **Container Safety**: Native containers track read/write access

```csharp
public struct MyJob : IJob {
    [ReadOnly] public NativeArray<int> Input;   // Read-only access
    [WriteOnly] public NativeArray<int> Output; // Write-only access
    public NativeArray<int> ReadWrite;          // Full access (exclusive)
}
```

---

## Burst Compiler Primer

### What Is Burst?

Burst is a compiler that transforms C# jobs into highly optimized native code. It:

- Eliminates managed overhead
- Auto-vectorizes loops (SIMD: processes 4-8 values at once)
- Inlines aggressively
- Produces code comparable to hand-written C++

### Enabling Burst

Add the `[BurstCompile]` attribute:

```csharp
[BurstCompile]
public struct MyJob : IJob {
    public void Execute() { /* Burst-compiled code */ }
}
```

### Burst Restrictions

Burst has stricter rules than regular C#:

| Allowed | Not Allowed |
|---------|-------------|
| Structs | Classes |
| NativeArray, NativeList, etc. | Managed arrays (`int[]`) |
| `Unity.Mathematics` types | `System.Numerics` |
| Static readonly fields | Static mutable fields |
| Function pointers | Delegates |

### Unity.Mathematics

Burst works best with `Unity.Mathematics` types:

```csharp
using Unity.Mathematics;

int3 position;           // Instead of Vector3Int
float3 velocity;         // Instead of Vector3
float4 color;            // RGBA as floats
int2 chunkId;            // Instead of Vector2Int
```

These types are designed for SIMD and have no managed overhead.

---

## Native Collections

### Why Native Collections?

Regular C# collections (`List<T>`, arrays) are managed by the garbage collector. Native collections:

- Live in unmanaged memory (no GC)
- Can be shared with jobs safely
- Must be manually disposed

### Common Native Types

```csharp
// Fixed-size array
NativeArray<VoxelData> voxels = new NativeArray<VoxelData>(65536, Allocator.Persistent);

// Dynamic list
NativeList<Vertex> vertices = new NativeList<Vertex>(1024, Allocator.Persistent);

// Thread-safe queue
NativeQueue<LightNode> queue = new NativeQueue<LightNode>(Allocator.Persistent);
```

### Allocator Lifetimes

| Allocator | Lifetime | Use Case |
|-----------|----------|----------|
| `Temp` | 1 frame | Temporary calculations |
| `TempJob` | 4 frames | Data passed to jobs |
| `Persistent` | Until disposed | Long-lived data |

**Important**: `Temp` allocator CANNOT be used with scheduled jobs. Use `TempJob` minimum.

### Disposal

Native collections MUST be disposed:

```csharp
public void Dispose() {
    if (_voxels.IsCreated) _voxels.Dispose();
    if (_vertices.IsCreated) _vertices.Dispose();
}
```

---

## Architecture Overview

### System Components

```
┌─────────────────────────────────────────────────────────────────┐
│                         VoxelWorld                               │
│  (Manages schedulers, toggles, and chunk lifecycle)             │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────────┐  ┌──────────────────┐  ┌───────────────┐ │
│  │ TerrainScheduler │  │ LightingScheduler│  │MeshingScheduler│ │
│  │                  │  │                  │  │               │ │
│  │ Schedules        │  │ Schedules        │  │ Schedules     │ │
│  │ terrain gen jobs │  │ lighting jobs    │  │ meshing jobs  │ │
│  └────────┬─────────┘  └────────┬─────────┘  └───────┬───────┘ │
│           │                     │                     │         │
│           ▼                     ▼                     ▼         │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │                     ChunkDataStore                        │  │
│  │  (Native arrays for all chunk data - voxels, light, etc.) │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Data Flow

```
Chunk Lifecycle:

  Initialize ──► TerrainGen ──► Lighting ──► Meshing ──► Ready
                    │              │            │
                    ▼              ▼            ▼
               Native arrays  Native arrays  Mesh.MeshDataArray
               (voxels,       (sunlight,     (vertices,
                heightmap)     blocklight)    indices)
```

---

## Data Layer: ChunkDataStore

**File**: `Assets/VektorVoxels/Data/ChunkDataStore.cs`

### Purpose

Centralized storage for all chunk data in native arrays. This allows jobs to access chunk data without going through managed Chunk objects.

### Structure

```csharp
public struct ChunkData {
    public NativeArray<VoxelData> Voxels;      // 16*256*16 = 65,536
    public NativeArray<VoxelColor> SunLight;   // Same size
    public NativeArray<VoxelColor> BlockLight; // Same size
    public NativeArray<HeightData> HeightMap;  // 16*16 = 256
}
```

### Key Operations

```csharp
// Allocate storage for a chunk
store.AllocateChunk(new int2(chunkX, chunkZ));

// Get chunk data for a job
ChunkData data = store.GetChunk(new int2(x, z));

// Copy between managed and native arrays
store.CopyVoxelsFrom(chunkId, managedVoxelArray);  // Managed → Native
store.CopyVoxelsTo(chunkId, managedVoxelArray);    // Native → Managed

// Clean up
store.DeallocateChunk(chunkId);
store.Dispose();  // Disposes ALL chunks
```

### Why This Pattern?

Jobs can't access managed objects (like `Chunk` class instances). The store provides:

1. **Native-only data path**: Jobs read/write NativeArrays directly
2. **Centralized lifetime management**: One place to allocate/dispose
3. **Efficient lookups**: Dictionary from chunk ID to data

---

## Terrain Generation Jobs

**Files**:
- `Assets/VektorVoxels/Jobs/TerrainGenerationJob.cs`
- `Assets/VektorVoxels/Jobs/TerrainJobScheduler.cs`

### TerrainGenerationJob

A parallel job that generates terrain one column at a time:

```csharp
[BurstCompile]
public struct TerrainGenerationJob : IJobParallelFor {
    // World parameters
    public int2 ChunkWorldPosition;
    public int Seed;
    public float NoiseScale;
    // ... more parameters

    // Output arrays
    [NativeDisableParallelForRestriction]
    public NativeArray<VoxelData> Voxels;

    [NativeDisableParallelForRestriction]
    public NativeArray<HeightData> HeightMap;

    public void Execute(int index) {
        // index = column number (0-255 for 16x16 chunk)
        int x = index % 16;
        int z = index / 16;

        // Generate height using noise
        float height = CalculateHeight(x, z);

        // Fill column with voxels
        FillColumn(x, z, height);
    }
}
```

**Key Attribute**: `[NativeDisableParallelForRestriction]`

Normally, parallel jobs can only write to index `i` in iteration `i`. This attribute allows writing to any index - necessary because one column iteration writes multiple Y values.

### TerrainJobScheduler

Manages job lifecycle:

```csharp
public class TerrainJobScheduler {
    public void ScheduleGeneration(Vector2Int chunkId, Action<Vector2Int> callback) {
        // 1. Get native arrays from store
        var data = _store.GetChunk(nativeId);

        // 2. Create and schedule job
        var job = new TerrainGenerationJob {
            Voxels = data.Voxels,
            HeightMap = data.HeightMap,
            // ... parameters
        };

        // 256 columns, batched in groups of 16
        JobHandle handle = job.Schedule(256, 16);

        // 3. Track pending job
        _pendingJobs.Add(new PendingJob {
            Handle = handle,
            Callback = callback
        });
    }

    public void Update() {
        // Check for completed jobs, invoke callbacks
    }
}
```

---

## Lighting Jobs

**Files**:
- `Assets/VektorVoxels/Jobs/LightingJobs.cs`
- `Assets/VektorVoxels/Jobs/LightingJobScheduler.cs`

### Overview

Lighting is split into multiple jobs for the first pass:

```
First Pass Pipeline:

  SunlightColumnJob ──┬──► LightPropagationJob (sun)
                      │
  BlockLightSourceJob ─┴──► LightPropagationJob (block)
```

### SunlightColumnJob

Parallel job that initializes sunlight columns:

```csharp
[BurstCompile]
public struct SunlightColumnJob : IJobParallelFor {
    [ReadOnly] public NativeArray<VoxelData> Voxels;
    [ReadOnly] public NativeArray<HeightData> HeightMap;

    [NativeDisableParallelForRestriction]
    public NativeArray<VoxelColor> SunLight;

    public NativeQueue<LightNodeNative>.ParallelWriter CavernSeeds;

    public void Execute(int index) {
        int x = index % 16;
        int z = index / 16;
        int height = HeightMap[index].Value;

        // White light above surface
        for (int y = height + 1; y < 256; y++) {
            SunLight[VoxelIndex(x, y, z)] = FullLight;
        }

        // Black below surface (propagation fills it)
        for (int y = 0; y <= height; y++) {
            SunLight[VoxelIndex(x, y, z)] = NoLight;
        }

        // If surface is glass, place seed for propagation
        if (IsTranslucent(heightVoxel)) {
            CavernSeeds.Enqueue(new LightNodeNative(x, height, z, FullLight));
        }

        // Detect cavern openings, place seeds
        DetectCaverns(x, z, height);
    }
}
```

**Key Type**: `NativeQueue<T>.ParallelWriter`

Multiple threads can safely enqueue to the same queue using a parallel writer. This is how column jobs collect seeds for the propagation phase.

### LightPropagationJob

Single-threaded BFS flood-fill:

```csharp
[BurstCompile]
public struct LightPropagationJob : IJob {
    [ReadOnly] public NativeArray<VoxelData> Voxels;
    public NativeArray<VoxelColor> LightMap;
    public NativeQueue<LightNodeNative> PropagationQueue;

    public void Execute() {
        while (PropagationQueue.Count > 0) {
            var node = PropagationQueue.Dequeue();
            PropagateFromNode(node);
        }
    }

    void PropagateFromNode(LightNodeNative node) {
        // Write light to current position
        LightMap[index] = VoxelColor.Max(current, node.Value);

        // Exit-tinting: if source is translucent, tint the outgoing light
        if (IsTranslucent(sourceVoxel)) {
            light = TintByVoxelColor(light, sourceVoxel);
        }

        // Propagate to 6 neighbors with attenuation
        for each neighbor {
            var attenuated = Attenuate(light);

            // Entry-tinting: if destination is translucent, tint
            if (IsTranslucent(destVoxel)) {
                attenuated = TintByVoxelColor(attenuated, destVoxel);
            }

            PropagationQueue.Enqueue(new LightNodeNative(neighborPos, attenuated));
        }
    }
}
```

### Light Tinting (Glass)

Light passing through colored glass gets tinted. This happens in two places:

1. **Exit-tinting**: When light leaves a translucent block
2. **Entry-tinting**: When light enters a translucent block

```
          ┌─────────┐
  White   │  Red    │   Red
  Light ──►  Glass  ├──► Light
          │         │
          └─────────┘

  Entry-tint: white × red = red (entering glass)
  Exit-tint:  white × red = red (leaving glass)
```

Both are needed because seeds can be placed inside glass (sunlight through glass at surface).

---

## Meshing Jobs

**Files**:
- `Assets/VektorVoxels/Jobs/MeshingJobs.cs`
- `Assets/VektorVoxels/Jobs/MeshingJobScheduler.cs`

### VisualMeshingJob

Generates mesh data for a chunk:

```csharp
[BurstCompile]
public struct VisualMeshingJob : IJob {
    // Input
    [ReadOnly] public NativeArray<VoxelData> Voxels;
    [ReadOnly] public NativeArray<VoxelColor> SunLight;
    [ReadOnly] public NativeArray<VoxelColor> BlockLight;
    [ReadOnly] public NativeArray<VoxelTextureRects> TextureRects;

    // Neighbor light data for boundary faces
    [ReadOnly] public NativeArray<VoxelColor> NeighborNorthSunLight;
    [ReadOnly] public NativeArray<VoxelColor> NeighborNorthBlockLight;
    // ... other neighbors

    // Output
    public NativeList<VertexNative> Vertices;
    public NativeList<uint> OpaqueIndices;
    public NativeList<uint> AlphaIndices;

    public void Execute() {
        for each voxel {
            for each face {
                if (FaceShouldRender(voxel, face)) {
                    AddFace(voxel, face);
                }
            }
        }
    }
}
```

### Neighbor Light Sampling

Boundary faces need light from neighboring chunks:

```csharp
void SampleLight(int3 pos, out VoxelColor sun, out VoxelColor block) {
    if (InBounds(pos)) {
        // Sample from this chunk
        sun = SunLight[VoxelIndex(pos)];
        block = BlockLight[VoxelIndex(pos)];
    }
    else if (pos.z >= 16 && HasNorthNeighbor) {
        // Sample from north neighbor
        var wrapped = new int3(pos.x, pos.y, pos.z - 16);
        sun = NeighborNorthSunLight[VoxelIndex(wrapped)];
        block = NeighborNorthBlockLight[VoxelIndex(wrapped)];
    }
    // ... other directions
}
```

### Mesh.MeshDataArray

Unity's API for creating meshes without managed allocations:

```csharp
// Allocate writable mesh data
Mesh.MeshDataArray meshData = Mesh.AllocateWritableMeshData(1);

// Job writes to it
job.Execute();  // Fills meshData

// Apply to actual mesh
Mesh.ApplyAndDisposeWritableMeshData(meshData, targetMesh);
```

---

## The Dual-Write Bridge Pattern

### The Problem

We have two data paths:
1. **Managed**: `Chunk.VoxelData[]`, `Chunk.SunLight[]` - used by legacy systems
2. **Native**: `ChunkDataStore` NativeArrays - used by jobs

Both need to stay in sync.

### The Solution

Explicit sync points in the chunk lifecycle:

```csharp
// After terrain generation (native → managed)
store.CopyVoxelsTo(chunkId, chunk.VoxelData);
store.CopyHeightMapTo(chunkId, chunk.HeightMap);

// After lighting first pass (native → managed)
store.CopySunLightTo(chunkId, chunk.SunLight);
store.CopyBlockLightTo(chunkId, chunk.BlockLight);

// After legacy lighting passes 2-3 (managed → native)
store.CopySunLightFrom(chunkId, chunk.SunLight);
store.CopyBlockLightFrom(chunkId, chunk.BlockLight);

// Before meshing (ensure native is up-to-date)
SyncLightToNativeData();
```

### Sync Methods in Chunk.cs

```csharp
public void SyncToNativeData()       // All data: managed → native
public void SyncFromNativeData()     // All data: native → managed
public void SyncVoxelsToNativeData() // Voxels + height only
public void SyncLightToNativeData()  // Light only
```

---

## Key Patterns and Gotchas

### 1. Allocator Lifetime

```csharp
// WRONG - Temp can't be used with jobs
var array = new NativeArray<int>(100, Allocator.Temp);
job.Data = array;
job.Schedule();  // CRASH!

// RIGHT - Use TempJob or Persistent
var array = new NativeArray<int>(100, Allocator.TempJob);
```

### 2. Job Completion

```csharp
// WRONG - Accessing data before job completes
job.Schedule();
var value = job.Output[0];  // RACE CONDITION!

// RIGHT - Wait for completion
JobHandle handle = job.Schedule();
handle.Complete();
var value = job.Output[0];  // Safe
```

### 3. Parallel Write Restrictions

```csharp
// WRONG - Parallel job writing to any index
public struct BadJob : IJobParallelFor {
    public NativeArray<int> Data;
    public void Execute(int i) {
        Data[i * 2] = 1;  // ERROR: Can only write to index i
    }
}

// RIGHT - Disable restriction (use carefully!)
public struct GoodJob : IJobParallelFor {
    [NativeDisableParallelForRestriction]
    public NativeArray<int> Data;

    public void Execute(int i) {
        // You're responsible for avoiding race conditions
        Data[i * 2] = 1;  // Allowed
    }
}
```

### 4. Struct Copying

Jobs are structs, so they're copied:

```csharp
var job = new MyJob { Value = 5 };
job.Schedule().Complete();
// job.Value is still 5! The scheduled copy had its own Value.

// To get output, use NativeArray/NativeList
var job = new MyJob { Output = new NativeArray<int>(1, Allocator.TempJob) };
job.Schedule().Complete();
int result = job.Output[0];  // Get result from native container
job.Output.Dispose();
```

### 5. Burst Restrictions

```csharp
// WRONG - Managed types in Burst job
[BurstCompile]
public struct BadJob : IJob {
    public string Name;           // ERROR: string is managed
    public List<int> Items;       // ERROR: List is managed
    public Action Callback;       // ERROR: delegate is managed
}

// RIGHT - Only unmanaged types
[BurstCompile]
public struct GoodJob : IJob {
    public int Id;
    public NativeArray<int> Items;
    public float3 Position;
}
```

---

## Performance Considerations

### When to Use Jobs

✅ **Good candidates**:
- Processing thousands of items (voxels, vertices)
- CPU-bound work that can be parallelized
- Operations that run every frame or frequently

❌ **Poor candidates**:
- Simple operations on small data
- Work that requires frequent managed interop
- Highly sequential algorithms

### Batch Sizes

For `IJobParallelFor`, the batch size affects scheduling:

```csharp
// Small batches = more parallelism, more overhead
job.Schedule(1000, 1);   // 1000 batches of 1

// Large batches = less overhead, less parallelism
job.Schedule(1000, 1000); // 1 batch of 1000

// Sweet spot depends on work per iteration
job.Schedule(256, 16);   // 16 batches of 16 (good for column work)
```

### Memory Layout

Burst optimizes linear memory access. Structure data for sequential access:

```csharp
// Cache-friendly: iterate Z, then Y, then X (matches VoxelIndex formula)
for (int z = 0; z < 16; z++) {
    for (int y = 0; y < 256; y++) {
        for (int x = 0; x < 16; x++) {
            int index = x + 16 * (y + 256 * z);
            // Sequential memory access
        }
    }
}
```

### Profiling

Use Unity's Profiler to verify job performance:

1. **Jobs Timeline**: See when jobs run and on which threads
2. **Burst Inspector**: View generated assembly (menu: Jobs > Burst > Open Inspector)
3. **Profile Markers**: Add custom markers in jobs

---

## Quick Reference

### File Locations

| Component | File |
|-----------|------|
| ChunkData struct | `Data/ChunkData.cs` |
| ChunkDataStore | `Data/ChunkDataStore.cs` |
| Terrain job | `Jobs/TerrainGenerationJob.cs` |
| Terrain scheduler | `Jobs/TerrainJobScheduler.cs` |
| Lighting jobs | `Jobs/LightingJobs.cs` |
| Lighting scheduler | `Jobs/LightingJobScheduler.cs` |
| Meshing job | `Jobs/MeshingJobs.cs` |
| Meshing scheduler | `Jobs/MeshingJobScheduler.cs` |

### Toggle Flags (VoxelWorld)

```csharp
_useUnityJobsTerrain   // Toggle terrain generation
_useUnityJobsLighting  // Toggle first-pass lighting
_useUnityJobsMeshing   // Toggle mesh generation
```

### Sync Points Summary

| Event | Direction | Method |
|-------|-----------|--------|
| After terrain gen | Native → Managed | `SyncFromNativeData()` |
| After Unity Jobs lighting | Native → Managed | `LightingScheduler.SyncToManagedChunk()` |
| After legacy lighting | Managed → Native | `SyncLightToNativeData()` |
| Before meshing | Managed → Native | `SyncLightToNativeData()` |

---

## Further Reading

- [Unity Jobs Documentation](https://docs.unity3d.com/Manual/JobSystem.html)
- [Burst User Guide](https://docs.unity3d.com/Packages/com.unity.burst@latest)
- [Native Containers](https://docs.unity3d.com/Manual/JobSystemNativeContainer.html)
- [Unity.Mathematics](https://docs.unity3d.com/Packages/com.unity.mathematics@latest)

---

*Document generated: November 2025*
*VektorVoxels Unity Jobs Rearchitecture*
