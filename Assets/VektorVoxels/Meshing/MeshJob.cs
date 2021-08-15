﻿using System;
using Unity.Jobs;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Threading;

namespace VektorVoxels.Meshing {
    public readonly struct MeshJob : IPoolJob {
        private readonly long _id;
        private readonly Chunk _chunk;
        private readonly NeighborSet _neighbors;
        private readonly CubicMesher _mesher;
        private readonly Action _callBack;

        public long Id => _id;

        public MeshJob(long id, Chunk chunk, NeighborSet neighbors, CubicMesher mesher, Action callBack) {
            _id = id;
            _chunk = chunk;
            _neighbors = neighbors;
            _mesher = mesher;
            _callBack = callBack;
        }

        public void Execute() {
            // Abort the job if the chunk's counter is != the assigned id.
            if (_chunk.JobCounter != _id) {
                Debug.LogWarning($"Aborting orphaned job with ID: {_id}");
                return;
            }
            
            _chunk.ThreadLock.EnterReadLock();
            _mesher.GenerateMeshData(_chunk, _neighbors);
            
            GlobalThreadPool.QueueOnMain(_callBack);
            _chunk.ThreadLock.ExitReadLock();
        }
    }
}