# Phase 1: Critical Safety & Performance Fixes

## Summary

Phase 1 focused on fixing critical safety issues that could cause crashes, deadlocks, or resource leaks, along with low-effort performance improvements.

## Changes Made

### 1. Thread-Safe Lock Acquisition (MeshJob, LightJob, GenerationJob)

**What**: Added `try-finally` blocks around all `ReaderWriterLockSlim` acquisitions.

**Why**: Without `try-finally`, if an exception occurs while holding a lock, the lock is never released. This causes deadlocks - other threads wait forever for a lock that will never be released.

**How**:
```csharp
// Before - unsafe
if (_chunk.ThreadLock.TryEnterReadLock(timeout)) {
    var mesher = VisualMesher.LocalThreadInstance;
    mesher.GenerateMeshData(_chunk, _neighbors);  // Exception here = deadlock
    _chunk.ThreadLock.ExitReadLock();
}

// After - safe
if (_chunk.ThreadLock.TryEnterReadLock(timeout)) {
    try {
        var mesher = VisualMesher.LocalThreadInstance;
        mesher.GenerateMeshData(_chunk, _neighbors);
    }
    finally {
        _chunk.ThreadLock.ExitReadLock();  // Always executes
    }
}
```

**Files**: `MeshJob.cs`, `LightJob.cs`, `GenerationJob.cs`

---

### 2. Cooperative Thread Shutdown (WorkerThread)

**What**: Replaced `Thread.Abort()` with `Thread.Interrupt()` and proper exception handling.

**Why**: `Thread.Abort()` is dangerous and deprecated - it can terminate a thread at any point, even in the middle of a critical section, causing corrupted state. `Thread.Interrupt()` only interrupts blocking calls (like `Sleep` or `Wait`), allowing the thread to clean up properly.

**How**:
```csharp
// Worker can be interrupted during Sleep/blocking calls
public void Interrupt() {
    _shuttingDown = true;
    _status = ThreadStatus.Offline;
    _thread.Interrupt();
}

private void WorkLoop() {
    try {
        while (true) {
            // ... work loop
        }
    }
    catch (ThreadInterruptedException) {
        // Expected when Interrupt() is called - exit gracefully
        _status = ThreadStatus.Offline;
    }
}
```

**Files**: `WorkerThread.cs`, `ThreadPool.cs`

---

### 3. ThreadPool.Shutdown() Timeout Fix

**What**: Added a 5-second timeout to `Shutdown()` method.

**Why**: The original code had an infinite loop waiting for all workers to go offline. If any worker was stuck, the application would hang forever on shutdown.

**How**:
```csharp
public void Shutdown() {
    _workQueue.CompleteAdding();

    var timeout = 5000;
    var elapsed = 0;
    while (elapsed < timeout) {
        var allOffline = true;
        foreach (var worker in _workers) {
            if (worker.Status != ThreadStatus.Offline) {
                allOffline = false;
                break;
            }
        }
        if (allOffline) return;

        Thread.Sleep(10);
        elapsed += 10;
    }
    Debug.LogWarning("ThreadPool.Shutdown() timed out waiting for workers.");
}
```

**Files**: `ThreadPool.cs`

---

### 4. Chunk OnDestroy Cleanup

**What**: Added `OnDestroy()` method to unsubscribe from events and dispose the lock.

**Why**:
- Event subscriptions without matching unsubscriptions cause memory leaks - the delegate keeps the Chunk alive even after destruction.
- `ReaderWriterLockSlim` implements `IDisposable` and should be disposed to release OS resources.

**How**:
```csharp
private void OnDestroy() {
    VoxelWorld.OnWorldEvent -= WorldEventHandler;
    _threadLock?.Dispose();
}
```

**Files**: `Chunk.cs`

---

### 5. Queue.Contains() Optimization (VoxelWorld)

**What**: Added a `HashSet<Chunk>` to track queue membership for O(1) lookups.

**Why**: `Queue<T>.Contains()` is O(n) - it iterates through every element. For a queue with hundreds of chunks, this becomes expensive. A HashSet provides O(1) average-case lookups.

**How**:
```csharp
private Queue<Chunk> _loadQueue;
private HashSet<Chunk> _loadQueueSet;  // Mirror set for fast lookups

// When adding:
if (_loadQueueSet.Contains(chunk)) continue;  // O(1) instead of O(n)
_loadQueue.Enqueue(chunk);
_loadQueueSet.Add(chunk);

// When removing:
var chunk = _loadQueue.Dequeue();
_loadQueueSet.Remove(chunk);
```

**Files**: `VoxelWorld.cs`

---

### 6. Conditional Chunk Sorting

**What**: Only sort chunks when the load region changes, not every frame.

**Why**: `List<T>.Sort()` is O(n log n). Running this every frame for hundreds of chunks wastes CPU time. Sorting only when the player moves to a new chunk position is sufficient.

**How**:
```csharp
private bool _needsSort;

// In FixedUpdate when load region changes:
if (!_loadRect.Equals(loadPrev)) {
    _needsSort = true;
    OnWorldEvent?.Invoke(WorldEvent.LoadRegionChanged);
}

// In Update:
if (_needsSort) {
    _loadedChunks.Sort(CompareChunks);
    _needsSort = false;
}
```

**Files**: `VoxelWorld.cs`

---

### 7. VisualMesher Loop Order Optimization

**What**: Changed loop order from Y-Z-X to Z-Y-X for better cache locality.

**Why**: The voxel array is indexed as `x + d.x * (y + d.y * z)`, meaning X varies fastest, then Y, then Z. Iterating in Z-Y-X order (Z outermost) means sequential memory accesses, which is 10-50x faster than random access due to CPU cache behavior.

**How**:
```csharp
// Before - cache unfriendly
for (var y = 0; y < d.y; y++)
    for (var z = 0; z < d.x; z++)
        for (var x = 0; x < d.x; x++)

// After - cache friendly (Z outermost, X innermost)
for (var z = 0; z < d.x; z++)
    for (var y = 0; y < d.y; y++)
        for (var x = 0; x < d.x; x++)
```

**Files**: `VisualMesher.cs`

---

### 8. Dead Code Removal

**What**: Removed test `ExampleJob` code and unnecessary `async/await`.

**Why**: Test code shouldn't be in production. The `async void Awake()` was only async for the test job - removing it simplifies the code and avoids the pitfalls of async void (exceptions are unobservable).

**Files**: `VoxelWorld.cs`

---

## Key Takeaways

1. **Always use try-finally with locks** - Exceptions happen, and deadlocks are hard to debug.

2. **Avoid Thread.Abort()** - Use cooperative shutdown with flags and Interrupt() for blocking calls.

3. **Dispose IDisposable objects** - Especially locks, streams, and any OS resources.

4. **Unsubscribe from events** - Every `+=` needs a matching `-=` to prevent memory leaks.

5. **Use appropriate data structures** - HashSet for membership testing, not Queue.Contains().

6. **Avoid per-frame work when possible** - Sort/search operations should be event-driven when the data rarely changes.

7. **Iterate arrays in memory order** - Understand your data layout and iterate to maximize cache hits.

## Next Steps

Phase 2 will focus on upgrading the lighting system from 4-bit to 8-bit per channel for full-color RGB lighting.
