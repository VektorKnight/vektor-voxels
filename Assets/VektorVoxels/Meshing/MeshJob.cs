using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Config;
using VektorVoxels.Threading;
using VektorVoxels.Threading.Jobs;

namespace VektorVoxels.Meshing {
    public class MeshJob : VektorJob {
        private readonly long _id;
        private readonly Chunk _chunk;
        private readonly NeighborSet _neighbors;
        private Mesh.MeshDataArray _meshData;
        private readonly Action _callBack;

        public MeshJob(long id, Chunk chunk, NeighborSet neighbors, Mesh.MeshDataArray meshData, Action callBack) {
            _id = id;
            _chunk = chunk;
            _neighbors = neighbors;
            _meshData = meshData;
            _callBack = callBack;
            CompletionState = JobCompletionState.None;
        }

        public override void Execute() {
            // Abort the job if the chunk's counter is != the assigned id.
            if (_chunk.JobCounter != _id) {
                Debug.LogWarning($"Aborting orphaned job with ID: {_id}");
                SignalCompletion(JobCompletionState.Aborted);
                return;
            }
            
            // Acquire a read lock on the chunk and generate mesh data.
            if (_chunk.ThreadLock.TryEnterReadLock(GlobalConstants.JOB_LOCK_TIMEOUT_MS)) {
                try {
                    var mesher = VisualMesher.LocalThreadInstance;
                    mesher.GenerateMeshData(_chunk, _neighbors);
                    mesher.ApplyMeshData(ref _meshData);
                }
                finally {
                    _chunk.ThreadLock.ExitReadLock();
                }

                // Signal completion.
                SignalCompletion(JobCompletionState.Completed);

                // Invoke callback on main if specified.
                if (_callBack != null) {
                    GlobalThreadPool.DispatchOnMain(_callBack, QueueType.Throttled);
                }
            }
            else {
                Debug.LogError("Job aborted due to read lock timeout expiration!\n" +
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