# Session Tracking

Track active work for continuity across sessions.

---

## Active Work

### Lighting Stabilization
**Status:** active
**Started:** 2025-11-27
**Updated:** 2025-11-27

**Goals:**
1. Fix ghost light bug (light removal doesn't cascade to neighbors)
2. Test if Pass 3 is redundant (may be able to reduce to 2 passes)
3. Ensure deterministic, bug-free lighting at chunk boundaries

**Planning Document:** `docs/chunk_pipeline_audit.md`

**Current State:**
Unity Jobs migration complete. Core pipeline functional. Ghost light bug fix implemented. Two-pass lighting toggle added.

**Key Decisions (this session):**
- **Stabilize before experimenting** - GPU lightmaps discussed but deferred. Fix current bugs first, then consider architectural changes.
- **F5 refresh added** - Minecraft-style workaround for lighting bugs. Press F5 to force re-light all chunks.
- **F3 toggle added** - Switch between 2-pass and 3-pass lighting for testing (moved from F6).
- **Project is a research sandbox** - Updated CLAUDE.md to reflect ongoing exploration rather than static reference.

**Fixes Implemented (this session):**
- [x] Ghost light bug (part 1) - Root cause was NOT in `LightRemovalJob` (which is dead code). The actual issue: light can propagate ~30 blocks (spanning 2 chunks), but only 1-hop neighbors were queued for relighting. Fix: `UpdateAffectedNeighbors` now queues 2-hop cardinal neighbors AND diagonal neighbors for light-affecting changes. Up to 13 chunks can be queued per light change (HashSet dedupes).
- [x] Ghost light bug (part 2) - Placing an opaque block to close a hole wasn't triggering extended neighbor queueing. The check only looked at NEW voxel data (`data.IsEmpty() || !data.IsOpaque()`), missing the case where OLD voxel was transparent. Fix: `GetAffectedNeighbors` now takes `oldData` parameter and checks both old and new voxel states.
- [x] Two-pass lighting toggle - Added `UseTwoPassLighting` flag to `LightingJobScheduler`. F3 toggles at runtime (moved from F6 due to GUI conflict). Pass 3 is only needed when light from multiple sources converges through an intermediate chunk.
- [x] World loading bugs - Two issues fixed:
  1. `InitializeWithData` never set `_state` after initialization - chunk stayed in `Uninitialized` state. Fixed by setting `_state = ChunkState.Lighting` before queuing for coordinated lighting.
  2. `ClearAllChunks` didn't complete pending terrain jobs before destroying chunks. Added `_terrainScheduler?.CompleteAll()` call and reordered operations to clear arrays before destroying GameObjects.
- [x] Smooth lighting dark seams - `SampleLight` in `MeshingJobs.cs` returned black when neighbor data unavailable. Fix: use clamped edge light from current chunk for visual continuity.
- [x] Sunlight entry-tinting for translucent blocks - Two places in `SunlightColumnJob` were seeding INSIDE glass with full white light, bypassing tint:
  1. **Surface glass** (line ~119): Seed was placed at heightmap position (inside glass). Fix: place seed ABOVE glass (in air), clear it first to enable propagation, let entry-tinting apply.
  2. **Cavern opening glass** (`CheckCavernOpening`): Seed was placed at neighbor position (might be glass). Fix: always seed in home air column, let propagation flow into neighbor with entry-tinting.
  Key insight: seeds should always be in air, propagation handles tinting on entry.

**Architectural Discussion (2025-11-27):**
Discussed GPU lightmaps (upload light as 3D texture, sample in shader instead of baking to vertices). Benefits: decouples lighting from meshing, enables dynamic lighting, smaller vertices. Deferred because:
1. Propagation bugs are orthogonal - need fixing regardless of render approach
2. Significant scope (shader rewrite, texture management, smooth lighting changes)
3. Better to have stable baseline before experimenting

Also discussed "update propagation wave" with explicit dependency graphs. Current system already does coordinated passes but implicitly. Key insight: **we're not missing a dependency graph, we're missing proper incremental updates.** Current system batches everything and recomputes. Smarter approach would track what changed and propagate minimally. Also deferred - fix bugs in current model first.

**Dead Code Identified:**
- `LightRemovalJob` and `RemoveLight()` in `LightingJobScheduler` are never called. The actual flow uses `ExecuteFullLighting` which clears and regenerates via Pass 1.
- `AddLight()` similarly unused. Both kept for potential future incremental update optimization.

**Next Steps:**
- [x] Fix ghost light bug - extended neighbor queueing to 2-hop + diagonals
- [x] Add Pass 3 toggle - F3 switches between 2-pass and 3-pass
- [x] Fix smooth lighting seams at chunk boundaries
- [x] Fix sunlight entry-tinting for translucent blocks
- [ ] Test two-pass mode for visual artifacts with multiple light sources
- [ ] Investigate intermittent null-reference on world load
- [ ] Fix UI click bleed-through after world load dialog
- [ ] Consider diagonal neighbor support for T-intersection seams (low priority)

---

### Unity Jobs + Burst Rearchitecture
**Status:** completed
**Started:** 2025-11-25
**Updated:** 2025-11-27

**Goals:**
1. Replace custom thread pool with Unity Job System
2. Add Burst compilation for performance-critical code
3. Fix light removal bug (cascade-clear algorithm)
4. Eliminate GC pressure with NativeContainers
5. Polish existing save/load system

**Planning Document:** `docs/unity_jobs_rearchitecture.md`

**Final State:**
All phases complete. Legacy threading system fully removed. Burst-compiled terrain, lighting, and meshing running via Unity Jobs with coordinated multi-pass scheduling.

**Key Decisions:**
- Keep dual meshing paths (greedy flat / standard smooth) - no GPU lightmaps
- Keep 64x64 finite world - infinite worlds out of scope
- Adapt existing RLE save system rather than rewrite
- Dependency-based job scheduling replaces lock-based synchronization

**Completed This Session (2025-11-26):**
- [x] Fix: Colored sunlight propagation across chunk boundaries
  - Root cause: Propagation only tinted light when ENTERING translucent blocks, not when EXITING
  - Fix: Added exit-tinting to both Unity Jobs and legacy propagation systems
  - Simplified `SunlightColumnJob` to use seeds instead of manual column filtering
  - Sunlight now works identically to block light after heightmap optimization
  - Modified `LightPropagationJob`, `LightMapper.PropagateLightNodes`, `SunlightColumnJob`
- [x] Phase 1: Data Layer Conversion
  - Added Unity packages: Burst 1.8.18, Collections 2.5.1, Mathematics 1.3.2
  - Fixed `HeightData.Dirty` (bool → byte for Burst compatibility)
  - Created `ChunkData` struct with NativeArrays
  - Created `ChunkDataStore` for world data management
  - Integrated dual-write bridge in `Chunk.cs`
- [x] Phase 2: Terrain Generation Jobs
  - Created `TerrainGenerationJob` with Burst compilation
  - Created `TerrainJobScheduler` for job lifecycle management
  - Integrated with `VoxelWorld` and `Chunk`
  - Added `_useUnityJobsTerrain` toggle for A/B testing
- [x] Phase 3: Lighting System Rearchitecture (First Pass)
  - Created `LightingJobs.cs`: SunlightColumnJob, BlockLightSourceJob, LightPropagationJob, LightRemovalJob
  - Created `LightingJobScheduler` with wavefront BFS propagation
  - Integrated with `Chunk.QueueLightPass()` for first pass
  - Added `_useUnityJobsLighting` toggle for A/B testing
  - Phases 2-3 (neighbor lighting) still use legacy system
- [x] Phase 4: Meshing System Conversion
  - Created `MeshingJobs.cs`: VisualMeshingJob with Burst compilation
  - Created `MeshingJobScheduler` with texture rect lookup tables
  - Integrated with `Chunk.QueueMeshPass()`
  - Added `_useUnityJobsMeshing` toggle for A/B testing
- [x] Phase 6: Persistence Polish (GC fix)
  - Root cause: `SaveWorld()` allocated 512KB VoxelData[] per chunk when saving many dirty chunks at once
  - Fix: Throttled save queue (one chunk per frame) + pooled buffer
  - `WorldPersistence`: Added `_saveBuffer`, `SaveInProgress`, `GetSaveBuffer()`, `SaveChunkAsyncFromBuffer()`
  - `VoxelWorld`: Added `_saveQueue`/`_saveQueueSet`, `ProcessSaveQueue()` called from Update
  - Now reads from `ChunkDataStore` NativeArrays (with fallback to legacy managed array)
  - `OnApplicationQuit` flushes queue synchronously
  - Modified files: `WorldPersistence.cs`, `VoxelWorld.cs`
- [x] Fix: Cross-chunk lighting race condition on voxel modification (initial attempt)
  - Initial fix was to reset neighbor._lightPass, but hybrid system was fundamentally flawed
- [x] MAJOR: Coordinated lighting system rearchitecture (VERIFIED WORKING)
  - Removed hybrid Unity Jobs + legacy threading race conditions
  - New architecture: VoxelWorld orchestrates all lighting in coordinated passes
  - All chunks complete Pass N before any starts Pass N+1
  - New files/changes:
    - `LightingJobs.cs`: Added `BorderSeedJob` for cross-chunk border propagation
    - `LightingJobScheduler.cs`: Added `ExecuteFullLighting()`, `ExecuteBorderPass()`
    - `VoxelWorld.cs`: Added `_lightingQueue`, `QueueChunkForLighting()`, `ProcessLightingQueue()`
    - `Chunk.cs`: `OnGenerationPassComplete()`, `Reload()`, `UpdateAffectedNeighbors()` now use coordinated path
    - Added `QueueMeshPassFromWorld()` for VoxelWorld to trigger meshing after lighting
  - Fix: Pass placeholder arrays for missing neighbors (Unity Jobs validates all arrays at schedule time)

**Previous Session (2025-11-25):**
- [x] Phase 1 of old refactor plan: Retry logic added to GenerationJob, LightJob, MeshJob
- [x] Fixed sunlight tunnel propagation bug (cavern nodes placed at wrong position)
- [x] Full system audit (threading, meshing, lighting, terrain gen)
- [x] Created comprehensive rearchitecture plan

**Known Bugs:**
- ~~Light removal doesn't cascade to neighbors (ghost light remains)~~ - Active focus for next session
- ~~Mystery multi-second hitch~~ - FIXED: Was GC from SaveChunkAsync allocating per-chunk
- ~~Cross-chunk lighting breaks after modification~~ - FIXED: Coordinated lighting system eliminates race conditions

**Workarounds Added:**
- F5 force refresh - clears all light data and re-lights all loaded chunks (Minecraft-style)

**Session 2025-11-27 Changes:**
- Created `docs/chunk_pipeline_audit.md` - comprehensive technical audit for peer review
- Updated `CLAUDE.md` - added collaboration style section, updated project description and architecture
- Added `VoxelWorld.ForceRefreshAllChunks()` - F5 keybind for force refresh

**Legacy Threading System Removal (2025-11-26):**
- [x] Replaced GlobalThreadPool in WorldPersistence with Task.Run + ConcurrentQueue callbacks
- [x] Added WorldPersistence.ProcessCallbacks() for main-thread callback dispatch
- [x] Removed legacy fallbacks in Chunk.cs (QueueGenerationPass, QueueLightPass, QueueMeshPass)
- [x] Removed Unity Jobs toggle fields and accessors (_useUnityJobsTerrain, etc.)
- [x] Deleted entire Threading folder (GlobalThreadPool, VektorJob, WorkerThread, etc.)
- [x] Deleted domain job files (GenerationJob.cs, LightJob.cs, MeshJob.cs)

**Player Position Save/Restore (2025-11-26):**
- [x] Added PlayerX, PlayerY, PlayerZ, PlayerRotationY to WorldSaveData
- [x] Fixed VoxelBody.Teleport() - must update _currentPosition/_previousPosition (interpolation was overwriting transform.position)
- [x] Fixed SerializeWorldData() - was missing player position fields in manual JSON construction
- [x] Added IPlayer.Teleport(position, yawDegrees) overload for rotation restore
- [x] VektorPlayer.Teleport now sets _desiredLook.y for camera rotation

**New Files This Session:**
- `Assets/VektorVoxels/Data/ChunkData.cs` - Native chunk data struct
- `Assets/VektorVoxels/Data/ChunkDataStore.cs` - World data management
- `Assets/VektorVoxels/Jobs/TerrainGenerationJob.cs` - Burst terrain job
- `Assets/VektorVoxels/Jobs/TerrainJobScheduler.cs` - Terrain job lifecycle manager
- `Assets/VektorVoxels/Jobs/LightingJobs.cs` - Burst lighting jobs (4 job types + BorderSeedJob)
- `Assets/VektorVoxels/Jobs/LightingJobScheduler.cs` - Lighting job lifecycle manager
- `Assets/VektorVoxels/Jobs/MeshingJobs.cs` - Burst meshing job
- `Assets/VektorVoxels/Jobs/MeshingJobScheduler.cs` - Meshing job lifecycle manager

**Deleted Files (Legacy Threading System):**
- `Assets/VektorVoxels/Threading/` - Entire folder (GlobalThreadPool, VektorJob, WorkerThread, etc.)
- `Assets/VektorVoxels/Generation/GenerationJob.cs` - Legacy terrain job
- `Assets/VektorVoxels/Lighting/LightJob.cs` - Legacy lighting job
- `Assets/VektorVoxels/Meshing/MeshJob.cs` - Legacy meshing job

**Phases:**
- [x] Planning and audit
- [x] Phase 1: Data Layer Conversion
- [x] Phase 2: Terrain Generation Jobs
- [x] Phase 3: Lighting System Rearchitecture (coordinated multi-pass)
- [x] Phase 4: Meshing System Conversion
- [x] Phase 5: Main Thread Integration (coordinated lighting queue)
- [x] Phase 6: Persistence Polish (GC fix + player position)
- [x] Phase 7: Legacy System Removal (Threading folder purged)

---

### Chunk Pipeline Refactor (Superseded)
**Status:** superseded by Unity Jobs rearchitecture
**Started:** 2025-11-25
**Updated:** 2025-11-25

**Summary:**
Original incremental refactor plan at `docs/chunk_pipeline_refactor.md`. Phase 1 (retry logic) completed and kept. Phase 2 (remove third pass) attempted but reverted - revealed that the synchronization model fundamentally requires multiple passes for cross-chunk propagation.

Decision made to do full rearchitecture with Unity Jobs instead of incremental patches. Original plan preserved for reference.

**Kept from this effort:**
- Retry logic (3 retries on lock timeout instead of app crash)
- Job counter re-check after lock acquisition
- Sunlight tunnel propagation fix

---

### Codebase Revival & Cleanup (Previous)
**Status:** paused (context preserved, superseded by rearchitecture)
**Started:** 2025-11-21
**Updated:** 2025-11-25

**Summary:**
Initial revival work including 8-bit lighting upgrade, save system implementation, various bug fixes. See previous session notes for details. This work provided foundation for the rearchitecture decision.

**Artifacts:**
- `docs/phase1_summary.md` - Learning summary
- `docs/phase2_summary.md` - Learning summary
- `docs/save_system_plan.md` - Save system design
- `docs/initial_report.md` - Full audit with severity ratings

---

## Format

When starting multi-session work, add an entry:

```markdown
### [Task Name]
**Status:** active | paused | blocked | completed
**Started:** YYYY-MM-DD
**Updated:** YYYY-MM-DD

**Current State:**
Brief summary of where we are.

**Next Steps:**
- [ ] Task 1
- [ ] Task 2

**Notes:**
Relevant context, decisions, blockers.
```

When resuming a session, check this file first to restore context.

---

## Completed Work

### Initial Codebase Audit
**Status:** completed
**Started:** 2025-11-20
**Completed:** 2025-11-20

**Summary:**
- Created comprehensive audit report (`docs/initial_report.md`)
- Added documentation to critical systems
- Set up simplified memory system
- Identified 75+ issues across threading, performance, architecture

**Artifacts:**
- `docs/initial_report.md` - Full audit with severity ratings
- `.claude/memory/architecture.md` - Extracted architectural knowledge
- Code documentation across 15+ files
