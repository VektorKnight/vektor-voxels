using UnityEngine;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Chunks {
    /// <summary>
    /// A voxel update to be processed by a chunk.
    /// </summary>
    public readonly struct VoxelUpdate {
        public readonly int Index;
        public readonly VoxelData Data;

        public VoxelUpdate(int index, VoxelData data) {
            Index = index;
            Data = data;
        }

        public VoxelUpdate(Vector3Int pos, VoxelData data) {
            Index = VoxelUtility.VoxelIndex(in pos, WorldManager.Instance.ChunkSize);
            Data = data;
        }
    }
}