# Session Tracking

Track active work for continuity across sessions.

---

## Active Work

### Codebase Revival & Cleanup
**Status:** active
**Started:** 2025-11-21
**Updated:** 2025-11-22

**Goals:**
1. Fast multi-threaded chunk generation/meshing/lighting
2. Full-color (8-bit) RGB voxel lighting
3. Fix PhysX collision stuttering
4. Polish for GitHub showcase

**Current State:**
Save system implemented. Several critical bug fixes applied. Lighting edge case fix was attempted but reverted - needs reinvestigation.

**Next Session Entry Point:**
1. **Investigate lighting boundary edge cases** - Reverted fix needs simpler approach
   - Issue: Partial chunks don't get proper boundary lighting when neighbors load later
   - Previous approach (subscription/polling) was too complex
   - Consider using existing LoadRegionChanged event mechanism
   - See `CheckAllNeighborsReady()` method (currently unused) in Chunk.cs

2. **Continue with Phase 3** - PhysX collision stuttering fix

**Phases:**
- [x] Phase 1: Critical fixes, performance wins, code cleanup
- [x] Phase 2: Upgrade lighting to 8-bit per channel
- [x] Phase 2.5: Save system implementation
- [ ] Phase 3: Fix PhysX collision
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
- ConcurrentQueue for voxel updates (thread safety) - `Chunks/Chunk.cs`
- VoxelTrace division by zero protection - `VoxelPhysics/VoxelTrace.cs`
- Mesh callback latency fix (throttled → default queue) - `Meshing/MeshJob.cs`
- Added `GlobalThreadPool.DispatchAction()` for simple async work
- Added `ActionJob` for thread pool actions

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
