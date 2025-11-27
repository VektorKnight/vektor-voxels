# Session Tracking

Track active work for continuity across sessions.

---

## Active Work

### Unity Jobs + Burst Rearchitecture
**Status:** active (Phase 6 in progress)
**Started:** 2025-11-25
**Updated:** 2025-11-26

**Goals:**
1. Replace custom thread pool with Unity Job System
2. Add Burst compilation for performance-critical code
3. Fix light removal bug (cascade-clear algorithm)
4. Eliminate GC pressure with NativeContainers
5. Polish existing save/load system

**Planning Document:** `docs/unity_jobs_rearchitecture.md`

**Current State:**
Phases 2-4 complete! Burst-compiled terrain, lighting (first pass), and meshing now running via Unity Jobs. Each system has inspector toggle for A/B testing. User reports noticeable performance improvement and some lighting bugs appear fixed.

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

**Previous Session (2025-11-25):**
- [x] Phase 1 of old refactor plan: Retry logic added to GenerationJob, LightJob, MeshJob
- [x] Fixed sunlight tunnel propagation bug (cavern nodes placed at wrong position)
- [x] Full system audit (threading, meshing, lighting, terrain gen)
- [x] Created comprehensive rearchitecture plan

**Known Bugs (to be fixed by rearchitecture):**
- Light removal doesn't cascade to neighbors (ghost light remains)
- ~~Mystery multi-second hitch~~ - FIXED: Was GC from SaveChunkAsync allocating per-chunk

**Next Session Entry Point:**
Phase 6 complete (GC fix verified). Remaining work:
- Option A: Extend lighting jobs to handle neighbor propagation (phases 2-3 of lighting)
- Option B: Add async job scheduling with batched completion handling (Phase 5)
- Option C: Fix light removal cascade bug (ghost light when removing light sources)

**New Files This Session:**
- `Assets/VektorVoxels/Data/ChunkData.cs` - Native chunk data struct
- `Assets/VektorVoxels/Data/ChunkDataStore.cs` - World data management
- `Assets/VektorVoxels/Jobs/TerrainGenerationJob.cs` - Burst terrain job
- `Assets/VektorVoxels/Jobs/TerrainJobScheduler.cs` - Terrain job lifecycle manager
- `Assets/VektorVoxels/Jobs/LightingJobs.cs` - Burst lighting jobs (4 job types)
- `Assets/VektorVoxels/Jobs/LightingJobScheduler.cs` - Lighting job lifecycle manager
- `Assets/VektorVoxels/Jobs/MeshingJobs.cs` - Burst meshing job
- `Assets/VektorVoxels/Jobs/MeshingJobScheduler.cs` - Meshing job lifecycle manager

**Phases:**
- [x] Planning and audit
- [x] Phase 1: Data Layer Conversion
- [x] Phase 2: Terrain Generation Jobs
- [x] Phase 3: Lighting System Rearchitecture (first pass only)
- [x] Phase 4: Meshing System Conversion
- [ ] Phase 5: Main Thread Integration (async scheduling, batching)
- [x] Phase 6: Persistence Polish (GC fix verified working)

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
