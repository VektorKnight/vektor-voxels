# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Collaboration Style

This is a research project where we work as peers. When collaborating:

- **Be direct and honest** - Don't suppress thoughts or avoid disagreement to be agreeable. Push back when you see issues, offer alternatives, voice concerns.
- **No artificial constraints** - You're not here to validate ideas; you're here to help solve interesting problems. If an approach seems wrong, say so.
- **Think out loud** - Share your reasoning, trade-offs you're weighing, and uncertainties. This is a sandbox for exploring complex systems together.
- **Challenge assumptions** - The user may be wrong. Prior decisions may have been suboptimal. Question things.

We get better results with an equal relationship when tackling complex problems.

## Project Overview

Vektor Voxels is a custom voxel engine built in Unity 6000.2.2f1+ (compatible with Unity 2021+). It uses Unity's Job System with Burst compilation for terrain generation, lighting, and meshing operations.

**Project nature:** This is an ongoing research sandbox - not a complete game, but a space to explore voxel engine problems (lighting, meshing, threading, persistence) in a playable environment. It evolves over time as interesting problems are investigated.

**Current focus:** Stabilizing the chunk pipeline and fixing boundary propagation edge cases in the lighting system.

## Build Commands

```bash
# Open in Unity Editor (primary development method)
# Or build via command line:
unity -quit -batchmode -projectPath . -buildWindowsPlayer Build/game.exe
```

No custom build scripts - uses Unity's built-in build system. Test framework available (com.unity.test-framework) but no tests currently implemented.

## Architecture

### Chunk Pipeline

Chunks progress through states: **Uninitialized → TerrainGeneration → Lighting → Meshing → Ready**

Each chunk is 16x256x16 voxels. The world supports up to 64x64 chunks with all chunks resident in memory (finite world system).

### Core Systems

1. **World System** (`World/VoxelWorld.cs`) - Singleton managing chunk loading/unloading based on view distance

2. **Chunk System** (`Chunks/Chunk.cs`) - Thread-safe chunk data with `ReaderWriterLockSlim`, handles state transitions and neighbor tracking

3. **Voxel Data** (`Voxels/`) - `VoxelData` struct contains ID, Flags, Orientation, and Color16 (4-bit RGB). Definitions hardcoded in `VoxelTable.cs`

4. **Terrain Generation** (`Generation/`) - Implements `ITerrainGenerator` interface. Uses `PerlinGenerator` by default with layered voxels (bedrock/stone/dirt/grass)

5. **Lighting** (`Jobs/LightingJobs.cs`, `Jobs/LightingJobScheduler.cs`) - Dual lighting: Sunlight (propagates down) + Block light (6-direction flood-fill). Uses RGB565 (16-bit) per light type. Coordinated multi-pass system ensures correct cross-chunk propagation. Smooth lighting with AO samples corners per face.

6. **Meshing** (`Jobs/MeshingJobs.cs`, `Meshing/VisualMesher.cs`) - Burst-compiled mesh generation. Custom vertex layout: Position, Normal, UV, SunLight (TexCoord1), BlockLight (TexCoord2). `CollisionMesher` generates physics mesh.

7. **Job System** (`Jobs/`, `Data/ChunkDataStore.cs`) - Uses Unity Job System with Burst compilation. `ChunkDataStore` manages NativeArray storage. Coordinated scheduling via `TerrainJobScheduler`, `LightingJobScheduler`, `MeshingJobScheduler`.

8. **Persistence** (`Persistence/WorldPersistence.cs`) - RLE-compressed chunk serialization. Async save with throttled queue to prevent GC spikes. Player position saved/restored.

9. **Player Interaction** (`Interaction/VektorPlayer.cs`) - Uses New Input System. Voxel raycasting via DDA algorithm (`VoxelPhysics/VoxelTrace.cs`) for place/break operations

### Namespace Structure

```
VektorVoxels
├── Chunks          // Chunk lifecycle and state machine
├── Data            // NativeArray storage (ChunkDataStore)
├── Generation      // Terrain generators
├── Jobs            // Unity Jobs (terrain, lighting, meshing schedulers)
├── Lighting        // Light data structures and legacy mapper
├── Meshing         // Mesh generation
├── Persistence     // World save/load
├── Voxels          // Voxel data structures
├── VoxelPhysics    // DDA raycasting
├── Interaction     // Player controller
├── World           // World management
└── UI/Debugging    // HUD and profiling
```

### Shaders

- `Shaders/VoxelsOpaque.shader` - Standard voxel rendering with baked lighting
- `Shaders/VoxelsAlpha.shader` - Transparent variant for translucent blocks
- Texture atlas: 256x256 with 16x16 tiles

## Key Implementation Details

- Lighting is baked into vertices, not recalculated at runtime
- Two material passes per chunk: Opaque + Alpha
- Border light propagation requires neighbor chunk data
- Chunk loading throttled via `ChunksPerTick` (default 4)
- Custom meshes supported via `ICustomMesh` interface

## Known Issues

### Debug Keybinds

- **F3** - Toggle two-pass lighting mode (experimental, ~33% faster)
- **F5** - Force refresh all chunks (Minecraft-style lighting fix)

### Minor Issues

- PhysX collider generation can cause stuttering on dense chunks
- Partial chunk loading at view boundaries may show incorrect edge lighting until neighbors load
- Two-pass lighting (F6) may have subtle artifacts when multiple light sources converge through intermediate chunks

## Additional Documentation

- **`docs/chunk_pipeline_audit.md`** - Technical deep-dive on chunk pipeline and boundary propagation issues
- **`docs/initial_report.md`** - Comprehensive code audit with issues categorized by severity
- **`.claude/memory/architecture.md`** - Deep architectural knowledge (state machines, algorithms, data structures)
- **`.claude/memory/sessions.md`** - Active work tracking for session continuity

The memory file contains critical non-obvious knowledge with confidence tags. Consult it before making significant changes to threading, lighting, or chunk systems.

## Memory System Maintenance

When making significant changes:

1. **Update `.claude/memory/architecture.md`** if you modify:
   - Threading/job system behavior
   - Chunk state machine transitions
   - Lighting propagation algorithm
   - Coordinate systems or indexing

2. **Add confidence tags** to new knowledge:
   - `[VERIFIED, HIGH]` - Directly observable in code
   - `[INFERRED, MEDIUM]` - Derived from patterns
   - `[ASSUMED, LOW]` - Needs verification

3. **Update session state** in `.claude/memory/sessions.md` when:
   - Starting multi-session work
   - Making progress on ongoing tasks
   - Completing or abandoning work streams

This keeps the knowledge base accurate for future Claude instances and developers.
