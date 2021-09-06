using UnityEngine;
using VektorVoxels.Voxels;

namespace VektorVoxels.World.Chunks {
    /// <summary>
    /// A voxel update to be processed by a chunk.
    /// </summary>
    public readonly struct VoxelUpdate {
        public readonly Vector3Int Position;
        public readonly VoxelData Data;

        public VoxelUpdate(Vector3Int pos, VoxelData data) {
            Position = pos;
            Data = data;
        }
    }
}