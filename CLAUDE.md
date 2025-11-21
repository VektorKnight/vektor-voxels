# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Vektor Voxels is a custom voxel engine built in Unity 6000.2.2f1+ (compatible with Unity 2021+). It implements concurrent job-based threading for terrain generation, lighting, and meshing operations. The project is no longer actively maintained but serves as a reference for cubic voxel meshing with per-side textures.

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

5. **Lighting** (`Lighting/LightMapper.cs`) - Dual lighting: Sunlight (propagates down) + Block light (6-direction flood-fill). Uses 4-bit per RGB channel. Smooth lighting with AO samples 8 corners per face

6. **Meshing** (`Meshing/`) - `VisualMesher` generates display mesh, `CollisionMesher` generates physics mesh. Custom vertex layout: Position, Normal, UV, SunLight (TexCoord1), BlockLight (TexCoord2)

7. **Threading** (`Threading/GlobalThreadPool.cs`) - Custom thread pool using 3/4 CPU cores (min 2). `VektorJob<T>` provides async/await pattern with callback dispatching to main thread

8. **Player Interaction** (`Interaction/VektorPlayer.cs`) - Uses New Input System. Voxel raycasting via DDA algorithm (`VoxelPhysics/VoxelTrace.cs`) for place/break operations

### Namespace Structure

```
VektorVoxels
├── Chunks          // Chunk lifecycle and data
├── Generation      // Terrain generators
├── Lighting        // Light propagation
├── Meshing         // Mesh generation
├── Threading/Jobs  // Job system and thread pool
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

- No chunk persistence/serialization
- Some lighting edge cases with neighbor propagation
- PhysX collider generation can cause stuttering

## Additional Documentation

- **`docs/initial_report.md`** - Comprehensive code audit with issues categorized by severity
- **`.claude/memory/architecture.md`** - Deep architectural knowledge (threading, state machines, algorithms)
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
