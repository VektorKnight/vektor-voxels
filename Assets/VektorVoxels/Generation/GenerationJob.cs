using System;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Threading;
using VektorVoxels.World;

namespace VektorVoxels.Generation {
    /// <summary>
    /// Executes the primary terrain generator on a given chunk.
    /// </summary>
    public class GenerationJob : PoolJob {
        private readonly Chunk _chunk;
        private readonly Action _callBack;
        
        public GenerationJob(long id, Chunk chunk, Action callBack) : base(id) {
            _chunk = chunk;
            _callBack = callBack;
        }

        public override void Execute() {
            // Abort the job if the chunk's counter is != the assigned id.
            if (_chunk.JobCounter != Id) {
                Debug.LogWarning($"Aborting orphaned job with ID: {Id}");
                CompletionState = JobCompletionState.Aborted;
                return;
            }
            
            _chunk.ThreadLock.EnterWriteLock();
            WorldManager.Instance.Generator.ProcessChunk(_chunk);
            _chunk.ThreadLock.ExitWriteLock();
            
            // Signal completion.
            CompletionState = JobCompletionState.Completed;
            
            // Invoke callback on main if specified.
            if (_callBack != null) {
                GlobalThreadPool.QueueOnMain(_callBack);
            }
        }
    }
}