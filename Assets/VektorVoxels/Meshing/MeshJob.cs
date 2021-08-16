using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Threading;

namespace VektorVoxels.Meshing {
    public class MeshJob : PoolJob {
        private readonly Chunk _chunk;
        private readonly NeighborSet _neighbors;
        private readonly CubicMesher _mesher;
        private readonly Action _callBack;

        public MeshJob(long id, Chunk chunk, NeighborSet neighbors, CubicMesher mesher, Action callBack) : base(id) {
            _chunk = chunk;
            _neighbors = neighbors;
            _mesher = mesher;
            _callBack = callBack;
            CompletionState = JobCompletionState.None;
        }

        public override void Execute() {
            // Abort the job if the chunk's counter is != the assigned id.
            if (_chunk.JobCounter != Id) {
                Debug.LogWarning($"Aborting orphaned job with ID: {Id}");
                CompletionState = JobCompletionState.Aborted;
                return;
            }
            
            // Acquire a read lock on the chunk and generate mesh data.
            _chunk.ThreadLock.EnterReadLock();
            _mesher.GenerateMeshData(_chunk, _neighbors);
            _chunk.ThreadLock.ExitReadLock();
            
            // Signal completion.
            CompletionState = JobCompletionState.Completed;

            // Invoke callback on main if specified.
            if (_callBack != null) {
                GlobalThreadPool.QueueOnMain(_callBack);
            }
        }
    }
}