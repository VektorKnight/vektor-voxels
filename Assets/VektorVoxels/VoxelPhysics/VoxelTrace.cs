using UnityEditor;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.VoxelPhysics {
    public static class VoxelTrace {
        private static Vector3Int Floor(this Vector3 v) {
            return new Vector3Int(
                Mathf.FloorToInt(v.x),
                Mathf.FloorToInt(v.y),
                Mathf.FloorToInt(v.z)
            );
        }
        
        /// <summary>
        /// Traces a ray through a given voxel grid.
        /// </summary>
        public static bool TraceRay(Ray ray, Chunk chunk, float distance, out VoxelTraceResult result) {
            // Calculate step values.
            var l = ray.direction * distance;
            var start = chunk.WorldToLocal(ray.origin);
            var chunkSize = WorldManager.CHUNK_SIZE;
            
            // Direction along each axis to step (+/-).
            var step = new Vector3Int(
                (int)Mathf.Sign(ray.direction.x),
                (int)Mathf.Sign(ray.direction.y),
                (int)Mathf.Sign(ray.direction.z)
            );
            
            // Size of the step on each axis.
            var stepSize = new Vector3(
                1f / Mathf.Abs(l.x),
                1f / Mathf.Abs(l.y),
                1f / Mathf.Abs(l.z)
            );

            var voxel = start.Floor();
            
            // Make sure we didn't just start in a solid voxel.
            if (VoxelUtility.InLocalGrid(voxel, chunkSize)) {
                var vi = VoxelUtility.VoxelIndex(voxel, chunkSize);
                var voxelData = chunk.VoxelData[vi];
                if (!voxelData.IsNull) {
                    result = new VoxelTraceResult(voxel, chunk.LocalToWorld(voxel), voxelData);
                    return true;
                }
            }
            
            // How far we've traversed on each axis.
            var delta = Vector3.zero;
            delta.x = step.x > 0
                ? ((voxel.x + 1) - start.x) * stepSize.x
                : (start.x - voxel.x) * stepSize.x;
            
            delta.y = step.y > 0
                ? ((voxel.y + 1) - start.y) * stepSize.y
                : (start.y - voxel.y) * stepSize.y;
            
            delta.z = step.z > 0
                ? ((voxel.z + 1) - start.z) * stepSize.z
                : (start.z - voxel.z) * stepSize.z;

            // Loop till we hit a voxel or reach max distance.
            var dSqr = distance * distance;
            while (true) {
                // DDA
                if (delta.x < delta.y) {
                    if (delta.x < delta.z) {
                        voxel.x += step.x;
                        delta.x += stepSize.x;
                    }
                    else {
                        voxel.z += step.z;
                        delta.z += stepSize.z;
                    }
                }
                else {
                    if (delta.y < delta.z) {
                        voxel.y += step.y;
                        delta.y += stepSize.y;
                    }
                    else {
                        voxel.z += step.z;
                        delta.z += stepSize.z;
                    }
                }

                // Done if delta is greater than ray length.
                if (delta.sqrMagnitude > dSqr) {
                    result = default;
                    return false;
                }
                
                // Done if y coordinate is outside of the vertical chunk space.
                if (voxel.y < 0 || voxel.y > chunkSize.y) {
                    result = default;
                    return false;
                }
                
                // Skip out of grid voxels.
                if (!VoxelUtility.InLocalGrid(voxel, chunkSize)) {
                    continue;
                }
                
                // Skip air and no collision voxels.
                var vi = VoxelUtility.VoxelIndex(voxel, chunkSize);
                var voxelData = chunk.VoxelData[vi];
                if (!voxelData.IsNull) {
                    result = new VoxelTraceResult(voxel, chunk.LocalToWorld(voxel), voxelData);
                    return true;
                }
            }
        }
    }
}