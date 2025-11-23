using UnityEditor;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.VoxelPhysics {
    /// <summary>
    /// DDA (Digital Differential Analyzer) algorithm for voxel raycasting.
    /// More efficient than PhysX raycasts for voxel grids - walks grid directly,
    /// visiting only intersected cells. O(max_distance) complexity.
    /// </summary>
    public static class VoxelTrace {
        private static Vector3Int Floor(this Vector3 v) {
            return new Vector3Int(
                Mathf.FloorToInt(v.x),
                Mathf.FloorToInt(v.y),
                Mathf.FloorToInt(v.z)
            );
        }
        
        /// <summary>
        /// Traces a ray through a chunk using DDA algorithm.
        /// Returns first non-null voxel hit within distance.
        /// </summary>
        /// <param name="ray">Ray in world space.</param>
        /// <param name="chunk">Chunk to trace within.</param>
        /// <param name="distance">Maximum trace distance in world units.</param>
        /// <param name="result">Hit result with local/world position and voxel data.</param>
        /// <returns>True if voxel hit, false if ray exits chunk without hit.</returns>
        public static bool TraceRay(Ray ray, Chunk chunk, float distance, out VoxelTraceResult result) {
            if (!chunk) {
                result = default;
                return false;
            }
            
            // Calculate step values.
            var l = ray.direction * distance;
            var start = chunk.WorldToLocal(ray.origin);
            var voxel = start.Floor();
            var chunkSize = VoxelWorld.CHUNK_SIZE;

            // Early out for degenerate rays
            if (l.sqrMagnitude < 0.0001f) {
                result = default;
                return false;
            }

            // Step direction per axis: +1, 0, or -1 based on ray direction.
            var step = new Vector3Int(
                (int)Mathf.Sign(ray.direction.x),
                (int)Mathf.Sign(ray.direction.y),
                (int)Mathf.Sign(ray.direction.z)
            );

            // Reciprocal of ray direction: t-parameter increment to cross one grid cell per axis.
            // Use large value instead of infinity for near-zero components to avoid NaN issues.
            const float epsilon = 0.0001f;
            var stepSize = new Vector3(
                Mathf.Abs(l.x) > epsilon ? 1f / Mathf.Abs(l.x) : float.MaxValue,
                Mathf.Abs(l.y) > epsilon ? 1f / Mathf.Abs(l.y) : float.MaxValue,
                Mathf.Abs(l.z) > epsilon ? 1f / Mathf.Abs(l.z) : float.MaxValue
            );

            // Make sure we didn't just start in a solid voxel.
            if (VoxelUtility.InLocalGrid(voxel, chunkSize)) {
                var vi = VoxelUtility.VoxelIndex(voxel, chunkSize);
                var voxelData = chunk.VoxelData[vi];
                if (!voxelData.IsNull) {
                    // Calculate entry normal based on which face we entered through.
                    // Find the closest face considering ray direction.
                    var localPos = start - new Vector3(voxel.x, voxel.y, voxel.z);
                    var entryNormal = Vector3Int.zero;
                    var minDist = float.MaxValue;

                    // Check each axis - only consider faces we could have entered through
                    if (ray.direction.x > 0 && localPos.x < minDist) {
                        minDist = localPos.x;
                        entryNormal = new Vector3Int(-1, 0, 0);
                    }
                    if (ray.direction.x < 0 && (1 - localPos.x) < minDist) {
                        minDist = 1 - localPos.x;
                        entryNormal = new Vector3Int(1, 0, 0);
                    }
                    if (ray.direction.y > 0 && localPos.y < minDist) {
                        minDist = localPos.y;
                        entryNormal = new Vector3Int(0, -1, 0);
                    }
                    if (ray.direction.y < 0 && (1 - localPos.y) < minDist) {
                        minDist = 1 - localPos.y;
                        entryNormal = new Vector3Int(0, 1, 0);
                    }
                    if (ray.direction.z > 0 && localPos.z < minDist) {
                        minDist = localPos.z;
                        entryNormal = new Vector3Int(0, 0, -1);
                    }
                    if (ray.direction.z < 0 && (1 - localPos.z) < minDist) {
                        entryNormal = new Vector3Int(0, 0, 1);
                    }

                    result = new VoxelTraceResult(voxel, chunk.LocalToWorld(voxel), voxelData, entryNormal);
                    return true;
                }
            }

            // Track which axis was last stepped for face normal calculation.
            var normal = Vector3Int.zero;
            
            // Initial t-values: distance to first grid boundary on each axis.
            // Use infinity for zero direction components to exclude them from stepping.
            var delta = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

            if (step.x != 0)
                delta.x = step.x > 0
                    ? ((voxel.x + 1) - start.x) * stepSize.x
                    : (start.x - voxel.x) * stepSize.x;

            if (step.y != 0)
                delta.y = step.y > 0
                    ? ((voxel.y + 1) - start.y) * stepSize.y
                    : (start.y - voxel.y) * stepSize.y;

            if (step.z != 0)
                delta.z = step.z > 0
                    ? ((voxel.z + 1) - start.z) * stepSize.z
                    : (start.z - voxel.z) * stepSize.z;

            // DDA main loop: step along axis with smallest delta (closest boundary).
            // Use <= for consistent tie-breaking priority: X > Y > Z
            while (true) {
                if (delta.x <= delta.y && delta.x <= delta.z) {
                    voxel.x += step.x;
                    delta.x += stepSize.x;
                    normal = new Vector3Int(-step.x, 0, 0);
                }
                else if (delta.y <= delta.z) {
                    voxel.y += step.y;
                    delta.y += stepSize.y;
                    normal = new Vector3Int(0, -step.y, 0);
                }
                else {
                    voxel.z += step.z;
                    delta.z += stepSize.z;
                    normal = new Vector3Int(0, 0, -step.z);
                }

                // Done if t-parameter exceeds 1.0 (past ray endpoint).
                if (Mathf.Min(delta.x, Mathf.Min(delta.y, delta.z)) > 1.0f) {
                    result = default;
                    return false;
                }

                // Done if y coordinate is outside of the vertical chunk space.
                if (voxel.y < 0 || voxel.y >= chunkSize.y) {
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
                    result = new VoxelTraceResult(voxel, chunk.LocalToWorld(voxel), voxelData, normal);
                    return true;
                }
            }
        }
    }
}