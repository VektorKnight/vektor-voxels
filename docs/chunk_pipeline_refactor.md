# Chunk Pipeline Refactoring Plan

**Created:** 2025-11-25
**Status:** Ready for review
**Estimated Phases:** 5 (incremental, each phase is independently shippable)

---

## Executive Summary

This document outlines a phased refactoring of the chunk pipeline to address architectural issues identified during the 2025-11-25 audit. Each phase is designed to be:

1. **Independently testable** - Can verify correctness before proceeding
2. **Safely revertible** - Isolated changes that can be rolled back
3. **Incrementally beneficial** - Each phase provides value even if later phases are skipped

### Issues Being Addressed

| Issue | Severity | Phase |
|-------|----------|-------|
| Lock timeout = app exit | Critical | 1 |
| Aborted jobs don't retry | Major | 1 |
| Job counter not re-checked after lock | Minor | 1 |
| Three identical lighting passes | Major | 2 |
| Volatile flags used non-atomically | Major | 3 |
| Mixed push/pull synchronization | Major | 4 |
| Event subscription overhead (N²) | Minor | 5 |

---

## Phase 0: Already Completed

**Status:** DONE (2025-11-25)

### 0.1 Light Propagation Bug Fix

**File:** `Assets/VektorVoxels/Lighting/LightMapper.cs`

**Problem:** Lightmap was never updated during propagation (code was commented out), causing exponential revisits.

**Fix Applied:**
```csharp
// Before: commented out
//if (improves) {
//    lightMap[cpi] = VoxelColor.Max(current, node.Value);
//}

// After: restored and working
if (!improves) continue;
lightMap[cpi] = VoxelColor.Max(current, node.Value);
```

### 0.2 VoxelColor Enhancements

**File:** `Assets/VektorVoxels/Lighting/VoxelColor.cs`

**Changes Applied:**
- Added 4-parameter `Decompose(out r, out g, out b, out a)` for LightMapper compatibility
- Fixed `Compare` bug (was comparing R twice instead of B), renamed to `AnyChannelGreater`
- Added `Attenuate()` with pre-computed 256-byte LUT
- Added `Tint()` and `AttenuateAndTint()` for translucent blocks
- Added `IsBelowThreshold`, `IsBlack`, `DominatesOrEquals`, `ChannelSum` helpers
- Made constants public: `ATTENUATION_MULTIPLIER`, `LIGHT_THRESHOLD`, `MAX_LIGHT`

---

## Phase 1: Robustness Improvements

**Goal:** Make the system resilient to transient failures instead of crashing
**Risk:** Low
**Files Modified:** 3
**Estimated Effort:** 30-60 minutes

### 1.1 Replace App Exit with Retry Logic

**Files:**
- `Assets/VektorVoxels/Generation/GenerationJob.cs`
- `Assets/VektorVoxels/Lighting/LightJob.cs`
- `Assets/VektorVoxels/Meshing/MeshJob.cs`

**Current Behavior (BAD):**
```csharp
// In all three job files, lock timeout causes app to exit
else {
    Debug.LogError("Job aborted due to lock timeout expiration!");
    SignalCompletion(JobCompletionState.Aborted);

    DispatchToContext(() => {
        if (Application.isEditor) Debug.Break();
        else Application.Quit();  // <-- This is extreme
    });
}
```

**New Behavior:**

#### Step 1.1.1: Add retry counter to job base class or per-job

In each job constructor, add:
```csharp
private int _retryCount = 0;
private const int MAX_RETRIES = 3;
```

#### Step 1.1.2: Replace exit logic with retry

Change the lock timeout handler to:
```csharp
else {
    _retryCount++;
    if (_retryCount <= MAX_RETRIES) {
        Debug.LogWarning($"Job failed to acquire lock, retry {_retryCount}/{MAX_RETRIES}");
        // Re-queue the job with a small delay
        DispatchToMain(() => {
            GlobalThreadPool.DispatchJob(this);
        }, QueueType.Default);
        return; // Don't signal completion yet
    }

    Debug.LogError($"Job failed after {MAX_RETRIES} retries. Chunk may be in invalid state.");
    SignalCompletion(JobCompletionState.Aborted);

    // Mark chunk for reload instead of crashing
    DispatchToMain(() => {
        _chunk._waitingForReload = true;
        _chunk._isDirty = true;
    }, QueueType.Default);
}
```

#### Step 1.1.3: Handle neighbor lock failures in LightJob

**File:** `Assets/VektorVoxels/Lighting/LightJob.cs:72-76`

**Current:**
```csharp
if (!success) {
    Debug.LogWarning($"Light pass {_pass} failed to acquire neighbor locks");
    SignalCompletion(JobCompletionState.Aborted);
    return;  // Chunk stuck!
}
```

**Change to:**
```csharp
if (!success) {
    _retryCount++;
    if (_retryCount <= MAX_RETRIES) {
        Debug.LogWarning($"Light pass {_pass} failed to acquire neighbor locks, retry {_retryCount}/{MAX_RETRIES}");
        // Small delay then retry
        Thread.Sleep(10);
        GlobalThreadPool.DispatchJob(this);
        return;
    }
    Debug.LogError($"Light pass failed after {MAX_RETRIES} retries");
    SignalCompletion(JobCompletionState.Aborted);
    return;
}
```

### 1.2 Re-check Job Counter After Lock Acquisition

**Files:** Same 3 job files

**Current:** Job counter only checked at start of `Execute()`

**Problem:** A reload could invalidate the job between the check and lock acquisition.

**Fix:** Add re-check immediately after acquiring lock:

```csharp
if (_chunk.ThreadLock.TryEnterWriteLock(GlobalConstants.JOB_LOCK_TIMEOUT_MS)) {
    // ADD THIS CHECK:
    if (_chunk.JobCounter != _id) {
        _chunk.ThreadLock.ExitWriteLock();
        Debug.Log($"Job {_id} invalidated after lock acquisition, aborting");
        SignalCompletion(JobCompletionState.Aborted);
        return;
    }

    try {
        // ... existing work ...
    }
    finally {
        _chunk.ThreadLock.ExitWriteLock();
    }
}
```

### 1.3 Verification Steps

After completing Phase 1:

1. **Test normal operation:** Load world, walk around, place/break blocks
2. **Test retry logic:** Add artificial delay in one job to force timeout
3. **Test recovery:** Verify chunks recover after transient failures
4. **Verify no crashes:** Ensure app never exits due to lock timeouts

---

## Phase 2: Remove Third Lighting Pass

**Goal:** Eliminate redundant work by reducing from 3 passes to 2
**Risk:** Medium (requires testing edge cases)
**Files Modified:** 2
**Estimated Effort:** 1-2 hours (including testing)

### 2.1 Background

Currently the pipeline runs:
```
Generation → Light1 → Wait → Light2 → Wait → Light3 → Wait → Mesh
```

Passes 2 and 3 are **identical**:
```csharp
case LightPass.Second:
    success = lightMapper.InitializeNeighborLightPass(_chunk, _neighbors);
    if (success) {
        lightMapper.PropagateSunLight(_chunk);
        lightMapper.PropagateBlockLight(_chunk);
    }
    break;
case LightPass.Third:
    // EXACT SAME CODE
```

With the propagation bug fixed, light should fully propagate in 2 passes:
1. Pass 1: Initialize and propagate within chunk
2. Pass 2: Propagate from neighbors (all 4 cardinal directions)

### 2.2 Implementation Steps

#### Step 2.2.1: Update LightPass enum

**File:** `Assets/VektorVoxels/Lighting/LightPass.cs` (or wherever defined)

**Current:**
```csharp
public enum LightPass {
    None,
    First,
    Second,
    Third
}
```

**Change to:** (keep Third for backward compatibility but mark obsolete)
```csharp
public enum LightPass {
    None,
    First,
    Second,
    [Obsolete("Third pass removed - two passes sufficient")]
    Third = Second  // Alias to Second for any lingering references
}
```

#### Step 2.2.2: Remove Third pass case from LightJob

**File:** `Assets/VektorVoxels/Lighting/LightJob.cs`

**Remove:**
```csharp
case LightPass.Third:
    success = lightMapper.InitializeNeighborLightPass(_chunk, _neighbors);
    if (success) {
        lightMapper.PropagateSunLight(_chunk);
        lightMapper.PropagateBlockLight(_chunk);
    }
    break;
```

#### Step 2.2.3: Update Chunk state transitions

**File:** `Assets/VektorVoxels/Chunks/Chunk.cs`

In `CheckForNeighborState()`, change:
```csharp
switch (_lightPass) {
    case LightPass.First:
        QueueLightPass(LightPass.Second);
        break;
    case LightPass.Second:
        QueueMeshPass();  // Skip Third, go directly to mesh
        break;
    // Remove Third case
}
```

#### Step 2.2.4: Update neighbor state checking

In `CheckForNeighborState()`, the neighbor requirement check:
```csharp
if (neighbor.State < ChunkState.WaitingForNeighbors || neighbor.LightPass < _lightPass) {
    return;
}
```

This should still work - neighbors need to be at same pass level.

#### Step 2.2.5: Clean up callback references

**File:** `Assets/VektorVoxels/Chunks/Chunk.cs`

Remove `_lightCallback3`:
```csharp
// Remove this line from field declarations
private Action _lightCallback1, _lightCallback2, _lightCallback3;

// Change to:
private Action _lightCallback1, _lightCallback2;

// Remove from Initialize():
_lightCallback3 = () => OnLightPassComplete(LightPass.Third);
```

### 2.3 Verification Steps

After completing Phase 2:

1. **Visual inspection:** Look for lighting artifacts at chunk boundaries
2. **Edge case testing:**
   - Place torch at chunk corner
   - Dig tunnel crossing chunk boundary
   - Place colored glass at boundary
3. **Performance measurement:** Compare chunk load time before/after
4. **Regression test:** Verify existing saved worlds still load correctly

### 2.4 Rollback Plan

If issues found:
1. Revert `LightPass` enum change
2. Restore `case LightPass.Third:` in `LightJob`
3. Restore `case LightPass.Second: → QueueLightPass(Third)` in Chunk
4. Restore `_lightCallback3`

---

## Phase 3: Consolidate Volatile Flags

**Goal:** Replace multiple volatile bools with atomic state management
**Risk:** Medium-High (threading changes)
**Files Modified:** 1 (major changes to Chunk.cs)
**Estimated Effort:** 2-3 hours

### 3.1 Problem

Current code has multiple volatile flags read in non-atomic combinations:

```csharp
// These are separate volatile reads - not atomic together!
if (_waitingForReload || _isDirty) {
    Reload();
}
if (_waitingForUnload) {
    Unload();
}
```

### 3.2 Solution: Bitfield with Interlocked Operations

#### Step 3.2.1: Define flag constants

**File:** `Assets/VektorVoxels/Chunks/Chunk.cs`

Add near top of class:
```csharp
// Chunk flags as bit positions
[Flags]
private enum ChunkFlags {
    None = 0,
    WaitingForJob = 1 << 0,
    PartialLoad = 1 << 1,
    WaitingForReload = 1 << 2,
    WaitingForUnload = 1 << 3,
    IsDirty = 1 << 4,
    PersistenceDirty = 1 << 5,
    SubscribedToNeighbors = 1 << 6
}

private volatile int _flags;
```

#### Step 3.2.2: Add helper methods

```csharp
private bool HasFlag(ChunkFlags flag) {
    return (Volatile.Read(ref _flags) & (int)flag) != 0;
}

private void SetFlag(ChunkFlags flag) {
    int oldFlags, newFlags;
    do {
        oldFlags = Volatile.Read(ref _flags);
        newFlags = oldFlags | (int)flag;
    } while (Interlocked.CompareExchange(ref _flags, newFlags, oldFlags) != oldFlags);
}

private void ClearFlag(ChunkFlags flag) {
    int oldFlags, newFlags;
    do {
        oldFlags = Volatile.Read(ref _flags);
        newFlags = oldFlags & ~(int)flag;
    } while (Interlocked.CompareExchange(ref _flags, newFlags, oldFlags) != oldFlags);
}

private bool TrySetFlag(ChunkFlags flag) {
    int oldFlags = Volatile.Read(ref _flags);
    if ((oldFlags & (int)flag) != 0) return false; // Already set
    int newFlags = oldFlags | (int)flag;
    return Interlocked.CompareExchange(ref _flags, newFlags, oldFlags) == oldFlags;
}

// Atomic read of multiple flags at once
private int GetFlags() {
    return Volatile.Read(ref _flags);
}
```

#### Step 3.2.3: Replace usages throughout Chunk.cs

**Before:**
```csharp
private volatile bool _waitingForJob;
_waitingForJob = true;
if (_waitingForJob) { ... }
```

**After:**
```csharp
SetFlag(ChunkFlags.WaitingForJob);
if (HasFlag(ChunkFlags.WaitingForJob)) { ... }
```

#### Step 3.2.4: Fix atomic multi-flag checks

**Before:**
```csharp
if (_waitingForReload || _isDirty) {
    Reload();
}
```

**After:**
```csharp
int flags = GetFlags();
if ((flags & ((int)ChunkFlags.WaitingForReload | (int)ChunkFlags.IsDirty)) != 0) {
    Reload();
}
```

### 3.3 Migration Checklist

Replace each occurrence:

| Old | New |
|-----|-----|
| `_waitingForJob = true` | `SetFlag(ChunkFlags.WaitingForJob)` |
| `_waitingForJob = false` | `ClearFlag(ChunkFlags.WaitingForJob)` |
| `_waitingForJob` | `HasFlag(ChunkFlags.WaitingForJob)` |
| `_partialLoad` | `HasFlag(ChunkFlags.PartialLoad)` |
| `_waitingForReload` | `HasFlag(ChunkFlags.WaitingForReload)` |
| `_waitingForUnload` | `HasFlag(ChunkFlags.WaitingForUnload)` |
| `_isDirty` | `HasFlag(ChunkFlags.IsDirty)` |
| `_persistenceDirty` | `HasFlag(ChunkFlags.PersistenceDirty)` |
| `_subscribedToNeighbors` | `HasFlag(ChunkFlags.SubscribedToNeighbors)` |

### 3.4 Verification Steps

1. **Thread safety test:** Rapid voxel placement while chunks loading
2. **State consistency:** Add debug assertions to verify flag invariants
3. **Stress test:** Load large view distance, walk quickly to trigger rapid load/unload

---

## Phase 4: Simplify Neighbor Synchronization

**Goal:** Choose one synchronization strategy (events OR polling), not both
**Risk:** Medium
**Files Modified:** 1 (Chunk.cs)
**Estimated Effort:** 2-3 hours

### 4.1 Decision Point

**Option A: Pure Events (Recommended)**
- Remove polling fallback
- Fix event edge cases instead
- Simpler mental model

**Option B: Pure Polling**
- Remove event subscriptions
- Poll every N frames
- More predictable but less responsive

### 4.2 Implementation: Pure Events

#### Step 4.2.1: Identify why polling exists

The comment says:
```csharp
// This catches newly loaded neighbors that weren't subscribed when we started waiting.
```

**Root cause:** When chunk enters `WaitingForNeighbors`, it subscribes to neighbors. But if a neighbor loads AFTER we subscribed, we miss its event.

**Fix:** Subscribe to `VoxelWorld.OnChunkLoaded` event instead of individual neighbors.

#### Step 4.2.2: Add world-level chunk loaded event

**File:** `Assets/VektorVoxels/World/VoxelWorld.cs`

Add event:
```csharp
public static event Action<Chunk> OnChunkLoaded;
```

Fire it when chunk finishes loading:
```csharp
// In the loading loop, after chunk.Initialize():
OnChunkLoaded?.Invoke(chunk);
```

#### Step 4.2.3: Subscribe to world event instead of neighbor events

**File:** `Assets/VektorVoxels/Chunks/Chunk.cs`

In `Initialize()`:
```csharp
VoxelWorld.OnChunkLoaded += OnAnyChunkLoaded;
```

Add handler:
```csharp
private void OnAnyChunkLoaded(Chunk loadedChunk) {
    // Check if this is one of our neighbors
    if (_state != ChunkState.WaitingForNeighbors) return;

    var offset = loadedChunk.ChunkId - _chunkId;
    bool isNeighbor = Math.Abs(offset.x) <= 1 && Math.Abs(offset.y) <= 1
                      && !(offset.x == 0 && offset.y == 0);

    if (isNeighbor) {
        CheckForNeighborState();
    }
}
```

#### Step 4.2.4: Remove polling from OnTick

**Remove:**
```csharp
if (_state == ChunkState.WaitingForNeighbors) {
    if (Time.frameCount % 2 == 0) {
        CheckForNeighborState();  // Remove this polling
    }
    return;
}
```

#### Step 4.2.5: Clean up in OnDestroy

```csharp
VoxelWorld.OnChunkLoaded -= OnAnyChunkLoaded;
```

### 4.3 Verification Steps

1. **Fresh world load:** Verify all chunks complete loading
2. **Boundary testing:** Chunk at view edge loads, unloads, reloads
3. **Performance:** Measure CPU usage without polling overhead

---

## Phase 5: (Optional) Central Event Bus

**Goal:** Reduce N² event connections to N
**Risk:** Low (additive change)
**Files Modified:** 2-3
**Estimated Effort:** 1-2 hours

### 5.1 Background

Current system: Each chunk subscribes to each neighbor's `OnLightPassCompleted` event.
With 100 chunks loaded: up to 800 subscriptions (8 neighbors × 100 chunks).

Central bus: Single subscription point, chunks filter relevant events.

### 5.2 Implementation

#### Step 5.2.1: Add central event in VoxelWorld

```csharp
// Central light pass completion notification
public static event Action<Chunk, LightPass> OnAnyChunkLightPassCompleted;

// Call this from Chunk.OnLightPassComplete instead of firing per-chunk event
internal static void NotifyLightPassCompleted(Chunk chunk, LightPass pass) {
    OnAnyChunkLightPassCompleted?.Invoke(chunk, pass);
}
```

#### Step 5.2.2: Update Chunk to use central event

Remove per-chunk event subscription logic. Instead:

```csharp
// In Initialize()
VoxelWorld.OnAnyChunkLightPassCompleted += OnAnyChunkLightComplete;

// Handler
private void OnAnyChunkLightComplete(Chunk other, LightPass pass) {
    if (_state != ChunkState.WaitingForNeighbors) return;
    if (pass < _lightPass) return;

    // Check if other is our neighbor
    var diff = other.ChunkId - _chunkId;
    if (Math.Abs(diff.x) <= 1 && Math.Abs(diff.y) <= 1) {
        CheckForNeighborState();
    }
}
```

#### Step 5.2.3: Remove per-neighbor subscription methods

Delete:
- `SubscribeToNeighborEvents()`
- `UnsubscribeFromNeighborEvents()`
- `OnNeighborLightPassCompleted()`
- `_subscribedToNeighbors` flag

### 5.3 Verification Steps

1. **Event count:** Log event subscriptions before/after
2. **Functional test:** Same as Phase 4
3. **Memory profiling:** Verify reduced delegate allocations

---

## Testing Checklist (All Phases)

### Functional Tests

- [ ] Fresh world generation completes
- [ ] Saved world loads correctly
- [ ] Place/break voxels at chunk center
- [ ] Place/break voxels at chunk boundary
- [ ] Place/break light source voxels
- [ ] Place/break translucent voxels
- [ ] Walk across chunk boundaries
- [ ] Rapid movement (teleport) to force chunk load/unload

### Performance Tests

- [ ] Initial world load time
- [ ] Chunk load time (average)
- [ ] Frame time during voxel edits
- [ ] CPU usage while idle
- [ ] Memory usage (no leaks after load/unload cycles)

### Edge Cases

- [ ] Load world at chunk boundary
- [ ] Light source at chunk corner
- [ ] Tunnel through multiple chunks
- [ ] Colored glass at chunk boundary
- [ ] Unload chunk with pending job
- [ ] Reload chunk mid-lighting

---

## Appendix: File Reference

| File | Phases | Notes |
|------|--------|-------|
| `Chunks/Chunk.cs` | 1, 2, 3, 4, 5 | Main target |
| `Generation/GenerationJob.cs` | 1 | Retry logic |
| `Lighting/LightJob.cs` | 1, 2 | Retry + remove pass |
| `Meshing/MeshJob.cs` | 1 | Retry logic |
| `Lighting/LightPass.cs` | 2 | Enum update |
| `Lighting/LightMapper.cs` | 0 (done) | Bug fixed |
| `Lighting/VoxelColor.cs` | 0 (done) | Enhanced |
| `World/VoxelWorld.cs` | 4, 5 | Events |

---

## Revision History

| Date | Author | Changes |
|------|--------|---------|
| 2025-11-25 | Claude | Initial plan created |
