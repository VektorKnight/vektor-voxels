# Session Tracking

Track active work for continuity across sessions.

---

## Active Work

### Chunk Pipeline Refactor
**Status:** active
**Started:** 2025-11-25
**Updated:** 2025-11-25

**Goals:**
1. Simplify chunk pipeline architecture
2. Remove redundant lighting pass
3. Fix synchronization anti-patterns
4. Improve robustness (retry on failure instead of crash)
5. Reduce event subscription overhead

**Current State:**
Comprehensive pipeline audit completed. Critical light propagation bug FIXED (lightmap wasn't being updated during propagation). VoxelColor struct enhanced with attenuation LUT and helper methods. Refactoring plan created at `docs/chunk_pipeline_refactor.md`.

**Critical Fix (2025-11-25):**
Root cause of exponential work in light propagation: `lightMap[cpi] = VoxelColor.Max(...)` was COMMENTED OUT. Every visit to a position saw original dark value, causing infinite revisits via multiple paths.

**VoxelColor Improvements (2025-11-25):**
- Added 4-parameter `Decompose()` (LightMapper compatibility)
- Fixed `Compare` bug (renamed to `AnyChannelGreater`)
- Added `Attenuate()` with pre-computed LUT
- Added `Tint()` and `AttenuateAndTint()` for translucent blocks
- Added `IsBelowThreshold`, `IsBlack`, `DominatesOrEquals` helpers
- Moved attenuation constants to VoxelColor

**Next Session Entry Point:**
Begin Phase 1 of chunk pipeline refactor per `docs/chunk_pipeline_refactor.md`

**Phases:**
- [x] Phase 0: Audit and document pipeline issues
- [ ] Phase 1: Low-risk fixes (retry logic, job counter re-check)
- [ ] Phase 2: Remove third lighting pass
- [ ] Phase 3: Consolidate volatile flags
- [ ] Phase 4: Simplify neighbor synchronization
- [ ] Phase 5: (Optional) Central event bus

---

### Codebase Revival & Cleanup (Previous)
**Status:** paused (blocked by pipeline refactor)
**Started:** 2025-11-21
**Updated:** 2025-11-25

**Goals:**
1. Fast multi-threaded chunk generation/meshing/lighting
2. Full-color (8-bit) RGB voxel lighting
3. Fix PhysX collision stuttering
4. Polish for GitHub showcase

**Phases:**
- [x] Phase 1: Critical fixes, performance wins, code cleanup
- [x] Phase 2: Upgrade lighting to 8-bit per channel
- [x] Phase 2.5: Save system implementation
- [ ] Phase 3: Fix PhysX collision (after pipeline refactor)
- [ ] Phase 4: Polish for showcase

**Save System (2025-11-22):**
- `Assets/VektorVoxels/Persistence/` - WorldSaveData, ChunkSerializer (RLE), VoxelIdRemapper, WorldPersistence
- `Assets/VektorVoxels/UI/WorldUI.cs` - IMGUI interface (F5 to toggle)
- Worlds saved to `Application.persistentDataPath/worlds/{name}/`
- Auto-save every 30 seconds, save on quit
- ID remapping via InternalName for voxel database changes
- Game waits for world create/load before generating chunks
- Plan documented in `docs/save_system_plan.md`

**Bug Fixes (2025-11-22):**
- ConcurrentQueue for voxel updates (thread safety) - `Chunks/Chunk.cs` - **Note: User reverted to regular Queue**
- VoxelTrace division by zero protection - `VoxelPhysics/VoxelTrace.cs`
- Mesh callback latency fix (throttled → default queue) - `Meshing/MeshJob.cs` - **Note: User reverted to Throttled**
- Added `GlobalThreadPool.DispatchAction()` for simple async work
- Added `ActionJob` for thread pool actions

**Latency Investigation (2025-11-22):**
- User reports extreme latency on boundary voxel placement (started after earlier threading changes)
- Distant chunks finish loading before near chunks (expected due to fewer neighbor dependencies)
- Lock contention occurs when chunks try to read neighbors still in Lighting state
- The leapfrog pattern is caused by sequential event handler processing
- User made additional changes: removed _persistenceDirty field, reverted _voxelUpdates to regular Queue

**Documentation Audit (2025-11-22):**
Added XML docs to: VektorPlayer, LightJob, NeighborSet, MeshJob, MeshTables, VoxelUtility, FacingDirection.
Core files (Chunk, VoxelWorld, LightMapper, VoxelBody, VoxelCollider, VoxelTrace) already well-documented.
Enums (ChunkState, LightPass, VoxelFlags, ChunkEvent, NeighborFlags) already documented.

**Memory Optimizations (2025-11-22):**
- VoxelColor (RGB565): Lighting memory halved (512 KB → 256 KB per chunk)
- Consolidated LightColor and Color16 into unified VoxelColor type
- Simplified translucent tinting: ColorData is now direct pass-through multiplier (no attenuation inversion)
- Color16 marked deprecated

**Artifacts:**
- `docs/phase1_summary.md` - Learning summary
- `docs/phase2_summary.md` - Learning summary
- `docs/save_system_plan.md` - Save system design

**Notes:**
User wants learning summaries explaining what/why/how for each phase.
Mesh callback was using Throttled queue causing 1-2s latency on voxel updates - changed to Default queue.

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
