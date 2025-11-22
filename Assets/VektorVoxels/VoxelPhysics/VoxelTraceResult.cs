using UnityEngine;
using VektorVoxels.Voxels;

namespace VektorVoxels.VoxelPhysics {
    public readonly struct VoxelTraceResult {
        public readonly Vector3Int Local;
        public readonly Vector3 World;
        public readonly VoxelData Voxel;
        public readonly Vector3Int Normal;

        public VoxelTraceResult(Vector3Int local, Vector3 world, VoxelData voxel, Vector3Int normal) {
            Local = local;
            World = world;
            Voxel = voxel;
            Normal = normal;
        }
    }
}