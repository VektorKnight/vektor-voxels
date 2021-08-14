using System;
using Unity.Jobs;
using VektorVoxels.Chunks;
using VektorVoxels.Threading;

namespace VektorVoxels.Meshing {
    public class MeshJob : IWorkItem {
        private readonly Chunk _chunk;
        private NeighborSet _neighbors;
        private readonly CubicMesher _mesher;
        private readonly Action _callBack;

        public NeighborSet Neighbors {
            get => _neighbors;
            set => _neighbors = value;
        }

        public MeshJob(Chunk chunk, NeighborSet neighbors, CubicMesher mesher, Action callBack) {
            _chunk = chunk;
            _neighbors = neighbors;
            _mesher = mesher;
            _callBack = callBack;
        }

        public void Execute() {
            _chunk.ThreadLock.EnterReadLock();
            _mesher.GenerateMeshData(_chunk, _neighbors);
            
            GlobalThreadPool.QueueOnMain(_callBack);
            _chunk.ThreadLock.ExitReadLock();
        }
    }
}