using System;
using System.Diagnostics;
using System.Threading;
using Unity.Jobs;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Config;
using VektorVoxels.Threading;
using VektorVoxels.Threading.Jobs;
using Debug = UnityEngine.Debug;

namespace VektorVoxels.Lighting {
    /// <summary>
    /// Worker job for chunk light propagation. Executes one of three passes:
    /// First pass initializes sun/block light and propagates within the chunk.
    /// Second/Third passes pull light from neighbors that have completed their passes.
    /// Aborts if chunk job counter has been invalidated (chunk unloaded/reloaded).
    /// </summary>
    public class LightJob : VektorJob {
        private const int MAX_RETRIES = 3;

        private readonly long _id;
        private readonly Chunk _chunk;
        private readonly NeighborSet _neighbors;
        private readonly LightPass _pass;
        private readonly Action _callBack;
        private int _retryCount;

        public LightJob(long id, Chunk chunk, NeighborSet neighbors, LightPass pass, Action callBack) {
            _id = id;
            _chunk = chunk;
            _neighbors = neighbors;
            _pass = pass;
            _callBack = callBack;
            _retryCount = 0;
        }

        public override void Execute() {
            // Abort the job if the chunk's counter is != the assigned id.
            if (_chunk.JobCounter != _id) {
                Debug.LogWarning($"Aborting orphaned job with ID: {_id}");
                SignalCompletion(JobCompletionState.Aborted);
                return;
            }
            
            if (_chunk.ThreadLock.TryEnterWriteLock(GlobalConstants.JOB_LOCK_TIMEOUT_MS)) {
                // Re-check job counter after acquiring lock - a reload could have invalidated us.
                if (_chunk.JobCounter != _id) {
                    _chunk.ThreadLock.ExitWriteLock();
                    Debug.Log($"Light job {_id} invalidated after lock acquisition, aborting");
                    SignalCompletion(JobCompletionState.Aborted);
                    return;
                }

                try {
                    var lightMapper = LightMapper.LocalThreadInstance;
                    bool success = true;
                    switch (_pass) {
                        case LightPass.None:
                            break;
                        case LightPass.First:
                            lightMapper.InitializeSunLightFirstPass(_chunk);
                            lightMapper.InitializeBlockLightFirstPass(_chunk);
                            lightMapper.PropagateSunLight(_chunk);
                            lightMapper.PropagateBlockLight(_chunk);
                            break;
                        case LightPass.Second:
                        case LightPass.Third:
                            success = lightMapper.InitializeNeighborLightPass(_chunk, _neighbors);
                            if (success) {
                                lightMapper.PropagateSunLight(_chunk);
                                lightMapper.PropagateBlockLight(_chunk);
                            }
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    if (!success) {
                        _chunk.ThreadLock.ExitWriteLock();
                        _retryCount++;
                        if (_retryCount <= MAX_RETRIES) {
                            Debug.LogWarning($"Light pass {_pass} failed to acquire neighbor locks, retry {_retryCount}/{MAX_RETRIES}");
                            Thread.Sleep(10);
                            GlobalThreadPool.DispatchJob(this);
                            return;
                        }
                        Debug.LogError($"Light pass {_pass} failed after {MAX_RETRIES} retries");
                        SignalCompletion(JobCompletionState.Aborted);
                        return;
                    }
                }
                finally {
                    if (_chunk.ThreadLock.IsWriteLockHeld) {
                        _chunk.ThreadLock.ExitWriteLock();
                    }
                }

                // Signal completion.
                SignalCompletion(JobCompletionState.Completed);

                // Invoke callback on main if specified.
                if (_callBack != null) {
                    GlobalThreadPool.DispatchOnMain(_callBack, QueueType.Default);
                }
            }
            else {
                _retryCount++;
                if (_retryCount <= MAX_RETRIES) {
                    Debug.LogWarning($"Light job failed to acquire lock, retry {_retryCount}/{MAX_RETRIES}");
                    GlobalThreadPool.DispatchJob(this);
                    return;
                }

                Debug.LogError($"Light job failed after {MAX_RETRIES} retries. Chunk may be in invalid state.");
                SignalCompletion(JobCompletionState.Aborted);
            }
        }
    }
}