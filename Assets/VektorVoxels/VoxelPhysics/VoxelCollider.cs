using System.Collections.Generic;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.VoxelPhysics {
    /// <summary>
    /// Static utility for AABB collision queries against the voxel grid.
    /// Works in world space, handles chunk boundary crossing.
    /// </summary>
    public static class VoxelCollider {
        // Reusable list to avoid allocations
        private static readonly List<Vector3Int> _solidVoxels = new List<Vector3Int>(64);

        /// <summary>
        /// Check if a world position contains a solid voxel.
        /// </summary>
        public static bool IsSolid(Vector3Int worldPos) {
            var world = VoxelWorld.Instance;
            if (world == null) return false;

            var chunkPos = world.WorldToChunkPos(worldPos);
            var chunkId = world.ChunkIdFromPos(chunkPos);

            // Bounds check before array access
            var maxChunks = world.MaxChunks;
            if (chunkId.x < 0 || chunkId.x >= maxChunks.x ||
                chunkId.y < 0 || chunkId.y >= maxChunks.y) {
                return false; // Out of world bounds horizontally = air (open edges)
            }

            var chunk = world.Chunks[chunkId.x, chunkId.y];

            // Chunk must exist and have completed terrain generation
            if (chunk == null || chunk.State < ChunkState.Lighting) {
                return true; // Not ready = solid (prevents falling through unloaded world)
            }

            var localPos = chunk.WorldToVoxel(worldPos);
            var chunkSize = VoxelWorld.CHUNK_SIZE;
            // Note: CHUNK_SIZE is Vector2Int where X = width (X and Z), Y = height

            // Check vertical bounds separately
            if (localPos.y < 0) return true; // Below world = solid floor
            if (localPos.y >= chunkSize.y) return false; // Above world = open sky

            // Check horizontal bounds (X and Z use the same width)
            if (localPos.x < 0 || localPos.x >= chunkSize.x ||
                localPos.z < 0 || localPos.z >= chunkSize.x) {
                return true; // Outside chunk horizontally = solid
            }

            var index = VoxelUtility.VoxelIndex(localPos, chunkSize);
            var voxelData = chunk.VoxelData[index];

            // Null voxels (ID 0) are air - not solid
            if (voxelData.IsNull) return false;

            return !voxelData.HasFlag(VoxelFlags.NoCollision);
        }

        /// <summary>
        /// Get all solid voxel positions within world-space bounds.
        /// </summary>
        public static void GetSolidVoxelsInBounds(Bounds bounds, List<Vector3Int> results) {
            results.Clear();

            var min = new Vector3Int(
                Mathf.FloorToInt(bounds.min.x),
                Mathf.FloorToInt(bounds.min.y),
                Mathf.FloorToInt(bounds.min.z)
            );
            var max = new Vector3Int(
                Mathf.FloorToInt(bounds.max.x),
                Mathf.FloorToInt(bounds.max.y),
                Mathf.FloorToInt(bounds.max.z)
            );

            for (int y = min.y; y <= max.y; y++) {
                for (int z = min.z; z <= max.z; z++) {
                    for (int x = min.x; x <= max.x; x++) {
                        var pos = new Vector3Int(x, y, z);
                        if (IsSolid(pos)) {
                            results.Add(pos);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Perform swept AABB collision against voxel grid.
        /// Resolves collisions axis-by-axis for proper sliding behavior.
        /// </summary>
        /// <param name="bounds">Entity bounds at current position.</param>
        /// <param name="velocity">Desired movement velocity (units per frame).</param>
        /// <returns>Collision result with final position and state.</returns>
        public static CollisionResult SweepAABB(Bounds bounds, Vector3 velocity) {
            var result = new CollisionResult {
                Position = bounds.center,
                Velocity = velocity,
                GroundNormal = Vector3.up
            };

            // Resolve Y axis first (gravity/jumping)
            if (velocity.y != 0) {
                result = ResolveAxis(bounds, result.Velocity, 1, result);
                bounds.center = result.Position;
            }

            // Then X axis
            if (velocity.x != 0) {
                result = ResolveAxis(bounds, result.Velocity, 0, result);
                bounds.center = result.Position;
            }

            // Then Z axis
            if (velocity.z != 0) {
                result = ResolveAxis(bounds, result.Velocity, 2, result);
            }

            result.Hit = result.Grounded || result.HitCeiling || result.HitWall;
            return result;
        }

        private static CollisionResult ResolveAxis(Bounds bounds, Vector3 velocity, int axis, CollisionResult result) {
            var movement = velocity[axis];
            if (Mathf.Approximately(movement, 0f)) return result;

            // Expand bounds in movement direction
            var expandedBounds = bounds;
            var center = bounds.center;
            var size = bounds.size;

            if (movement > 0) {
                center[axis] += movement * 0.5f;
            } else {
                center[axis] += movement * 0.5f;
            }
            size[axis] += Mathf.Abs(movement);
            expandedBounds.center = center;
            expandedBounds.size = size;

            // Find solid voxels in expanded region
            GetSolidVoxelsInBounds(expandedBounds, _solidVoxels);

            if (_solidVoxels.Count == 0) {
                // No collision, apply full movement
                var newPos = result.Position;
                newPos[axis] += movement;
                result.Position = newPos;
                return result;
            }

            // Find closest collision
            float closestDist = Mathf.Abs(movement);
            bool collided = false;

            foreach (var voxelPos in _solidVoxels) {
                var voxelBounds = new Bounds(
                    new Vector3(voxelPos.x + 0.5f, voxelPos.y + 0.5f, voxelPos.z + 0.5f),
                    Vector3.one
                );

                float dist = GetPenetrationDistance(bounds, voxelBounds, axis, movement);
                // Only consider positive distances (voxel is ahead of us, not already overlapping)
                if (dist >= 0 && dist < closestDist) {
                    closestDist = dist;
                    collided = true;
                }
            }

            // Apply movement up to collision point
            var finalPos = result.Position;
            var finalVel = result.Velocity;

            if (collided) {
                // Small epsilon to prevent floating point issues
                const float epsilon = 0.001f;
                finalPos[axis] += Mathf.Sign(movement) * (closestDist - epsilon);
                finalVel[axis] = 0;

                // Set collision flags
                if (axis == 1) {
                    if (movement < 0) {
                        result.Grounded = true;
                        result.GroundNormal = Vector3.up;
                    } else {
                        result.HitCeiling = true;
                    }
                } else {
                    result.HitWall = true;
                }
            } else {
                finalPos[axis] += movement;
            }

            result.Position = finalPos;
            result.Velocity = finalVel;
            return result;
        }

        private static float GetPenetrationDistance(Bounds entity, Bounds voxel, int axis, float movement) {
            if (movement > 0) {
                // Moving positive: distance from entity max to voxel min
                return voxel.min[axis] - entity.max[axis];
            } else {
                // Moving negative: distance from entity min to voxel max
                return entity.min[axis] - voxel.max[axis];
            }
        }
    }
}
