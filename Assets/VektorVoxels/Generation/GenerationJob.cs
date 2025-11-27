using System;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Config;
using VektorVoxels.Threading;
using VektorVoxels.Threading.Jobs;
using VektorVoxels.World;

namespace VektorVoxels.Generation {
    /// <summary>
    /// Executes the primary terrain generator on a given chunk.
    /// </summary>
    public class GenerationJob : VektorJob {
        private const int MAX_RETRIES = 3;

        private readonly long _id;
        private readonly Chunk _chunk;
        private readonly Action _callBack;
        private int _retryCount;

        public GenerationJob(long id, Chunk chunk, Action callBack) {
            _id = id;
            _chunk = chunk;
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
                    Debug.Log($"Generation job {_id} invalidated after lock acquisition, aborting");
                    SignalCompletion(JobCompletionState.Aborted);
                    return;
                }

                try {
                    VoxelWorld.Instance.Generator.ProcessChunk(_chunk);
                }
                finally {
                    _chunk.ThreadLock.ExitWriteLock();
                }

                // Signal completion.
                SignalCompletion(JobCompletionState.Completed);

                // Invoke callback on main if specified.
                if (_callBack != null) {
                    DispatchToMain(_callBack, QueueType.Default);
                }
            }
            else {
                _retryCount++;
                if (_retryCount <= MAX_RETRIES) {
                    Debug.LogWarning($"Generation job failed to acquire lock, retry {_retryCount}/{MAX_RETRIES}");
                    GlobalThreadPool.DispatchJob(this);
                    return;
                }

                Debug.LogError($"Generation job failed after {MAX_RETRIES} retries. Chunk may be in invalid state.");
                SignalCompletion(JobCompletionState.Aborted);
            }
        }
    }
}