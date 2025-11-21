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
        private readonly long _id;
        private readonly Chunk _chunk;
        private readonly Action _callBack;
        
        public GenerationJob(long id, Chunk chunk, Action callBack) {
            _id = id;
            _chunk = chunk;
            _callBack = callBack;
        }

        public override void Execute() {
            // Abort the job if the chunk's counter is != the assigned id.
            if (_chunk.JobCounter != _id) {
                Debug.LogWarning($"Aborting orphaned job with ID: {_id}");
                SignalCompletion(JobCompletionState.Aborted);
                return;
            }

            if (_chunk.ThreadLock.TryEnterWriteLock(GlobalConstants.JOB_LOCK_TIMEOUT_MS)) {
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
                Debug.LogError("Job aborted due to lock timeout expiration!\n" +
                               "Something is probably imploding.");

                SignalCompletion(JobCompletionState.Aborted);

                // This honestly gets us into an invalid state that cannot be recovered from
                // so the application will just exit by default.
                DispatchToContext(() => {
                    if (Application.isEditor) {
                        Debug.Break();
                    }
                    else {
                        Application.Quit();
                    }
                });
            }
        }
    }
}