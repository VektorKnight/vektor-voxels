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
using VektorVoxels.World;

namespace VektorVoxels.Meshing {
    /// <summary>
    /// Worker job for chunk mesh generation. Uses greedy meshing for flat lighting
    /// (better vertex reduction) or standard meshing for smooth lighting (AO requires
    /// per-vertex data). Outputs to a MeshDataArray for efficient GPU upload.
    /// Aborts if chunk job counter has been invalidated.
    /// </summary>
    public class MeshJob : VektorJob {
        private const int MAX_RETRIES = 3;

        private readonly long _id;
        private readonly Chunk _chunk;
        private readonly NeighborSet _neighbors;
        private Mesh.MeshDataArray _meshData;
        private readonly Action _callBack;
        private int _retryCount;

        public MeshJob(long id, Chunk chunk, NeighborSet neighbors, Mesh.MeshDataArray meshData, Action callBack) {
            _id = id;
            _chunk = chunk;
            _neighbors = neighbors;
            _meshData = meshData;
            _callBack = callBack;
            _retryCount = 0;
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
                // Re-check job counter after acquiring lock - a reload could have invalidated us.
                if (_chunk.JobCounter != _id) {
                    _chunk.ThreadLock.ExitReadLock();
                    Debug.Log($"Mesh job {_id} invalidated after lock acquisition, aborting");
                    SignalCompletion(JobCompletionState.Aborted);
                    return;
                }

                try {
                    var mesher = VisualMesher.LocalThreadInstance;

                    // Use greedy meshing for flat lighting (better vertex reduction),
                    // standard meshing for smooth lighting (AO requires per-vertex lighting).
                    if (VoxelWorld.Instance.UseSmoothLighting) {
                        mesher.GenerateMeshData(_chunk, _neighbors);
                    }
                    else {
                        mesher.GenerateMeshDataGreedy(_chunk, _neighbors);
                    }

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
                _retryCount++;
                if (_retryCount <= MAX_RETRIES) {
                    Debug.LogWarning($"Mesh job failed to acquire lock, retry {_retryCount}/{MAX_RETRIES}");
                    GlobalThreadPool.DispatchJob(this);
                    return;
                }

                Debug.LogError($"Mesh job failed after {MAX_RETRIES} retries. Chunk may be in invalid state.");
                SignalCompletion(JobCompletionState.Aborted);
            }
        }
    }
}