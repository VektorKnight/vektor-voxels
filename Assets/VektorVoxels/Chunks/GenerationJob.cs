using System;
using VektorVoxels.Threading;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Chunks {
    public class GenerationJob : IWorkItem {
        private readonly Chunk _chunk;
        private readonly Action _callBack;

        public GenerationJob(Chunk chunk, Action callBack) {
            _chunk = chunk;
            _callBack = callBack;
        }

        public void Execute() {
            _chunk.ThreadLock.EnterWriteLock();
            WorldManager.Instance.Generator.ProcessChunk(_chunk);
            
            /*var dimensions = WorldManager.Instance.ChunkSize;
            for (var z = 0; z < dimensions.x; z++) {
                for (var x = 0; x < dimensions.x; x++) {
                    _chunk.VoxelData[VoxelUtility.VoxelIndex(x, 0, z, dimensions)] = VoxelTable.GetVoxelDefinition(1).GetDataInstance();
                    _chunk.HeightMap[VoxelUtility.HeightIndex(x, z, dimensions.x)] = new HeightData(0, true);
                }
            }

            if (true) {
                _chunk.VoxelData[VoxelUtility.VoxelIndex(4, 1, 6, dimensions)] = VoxelTable.GetVoxelDefinition((uint)(11 + (_chunk.ChunkId.x % 2))).GetDataInstance();
                _chunk.HeightMap[VoxelUtility.HeightIndex(4, 6, dimensions.x)] = new HeightData(1, true);
            }*/
            
            GlobalThreadPool.QueueOnMain(_callBack);
            _chunk.ThreadLock.ExitWriteLock();
        }
    }
}