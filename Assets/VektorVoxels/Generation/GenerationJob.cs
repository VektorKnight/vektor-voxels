using System;
using UnityEngine;
using VektorVoxels.Threading;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Chunks {
    /// <summary>
    /// Executes the primary terrain generator on a given chunk.
    /// </summary>
    public readonly struct GenerationJob : IPoolJob {
        private readonly long _id;
        private readonly Chunk _chunk;
        private readonly Action _callBack;

        public long Id => _id;

        public GenerationJob(long id, Chunk chunk, Action callBack) {
            _id = id;
            _chunk = chunk;
            _callBack = callBack;
        }

        public void Execute() {
            // Abort the job if the chunk's counter is != the assigned id.
            if (_chunk.JobCounter != _id) {
                Debug.LogWarning($"Aborting orphaned job with ID: {_id}");
                return;
            }
            
            _chunk.ThreadLock.EnterWriteLock();
            WorldManager.Instance.Generator.ProcessChunk(_chunk);
            GlobalThreadPool.QueueOnMain(_callBack);
            _chunk.ThreadLock.ExitWriteLock();
        }
    }
}