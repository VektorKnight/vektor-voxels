# Unity Jobs + Burst Rearchitecture Plan

**Created:** 2025-11-25
**Status:** Planning
**Scope:** Complete replacement of threading, lighting, and meshing systems

---

## Goals

Moving from the custom thread pool to Unity's Job System with Burst:

1. **Performance**: 5-10x faster light propagation and meshing via SIMD
2. **Stability**: No more race conditions - dependency-based scheduling instead of locks
3. **Maintainability**: Simpler sync model without manual locking
4. **GC Elimination**: Zero per-frame allocations with NativeContainers

---

## Current Architecture Analysis

### Threading System
| Component | Implementation | Issues |
|-----------|---------------|--------|
| Thread Pool | Custom `GlobalThreadPool` + `WorkerThread` | Manual power management, no work stealing |
| Jobs | `VektorJob<T>` with async/await pattern | Unused await support, callback-based in practice |
| Synchronization | `ReaderWriterLockSlim` per chunk | Lock contention, 10s timeout causes stalls |
| Main Thread | Dual queues (Default + Throttled) | Throttling helps but doesn't prevent all hitches |

### Data Structures (Burst Compatibility)
| Structure | Size | Blittable | Notes |
|-----------|------|-----------|-------|
| `VoxelData` | 8 bytes | ✅ Yes | ushort + flags + orientation + VoxelColor |
| `VoxelColor` | 2 bytes | ✅ Yes | RGB565 packed |
| `HeightData` | 2 bytes | ⚠️ Partial | bool needs → byte for Burst |
| `Vertex` | 48 bytes | ✅ Yes | Position, Normal, UV, Lights, TileRepeat |
| `LightNode` | 16 bytes | ✅ Yes | Vector3Int + VoxelColor |

### Current Pipeline
```
Chunk.Initialize()
    → GenerationJob (write lock)
        → LightJob Pass 1 (write lock)
            → WaitForNeighbors
                → LightJob Pass 2 (write lock + neighbor read locks)
                    → WaitForNeighbors
                        → LightJob Pass 3 (write lock + neighbor read locks)
                            → MeshJob (read lock + neighbor read locks)
                                → Ready
```

### Identified Problems
1. **Light Removal Bug**: No cascade-clear when light sources removed
2. **Lock Contention**: Jobs wait up to 10s for locks, causing hitches
3. **GC Pressure**: Job objects allocated per operation, no pooling
4. **N² Event Subscriptions**: Each chunk subscribes to 8 neighbors
5. **Mystery Hitch**: Undiagnosed multi-second stutter (likely GC or lock-related)

---

## Target Architecture

### Design Principles
1. **Data-Oriented**: Separate data from behavior, batch operations
2. **Dependency-Based**: No locks, jobs declare dependencies
3. **Burst-First**: Design for SIMD from the start
4. **Chunk as Data**: Chunks become data containers, not MonoBehaviours

### New Pipeline Overview
```
ChunkDataStore (NativeArrays)
    ↓
TerrainGenerationJob (IJobParallelFor)
    ↓
LightingJob (IJob with wavefront propagation)
    ↓ [dependency chain handles synchronization]
MeshingJob (IJobParallelFor per chunk)
    ↓
Main Thread: Apply mesh data
```

---

## Phase 1: Data Layer Conversion

**Goal**: Convert chunk data to NativeContainers without changing job system yet

### 1.1 ChunkData Structure
```csharp
public struct ChunkData : IDisposable {
    public int2 ChunkId;
    public NativeArray<VoxelData> Voxels;      // 65,536 elements
    public NativeArray<VoxelColor> SunLight;   // 65,536 elements
    public NativeArray<VoxelColor> BlockLight; // 65,536 elements
    public NativeArray<byte> HeightMap;        // 256 elements (HeightData.Value only)

    public ChunkState State;
    public LightPass CurrentPass;

    public void Dispose() {
        if (Voxels.IsCreated) Voxels.Dispose();
        if (SunLight.IsCreated) SunLight.Dispose();
        if (BlockLight.IsCreated) BlockLight.Dispose();
        if (HeightMap.IsCreated) HeightMap.Dispose();
    }
}
```

### 1.2 World Data Store
```csharp
public class ChunkDataStore : IDisposable {
    // Flat storage for all chunks (64x64 max = 4096 chunks)
    public NativeArray<ChunkData> Chunks;

    // Lookup: ChunkId → index
    public NativeHashMap<int2, int> ChunkLookup;

    // State tracking for job scheduling
    public NativeList<int> ChunksNeedingGeneration;
    public NativeList<int> ChunksNeedingLighting;
    public NativeList<int> ChunksNeedingMeshing;
}
```

### 1.3 HeightData Fix
```csharp
// Before (not Burst-safe)
public struct HeightData {
    public byte Value;
    public bool Dirty;  // bool is not blittable
}

// After
public struct HeightData {
    public byte Value;
    public byte Dirty;  // 0 = clean, 1 = dirty
}
```

### 1.4 Migration Strategy
1. Create `ChunkDataStore` alongside existing system
2. Sync data between old Chunk and new ChunkData
3. Gradually move operations to use NativeArrays
4. Remove old arrays once all systems converted

---

## Phase 2: Terrain Generation Jobs

**Goal**: Replace `GenerationJob` with Burst-compiled terrain generation

### 2.1 Noise Generation Job
```csharp
[BurstCompile]
public struct TerrainNoiseJob : IJobParallelFor {
    [ReadOnly] public int2 ChunkWorldOffset;
    [ReadOnly] public float NoiseScale;
    [ReadOnly] public NativeArray<VoxelLayer> Layers;

    [WriteOnly] public NativeArray<VoxelData> Voxels;
    [WriteOnly] public NativeArray<byte> HeightMap;

    public void Execute(int index) {
        // index = x + z * 16 (one column per thread)
        int x = index % 16;
        int z = index / 16;

        // Sample noise (use Unity.Mathematics.noise)
        float2 worldPos = new float2(
            ChunkWorldOffset.x + x,
            ChunkWorldOffset.y + z
        ) * NoiseScale;

        float height = noise.cnoise(worldPos) * 0.5f + 0.5f;
        int heightInt = (int)(height * MaxHeight);

        HeightMap[index] = (byte)heightInt;

        // Fill column with layers
        FillColumn(x, z, heightInt);
    }
}
```

### 2.2 Performance Expectations
| Metric | Current | With Burst |
|--------|---------|------------|
| Noise samples/chunk | 256 | 256 |
| Time per chunk | ~2-5ms | ~0.1-0.3ms |
| Parallelism | 1 chunk/job | 256 columns parallel |

### 2.3 Advanced Noise (Future)
```csharp
// Unity.Mathematics provides:
// - noise.cnoise (Perlin)
// - noise.snoise (Simplex)
// - noise.cellular (Worley)

// Fractal Brownian Motion for terrain:
float FBM(float2 pos, int octaves) {
    float value = 0;
    float amplitude = 1;
    float frequency = 1;

    for (int i = 0; i < octaves; i++) {
        value += noise.snoise(pos * frequency) * amplitude;
        amplitude *= 0.5f;
        frequency *= 2f;
    }

    return value;
}
```

---

## Phase 3: Lighting System Rearchitecture

**Goal**: Replace BFS light propagation with Burst-optimized wavefront algorithm

### 3.1 The Light Removal Problem

**Root Cause**: Current system only propagates light forward, never removes it.

**Solution**: Implement proper light subtraction with cascade clearing.

```csharp
public enum LightUpdateType {
    Add,      // Light source placed
    Remove,   // Light source removed
    Rebuild   // Full chunk rebuild
}
```

### 3.2 Light Propagation Strategy

**Current**: Stack-based BFS per chunk, 3 passes for cross-chunk propagation

**New**: Wavefront propagation with proper removal support

```csharp
[BurstCompile]
public struct LightPropagationJob : IJob {
    // Input
    [ReadOnly] public NativeArray<VoxelData> Voxels;
    [ReadOnly] public NativeArray<VoxelData> NeighborVoxelsN;
    [ReadOnly] public NativeArray<VoxelData> NeighborVoxelsE;
    [ReadOnly] public NativeArray<VoxelData> NeighborVoxelsS;
    [ReadOnly] public NativeArray<VoxelData> NeighborVoxelsW;

    // Input/Output
    public NativeArray<VoxelColor> LightMap;

    // Neighbor light (read-only during propagation)
    [ReadOnly] public NativeArray<VoxelColor> NeighborLightN;
    [ReadOnly] public NativeArray<VoxelColor> NeighborLightE;
    [ReadOnly] public NativeArray<VoxelColor> NeighborLightS;
    [ReadOnly] public NativeArray<VoxelColor> NeighborLightW;

    // Work queue (fixed size, no allocation)
    public NativeQueue<LightNode> PropagationQueue;

    public void Execute() {
        // Phase 1: Initialize from light sources + boundaries
        InitializeLightSources();
        InitializeBoundaryLight();

        // Phase 2: Propagate until queue empty
        while (PropagationQueue.Count > 0) {
            var node = PropagationQueue.Dequeue();
            PropagateFromNode(node);
        }
    }
}
```

### 3.3 Light Removal Algorithm

When a light source is removed:

```csharp
[BurstCompile]
public struct LightRemovalJob : IJob {
    public NativeArray<VoxelColor> LightMap;
    public NativeQueue<int3> RemovalQueue;    // Positions to clear
    public NativeQueue<LightNode> RefillQueue; // Positions to re-propagate

    public void Execute() {
        // Phase 1: Clear light from removed source (BFS outward)
        while (RemovalQueue.Count > 0) {
            var pos = RemovalQueue.Dequeue();
            var currentLight = LightMap[Index(pos)];
            LightMap[Index(pos)] = VoxelColor.Black();

            // Check 6 neighbors
            for (int i = 0; i < 6; i++) {
                var neighborPos = pos + Directions[i];
                var neighborLight = LightMap[Index(neighborPos)];

                if (neighborLight < currentLight) {
                    // This light came from us, clear it
                    RemovalQueue.Enqueue(neighborPos);
                } else if (neighborLight >= currentLight) {
                    // This light came from elsewhere, re-propagate from here
                    RefillQueue.Enqueue(new LightNode(neighborPos, neighborLight));
                }
            }
        }

        // Phase 2: Re-propagate from boundaries
        while (RefillQueue.Count > 0) {
            var node = RefillQueue.Dequeue();
            PropagateFromNode(node);
        }
    }
}
```

### 3.4 Cross-Chunk Synchronization

**Current**: 3 passes with event-based waiting

**New**: Dependency chain with JobHandle

```csharp
public class LightingScheduler {
    public JobHandle ScheduleChunkLighting(int chunkIndex, JobHandle dependency) {
        // Pass 1: Internal propagation
        var pass1 = new LightPropagationJob {
            Voxels = chunks[chunkIndex].Voxels,
            LightMap = chunks[chunkIndex].BlockLight,
            // No neighbor data for pass 1
        }.Schedule(dependency);

        // Wait for all neighbors to complete pass 1
        var neighborDeps = GatherNeighborDependencies(chunkIndex, pass1);

        // Pass 2: With neighbor data
        var pass2 = new LightPropagationJob {
            Voxels = chunks[chunkIndex].Voxels,
            LightMap = chunks[chunkIndex].BlockLight,
            NeighborLightN = GetNeighborLight(chunkIndex, Direction.North),
            // ... etc
        }.Schedule(JobHandle.CombineDependencies(neighborDeps));

        return pass2;
    }
}
```

### 3.5 Sunlight Optimization

Sunlight is special: it propagates top-down at full intensity until hitting a surface.

```csharp
[BurstCompile]
public struct SunlightColumnJob : IJobParallelFor {
    [ReadOnly] public NativeArray<VoxelData> Voxels;
    [ReadOnly] public NativeArray<byte> HeightMap;

    public NativeArray<VoxelColor> SunLight;
    public NativeQueue<LightNode>.ParallelWriter CavernNodes;

    public void Execute(int columnIndex) {
        int x = columnIndex % 16;
        int z = columnIndex / 16;
        int height = HeightMap[columnIndex];

        // Full sunlight above heightmap
        for (int y = height + 1; y < 256; y++) {
            SunLight[VoxelIndex(x, y, z)] = VoxelColor.White();
        }

        // Black below heightmap
        for (int y = 0; y <= height; y++) {
            SunLight[VoxelIndex(x, y, z)] = VoxelColor.Black();
        }

        // Detect cavern entrances for horizontal propagation
        DetectCavernOpenings(x, z, height);
    }
}
```

---

## Phase 4: Meshing System Conversion

**Goal**: Convert meshing to IJobParallelFor with Burst compilation

### 4.1 Mesh Data Structures (Already Burst-Safe)
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct Vertex {  // 48 bytes, blittable
    public float3 Position;
    public float3 Normal;
    public float2 TexCoord;
    public Color32 SunLight;
    public Color32 BlockLight;
    public float2 TileRepeat;
}
```

### 4.2 Greedy vs Smooth Lighting Trade-off

**Constraint**: Greedy meshing is incompatible with smooth lighting.
- Smooth lighting requires per-vertex AO sampling from 8 corner neighbors
- Greedy meshing merges faces, losing intermediate vertices
- Current system has dual code paths: `GenerateMeshData()` vs `GenerateMeshDataGreedy()`

**Decision**: Keep dual-path approach for now.

**Future Option - GPU Lightmap Sampling**:
```csharp
// Instead of baking light into vertices, upload as 3D texture
// Shader samples light at fragment position
// Allows greedy mesh + smooth light, but adds complexity:
// - 256KB texture per chunk (16x256x16 * 2 channels * 2 bytes)
// - Filtering artifacts at chunk boundaries
// - Texture atlas management
// NOT implementing in this rearchitecture - noted for future exploration
```

### 4.3 Greedy Meshing Job (Flat Lighting Path)
```csharp
[BurstCompile]
public struct GreedyMeshJob : IJob {
    [ReadOnly] public NativeArray<VoxelData> Voxels;
    [ReadOnly] public NativeArray<VoxelColor> SunLight;
    [ReadOnly] public NativeArray<VoxelColor> BlockLight;

    // Neighbor data for face culling
    [ReadOnly] public NativeArray<VoxelData> NeighborN;
    // ... etc

    // Output (pre-allocated max size)
    public NativeList<Vertex> Vertices;
    public NativeList<uint> OpaqueIndices;
    public NativeList<uint> AlphaIndices;

    // Work buffers (allocated once, reused)
    public NativeArray<FaceMask> FaceMaskBuffer;  // 16*256 = 4096

    public void Execute() {
        Vertices.Clear();
        OpaqueIndices.Clear();
        AlphaIndices.Clear();

        // Process 6 face directions
        for (int face = 0; face < 6; face++) {
            ProcessFace(face);
        }
    }
}
```

### 4.4 Smooth Lighting Mesh Job (Standard Path)
```csharp
[BurstCompile]
public struct SmoothMeshJob : IJob {
    [ReadOnly] public NativeArray<VoxelData> Voxels;
    [ReadOnly] public NativeArray<VoxelColor> SunLight;
    [ReadOnly] public NativeArray<VoxelColor> BlockLight;

    // All 8 neighbors needed for corner AO sampling
    [ReadOnly] public NativeArray<VoxelData> NeighborN;
    [ReadOnly] public NativeArray<VoxelData> NeighborE;
    // ... (8 directions for voxels, 8 directions for sun, 8 for block)

    public NativeList<Vertex> Vertices;
    public NativeList<uint> OpaqueIndices;
    public NativeList<uint> AlphaIndices;

    public void Execute() {
        // Per-face iteration (NO greedy merging - preserves vertex density)
        for (int z = 0; z < 16; z++) {
            for (int y = 0; y < 256; y++) {
                for (int x = 0; x < 16; x++) {
                    var voxel = Voxels[VoxelIndex(x, y, z)];
                    if (voxel.IsEmpty()) continue;

                    for (int face = 0; face < 6; face++) {
                        if (!ShouldRenderFace(x, y, z, face)) continue;

                        // Sample 8 corners for AO - requires per-face vertices
                        var cornerLights = SampleCornerLights(x, y, z, face);
                        GenerateFaceVertices(x, y, z, face, voxel, cornerLights);
                    }
                }
            }
        }
    }
}
```

### 4.5 Parallel Chunk Meshing
```csharp
// Process multiple chunks in parallel
[BurstCompile]
public struct BatchMeshJob : IJobParallelFor {
    [ReadOnly] public NativeArray<ChunkData> Chunks;
    [ReadOnly] public NativeArray<int> ChunkIndices;  // Which chunks to mesh

    // Per-chunk output (indexed by job index)
    [NativeDisableParallelForRestriction]
    public NativeArray<MeshOutput> MeshOutputs;

    public void Execute(int jobIndex) {
        int chunkIndex = ChunkIndices[jobIndex];
        var chunk = Chunks[chunkIndex];

        // Generate mesh for this chunk
        GenerateMesh(chunk, ref MeshOutputs[jobIndex]);
    }
}
```

### 4.4 Performance Expectations
| Metric | Current | With Burst |
|--------|---------|------------|
| Vertices/chunk | ~10-50K | Same |
| Time per chunk | ~5-15ms | ~0.5-2ms |
| Parallelism | 1 chunk/thread | N chunks parallel |
| Memory | New arrays each time | Pre-allocated, reused |

---

## Phase 5: Main Thread Integration

**Goal**: Clean separation between job scheduling and Unity API calls

### 5.1 Chunk Manager (MonoBehaviour)
```csharp
public class ChunkManager : MonoBehaviour {
    private ChunkDataStore _dataStore;
    private JobHandle _currentFrame;

    // Unity objects (must be on main thread)
    private Dictionary<int2, ChunkRenderer> _renderers;

    void Update() {
        // Complete previous frame's jobs
        _currentFrame.Complete();

        // Apply mesh data to Unity meshes
        ApplyPendingMeshes();

        // Schedule next frame's jobs
        _currentFrame = ScheduleFrameJobs();
    }

    private JobHandle ScheduleFrameJobs() {
        var handle = default(JobHandle);

        // Terrain generation
        handle = ScheduleTerrainJobs(handle);

        // Lighting
        handle = ScheduleLightingJobs(handle);

        // Meshing
        handle = ScheduleMeshingJobs(handle);

        return handle;
    }
}
```

### 5.2 Chunk Renderer (Lightweight MonoBehaviour)
```csharp
public class ChunkRenderer : MonoBehaviour {
    public MeshFilter MeshFilter;
    public MeshRenderer MeshRenderer;
    public MeshCollider MeshCollider;

    private Mesh _mesh;

    public void ApplyMeshData(NativeArray<Vertex> vertices,
                               NativeArray<uint> indices) {
        // This runs on main thread after jobs complete
        _mesh.SetVertexBufferData(vertices, ...);
        _mesh.SetIndexBufferData(indices, ...);
        _mesh.RecalculateBounds();
    }
}
```

### 5.3 Frame Budget
```csharp
public class FrameBudget {
    public int MaxChunksToGenerate = 4;
    public int MaxChunksToLight = 8;
    public int MaxChunksToMesh = 4;
    public int MaxMeshAppliesPerFrame = 4;  // GPU upload limit
}
```

---

## Phase 6: Persistence System

**Goal**: Polish and integrate existing save/load with new architecture

### 6.1 Existing System (Keep and Adapt)

The project already has a persistence system:
- **Format**: RLE binary per-column + world JSON
- **World JSON**: Name→ID mapping table for voxel definition versioning
- **Scope**: 64×64 chunk finite world (appropriate for project goals)

**NOT in scope**: Infinite worlds. That would require:
- Region file system (32×32 chunks per file)
- LRU chunk cache with memory budgets
- Async streaming with prioritization
- 64-bit coordinates
- Significant complexity not justified for "creative mode reference" goal

### 6.2 Adaptation for New Architecture

```csharp
// Existing serialization works on managed arrays
// Need adapter to convert NativeArray ↔ managed for save/load

public static class ChunkSerializer {
    public static byte[] Serialize(NativeArray<VoxelData> voxels) {
        // Copy to managed array for existing RLE encoder
        var managed = voxels.ToArray();
        return ExistingRLEEncoder.Encode(managed);
    }

    public static void Deserialize(byte[] data, NativeArray<VoxelData> target) {
        var managed = ExistingRLEDecoder.Decode(data);
        target.CopyFrom(managed);
    }
}
```

### 6.3 Integration Points

1. **On Chunk Modification**: Mark chunk dirty for save
2. **On World Exit**: Save all dirty chunks
3. **On World Load**: Load existing chunks, generate missing ones
4. **Autosave**: Periodic background save of dirty chunks

### 6.4 Polish Tasks
- [ ] Ensure existing save/load works with new ChunkData
- [ ] Add dirty tracking to ChunkData struct
- [ ] Test save/load round-trip preserves all voxel state
- [ ] Handle version migration if VoxelTable changes

---

## Migration Strategy

### Incremental Approach
Each phase can be completed and tested independently:

```
Phase 1 (Data Layer)
    - ChunkDataStore alongside existing Chunk
    - Dual-write to both systems
    - Verify data consistency

Phase 2 (Terrain Generation)
    - New Burst terrain jobs
    - A/B test against old generator
    - Remove old GenerationJob

Phase 3 (Lighting)
    - New light propagation with removal support
    - Extensive testing of edge cases
    - Remove old LightMapper/LightJob

Phase 4 (Meshing)
    - New Burst meshing jobs
    - Verify visual parity
    - Remove old VisualMesher/MeshJob

Phase 5 (Integration)
    - New ChunkManager
    - Remove GlobalThreadPool
    - Performance profiling

Phase 6 (Persistence)
    - Save/load implementation
    - Testing and polish
```

### Rollback Points
Each phase creates a stable state:
- Phase 1 complete: Old jobs work with new data
- Phase 2 complete: New terrain, old lighting/meshing
- Phase 3 complete: New terrain + lighting, old meshing
- Phase 4 complete: Fully new pipeline
- Phase 5 complete: Clean architecture
- Phase 6 complete: Full feature set

---

## Risk Assessment

### High Risk
| Risk | Mitigation |
|------|------------|
| Light removal algorithm incorrect | Extensive unit tests, visual debugging |
| Burst compilation errors | Incremental conversion, regular testing |
| NativeContainer memory leaks | Strict Dispose patterns, leak detection |

### Medium Risk
| Risk | Mitigation |
|------|------------|
| Performance regression | A/B benchmarking at each phase |
| Cross-chunk synchronization bugs | Formal verification of dependency chains |
| Save format incompatibility | Version field in save format |

### Low Risk
| Risk | Mitigation |
|------|------------|
| Unity API changes | Target LTS version (6000.x) |
| Burst version issues | Pin Burst package version |

---

## Success Metrics

### Performance Targets
| Metric | Current | Target |
|--------|---------|--------|
| Chunk generation | 2-5ms | <0.5ms |
| Light propagation | 5-10ms | <1ms |
| Mesh generation | 5-15ms | <2ms |
| Frame time (idle) | Varies + hitches | Consistent <5ms |
| Memory (100 chunks) | ~50MB managed | ~30MB native |

### Functional Targets
- [ ] Light properly removes when source removed
- [ ] No visible seams at chunk boundaries
- [ ] Smooth chunk loading while moving
- [ ] Save/load preserves all placed blocks
- [ ] No GC allocations during normal gameplay

---

## Appendix A: Key Files to Modify/Replace

| Current File | Action | New File |
|--------------|--------|----------|
| `Threading/GlobalThreadPool.cs` | Remove | - |
| `Threading/ThreadPool.cs` | Remove | - |
| `Threading/WorkerThread.cs` | Remove | - |
| `Threading/Jobs/VektorJob.cs` | Remove | - |
| `Chunks/Chunk.cs` | Gut/Replace | `Data/ChunkData.cs` |
| `Generation/GenerationJob.cs` | Replace | `Jobs/TerrainGenerationJob.cs` |
| `Lighting/LightMapper.cs` | Replace | `Jobs/LightPropagationJob.cs` |
| `Lighting/LightJob.cs` | Replace | (merged into scheduler) |
| `Meshing/VisualMesher.cs` | Replace | `Jobs/MeshGenerationJob.cs` |
| `Meshing/MeshJob.cs` | Replace | (merged into scheduler) |
| `World/VoxelWorld.cs` | Refactor | `ChunkManager.cs` |

---

## Appendix B: Unity Packages Required

```json
{
  "dependencies": {
    "com.unity.burst": "1.8.x",
    "com.unity.collections": "2.x.x",
    "com.unity.mathematics": "1.3.x",
    "com.unity.jobs": "0.70.x"
  }
}
```

---

## Appendix C: Reference Resources

- [Unity Job System Manual](https://docs.unity3d.com/Manual/JobSystem.html)
- [Burst User Guide](https://docs.unity3d.com/Packages/com.unity.burst@latest)
- [NativeContainers](https://docs.unity3d.com/Packages/com.unity.collections@latest)
- [0fps Blog: Voxel Lighting](https://0fps.net/2018/02/21/voxel-lighting/)
- [Seed of Andromeda: Chunk Management](https://www.seedofandromeda.com/blogs/1-creating-a-region-file-system-for-a-voxel-game)

---

## Revision History

| Date | Changes |
|------|---------|
| 2025-11-25 | Initial plan |
