using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Meshing;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Lighting {
    /// <summary>
    /// Manages light propagation through voxel chunks using BFS flood-fill.
    /// Processes sunlight (top-down) and block light (point sources) separately.
    /// Uses 8-bit color channels (0-255 range) with per-voxel attenuation.
    /// Thread-local instances accessed via LocalThreadInstance for job system.
    /// Light decrements by 17 per voxel (255/15) to maintain similar propagation distance as 4-bit.
    /// </summary>
    public sealed class LightMapper {
        // Constants for light propagation.
        // Light decrements by 17 per voxel (255/15) to maintain similar propagation distance as 4-bit.
        private const int LIGHT_DECREMENT = 17;
        private const int LIGHT_THRESHOLD = 16;

        // Estimated stack capacity: chunk is 16x256x16, typical propagation hits ~10% of voxels.
        private const int ESTIMATED_NODE_CAPACITY = 6000;

        // Static thread-local instance for the job system.
        private static readonly ThreadLocal<LightMapper> _threadLocal = new ThreadLocal<LightMapper>(() => new LightMapper());
        public static LightMapper LocalThreadInstance => _threadLocal.Value;

        // Cardinal neighbors for horizontal sun propagation at chunk boundaries.
        public static readonly Vector3Int[] _sunNeighbors = {
            Vector3Int.forward,
            Vector3Int.right,
            Vector3Int.back,
            Vector3Int.left
        };

        private readonly Stack<LightNode> _blockNodes;
        private readonly Stack<LightNode> _sunNodes;

        public LightMapper() {
            _blockNodes = new Stack<LightNode>(ESTIMATED_NODE_CAPACITY);
            _sunNodes = new Stack<LightNode>(ESTIMATED_NODE_CAPACITY);
        }
        
        // Acquires read locks on neighbors to prevent concurrent chunk modifications during boundary propagation.
        // Returns true if all locks acquired, false otherwise. Releases any acquired locks on failure.
        private bool AcquireNeighborLocks(in NeighborSet neighbors) {
            const int timeout = Config.GlobalConstants.JOB_LOCK_TIMEOUT_MS;
            bool northLocked = false, eastLocked = false, southLocked = false, westLocked = false;

            try {
                if ((neighbors.Flags & NeighborFlags.North) != 0) {
                    if (!neighbors.North.ThreadLock.TryEnterReadLock(timeout)) {
                        Debug.LogWarning("Failed to acquire North neighbor lock within timeout");
                        return false;
                    }
                    northLocked = true;
                }

                if ((neighbors.Flags & NeighborFlags.East) != 0) {
                    if (!neighbors.East.ThreadLock.TryEnterReadLock(timeout)) {
                        Debug.LogWarning("Failed to acquire East neighbor lock within timeout");
                        return false;
                    }
                    eastLocked = true;
                }

                if ((neighbors.Flags & NeighborFlags.South) != 0) {
                    if (!neighbors.South.ThreadLock.TryEnterReadLock(timeout)) {
                        Debug.LogWarning("Failed to acquire South neighbor lock within timeout");
                        return false;
                    }
                    southLocked = true;
                }

                if ((neighbors.Flags & NeighborFlags.West) != 0) {
                    if (!neighbors.West.ThreadLock.TryEnterReadLock(timeout)) {
                        Debug.LogWarning("Failed to acquire West neighbor lock within timeout");
                        return false;
                    }
                    westLocked = true;
                }

                return true;
            }
            finally {

                // Check if we need to clean up (returning false means we failed partway through)
                bool allAcquired =
                    ((neighbors.Flags & NeighborFlags.North) == 0 || northLocked) &&
                    ((neighbors.Flags & NeighborFlags.East) == 0 || eastLocked) &&
                    ((neighbors.Flags & NeighborFlags.South) == 0 || southLocked) &&
                    ((neighbors.Flags & NeighborFlags.West) == 0 || westLocked);

                if (!allAcquired) {
                    // Release what we acquired
                    if (northLocked) neighbors.North.ThreadLock.ExitReadLock();
                    if (eastLocked) neighbors.East.ThreadLock.ExitReadLock();
                    if (southLocked) neighbors.South.ThreadLock.ExitReadLock();
                    if (westLocked) neighbors.West.ThreadLock.ExitReadLock();
                }
            }
        }

        private void ReleaseNeighborLocks(in NeighborSet neighbors) {
            if ((neighbors.Flags & NeighborFlags.North) != 0) {
                neighbors.North.ThreadLock.ExitReadLock();
            }
            
            if ((neighbors.Flags & NeighborFlags.East) != 0) {
                neighbors.East.ThreadLock.ExitReadLock();
            }
            
            if ((neighbors.Flags & NeighborFlags.South) != 0) {
                neighbors.South.ThreadLock.ExitReadLock();
            }
            
            if ((neighbors.Flags & NeighborFlags.West) != 0) {
                neighbors.West.ThreadLock.ExitReadLock();
            }
        }
        
        // Propagates light from neighbor chunk across boundary.
        // Uses inline parameters to avoid Vector2Int heap allocations.
        // When isSunLight is true, iterates from heightmap down; otherwise iterates full column.
        private void PropagateBoundaryLight(
            in Chunk home, in Chunk neighbor,
            int hpX, int hpZ, int npX, int npZ, Vector2Int d,
            LightColor[] homeLightMap, LightColor[] neighborLightMap,
            Stack<LightNode> nodeStack, bool isSunLight)
        {
            // Determine starting Y: sunlight starts from heightmap, block light from top.
            int startY;
            if (isSunLight) {
                var neighborHeight = neighbor.HeightMap[VoxelUtility.HeightIndex(npX, npZ, d.x)];
                startY = neighborHeight.Value + 1;
            }
            else {
                startY = d.y - 1;
            }

            for (var y = startY; y >= 0; y--) {
                var vpi = VoxelUtility.VoxelIndex(hpX, y, hpZ, d);
                var voxel = home.VoxelData[vpi];

                // Skip if voxel at boundary is opaque.
                if (voxel.Id != 0 && !voxel.HasFlag(VoxelFlags.AlphaRender)) {
                    continue;
                }

                // Grab home and neighbor light values.
                var homeLight = homeLightMap[vpi];
                var neighborLight = neighborLightMap[VoxelUtility.VoxelIndex(npX, y, npZ, d)];

                neighborLight.Decompose(out var nlr, out var nlg, out var nlb, out _);

                // Skip if neighbor light is below threshold on all channels.
                if (nlr <= LIGHT_THRESHOLD && nlg <= LIGHT_THRESHOLD && nlb <= LIGHT_THRESHOLD) {
                    continue;
                }

                homeLight.Decompose(out var hlr, out var hlg, out var hlb, out _);

                // Skip if home light is greater or equal on all channels.
                if (hlr >= nlr && hlg >= nlg && hlb >= nlb) {
                    continue;
                }

                // Decrement neighbor light values (inline math instead of Mathf).
                var dr = nlr > LIGHT_DECREMENT ? nlr - LIGHT_DECREMENT : 0;
                var dg = nlg > LIGHT_DECREMENT ? nlg - LIGHT_DECREMENT : 0;
                var db = nlb > LIGHT_DECREMENT ? nlb - LIGHT_DECREMENT : 0;

                // Place a propagation node with the decremented neighbor values.
                nodeStack.Push(new LightNode(new Vector3Int(hpX, y, hpZ), new LightColor(dr, dg, db, 0)));
            }
        }
        
        /// <summary>
        /// BFS flood-fill light propagation. Decrements intensity per voxel,
        /// applies voxel attenuation from ColorData, and stops when all channels below threshold.
        /// Prevents backwards propagation by rejecting nodes with inferior light values.
        /// </summary>
        private void PropagateLightNodes(in VoxelData[] voxelData, in LightColor[] lightMap, Vector2Int d, bool sun) {
            var stack = sun ? _sunNodes : _blockNodes;
            while (stack.Count > 0) {
                // Grab node and sample lightmap at the node position.
                var node = stack.Pop();
                var cpi = VoxelUtility.VoxelIndex(node.Position, d);
                var current = lightMap[cpi];

                // Write max of the current light and node values.
                current = LightColor.Max(current, node.Value);
                lightMap[cpi] = current;

                // Decompose the light here to avoid unnecessary bit ops till the packed value is needed.
                node.Value.Decompose(out var nr, out var ng, out var nb, out _);

                // Done propagating if the node value is below threshold on all channels.
                if (nr <= LIGHT_THRESHOLD && ng <= LIGHT_THRESHOLD && nb <= LIGHT_THRESHOLD) {
                    continue;
                }

                // Process each neighbor.
                for (var i = 0; i < 6; i++) {
                    // Grab neighbor voxel and light depending on locality.
                    // Voxels outside the current grid will be skipped.
                    var np = MeshTables.VoxelNeighbors[i] + node.Position;
                    var npi = VoxelUtility.VoxelIndex(np, d);

                    if (!VoxelUtility.InLocalGrid(np, d)) {
                        continue;
                    }

                    var neighbor = voxelData[npi];
                    var neighborLight = lightMap[npi];

                    // Skip if neighbor is opaque.
                    if (neighbor.Id != 0 && (neighbor.Flags & VoxelFlags.AlphaRender) == 0) {
                        continue;
                    }

                    // Decrement light on propagation.
                    var dr = nr - LIGHT_DECREMENT;
                    var dg = ng - LIGHT_DECREMENT;
                    var db = nb - LIGHT_DECREMENT;

                    // Decompose neighbor attenuation and subtract from decremented node values.
                    // Scale 4-bit attenuation to 8-bit by multiplying by LIGHT_DECREMENT.
                    neighbor.ColorData.Decompose(out var nar, out var nag, out var nab, out _);
                    var attenR = nar * LIGHT_DECREMENT;
                    var attenG = nag * LIGHT_DECREMENT;
                    var attenB = nab * LIGHT_DECREMENT;

                    // Clamp using inline math (faster than Mathf.Clamp).
                    var ar = dr - attenR;
                    var ag = dg - attenG;
                    var ab = db - attenB;
                    if (ar < 0) ar = 0; else if (ar > 255) ar = 255;
                    if (ag < 0) ag = 0; else if (ag > 255) ag = 255;
                    if (ab < 0) ab = 0; else if (ab > 255) ab = 255;

                    // Skip if attenuated light is zero.
                    if (ar + ag + ab == 0) {
                        continue;
                    }

                    // Skip if the existing light value is >= on all channels.
                    // Prevents propagating backwards or through the influence of a superior light source.
                    neighborLight.Decompose(out var nlr, out var nlg, out var nlb, out _);
                    if (nlr >= ar && nlg >= ag && nlb >= ab) {
                        continue;
                    }

                    // Push a new node to the stack.
                    stack.Push(new LightNode(np, new LightColor(ar, ag, ab, 0)));
                }
            }
        }

        /// <summary>
        /// Scans the voxel grid and sets any light values above the heightmap to full intensity (255, 255, 255).
        /// Propagation nodes are placed at cavern openings where sunlight can spill into underground areas.
        /// </summary>
        public void InitializeSunLightFirstPass(in Chunk chunk) {
            var heightMap = chunk.HeightMap;
            var sunLight = chunk.SunLight;
            var d = VoxelWorld.CHUNK_SIZE;

            _sunNodes.Clear();

            // Clear all light in one call - much faster than individual writes.
            System.Array.Clear(sunLight, 0, sunLight.Length);

            var fullLight = new LightColor(255, 255, 255, 0);

            // For each column, set sunlight above heightmap and find cavern entry points.
            for (var z = 0; z < d.x; z++) {
                for (var x = 0; x < d.x; x++) {
                    var hi = VoxelUtility.HeightIndex(x, z, d.x);
                    ref var heightData = ref heightMap[hi];
                    var height = heightData.Value;
                    heightData.Dirty = false;

                    // Find max height among cardinal neighbors for regionMax optimization.
                    var regionMax = (int)height;
                    for (var i = 0; i < 4; i++) {
                        var nx = x + _sunNeighbors[i].x;
                        var nz = z + _sunNeighbors[i].z;
                        if (nx < 0 || nx >= d.x || nz < 0 || nz >= d.x) continue;
                        var nh = heightMap[VoxelUtility.HeightIndex(nx, nz, d.x)].Value;
                        if (nh > regionMax) regionMax = nh;
                    }

                    // Set full sunlight for all voxels above heightmap.
                    for (var y = height + 1; y < d.y; y++) {
                        sunLight[VoxelUtility.VoxelIndex(x, y, z, d)] = fullLight;
                    }

                    // Skip if column is at world top (no air above).
                    if (height >= d.y - 1) continue;

                    // Check all Y levels from our surface up to regionMax for cavern openings.
                    // This catches both vertical shafts and horizontal tunnel entrances.
                    for (var y = height + 1; y <= regionMax; y++) {
                        // Check 4 cardinal neighbors at this Y level.
                        for (var i = 0; i < 4; i++) {
                            var nx = x + _sunNeighbors[i].x;
                            var nz = z + _sunNeighbors[i].z;

                            // Skip out of bounds.
                            if (nx < 0 || nx >= d.x || nz < 0 || nz >= d.x) continue;

                            var neighborHeight = heightMap[VoxelUtility.HeightIndex(nx, nz, d.x)].Value;

                            // If this Y is below the neighbor's surface, there's a cavern opening.
                            // Place a propagation node - it will flood-fill into the neighbor's underground.
                            if (y < neighborHeight) {
                                _sunNodes.Push(new LightNode(new Vector3Int(x, y, z), fullLight));
                                break; // Only need one node per Y level
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Initializes nodes for any light spilling over from neighbors.
        /// All neighbors should be loaded and have completed the first lighting pass before calling this.
        /// Returns false if neighbor locks could not be acquired.
        /// </summary>
        public bool InitializeNeighborLightPass(in Chunk chunk, NeighborSet neighbors) {
            if (!AcquireNeighborLocks(in neighbors)) {
                return false;
            }

            _sunNodes.Clear();
            _blockNodes.Clear();
            var d = VoxelWorld.CHUNK_SIZE;
            var lastIdx = d.x - 1;

            // Cache neighbor flags for faster checks.
            var hasNorth = (neighbors.Flags & NeighborFlags.North) != 0;
            var hasEast = (neighbors.Flags & NeighborFlags.East) != 0;
            var hasSouth = (neighbors.Flags & NeighborFlags.South) != 0;
            var hasWest = (neighbors.Flags & NeighborFlags.West) != 0;

            // Process columns along each neighbor boundary.
            for (var i = 0; i < d.x; i++) {
                // Northern boundary (home Z = last, neighbor Z = 0).
                if (hasNorth) {
                    PropagateBoundaryLight(chunk, neighbors.North, i, lastIdx, i, 0, d,
                        chunk.SunLight, neighbors.North.SunLight, _sunNodes, true);
                    PropagateBoundaryLight(chunk, neighbors.North, i, lastIdx, i, 0, d,
                        chunk.BlockLight, neighbors.North.BlockLight, _blockNodes, false);
                }

                // Eastern boundary (home X = last, neighbor X = 0).
                if (hasEast) {
                    PropagateBoundaryLight(chunk, neighbors.East, lastIdx, i, 0, i, d,
                        chunk.SunLight, neighbors.East.SunLight, _sunNodes, true);
                    PropagateBoundaryLight(chunk, neighbors.East, lastIdx, i, 0, i, d,
                        chunk.BlockLight, neighbors.East.BlockLight, _blockNodes, false);
                }

                // Southern boundary (home Z = 0, neighbor Z = last).
                if (hasSouth) {
                    PropagateBoundaryLight(chunk, neighbors.South, i, 0, i, lastIdx, d,
                        chunk.SunLight, neighbors.South.SunLight, _sunNodes, true);
                    PropagateBoundaryLight(chunk, neighbors.South, i, 0, i, lastIdx, d,
                        chunk.BlockLight, neighbors.South.BlockLight, _blockNodes, false);
                }

                // Western boundary (home X = 0, neighbor X = last).
                if (hasWest) {
                    PropagateBoundaryLight(chunk, neighbors.West, 0, i, lastIdx, i, d,
                        chunk.SunLight, neighbors.West.SunLight, _sunNodes, true);
                    PropagateBoundaryLight(chunk, neighbors.West, 0, i, lastIdx, i, d,
                        chunk.BlockLight, neighbors.West.BlockLight, _blockNodes, false);
                }
            }

            ReleaseNeighborLocks(in neighbors);
            return true;
        }

        /// <summary>
        /// Scans the voxel grid and generates initial nodes for any light source voxels.
        /// </summary>
        public void InitializeBlockLightFirstPass(in Chunk chunk) {
            var voxelData = chunk.VoxelData;
            var blockLight = chunk.BlockLight;
            var d = VoxelWorld.CHUNK_SIZE;
            _blockNodes.Clear();

            // Loop order Z → Y → X for cache-friendly sequential memory access.
            // VoxelIndex = x + width * (y + height * z), so X should be innermost.
            for (var z = 0; z < d.x; z++) {
                for (var y = 0; y < d.y; y++) {
                    for (var x = 0; x < d.x; x++) {
                        var idx = VoxelUtility.VoxelIndex(x, y, z, d);
                        var voxel = voxelData[idx];
                        blockLight[idx] = LightColor.Clear();

                        if ((voxel.Flags & VoxelFlags.LightSource) == 0) continue;

                        // Convert 4-bit ColorData to 8-bit LightColor.
                        var lightColor = LightColor.FromColor16(voxel.ColorData);
                        _blockNodes.Push(new LightNode(new Vector3Int(x, y, z), lightColor));
                    }
                }
            }
        }
        
        /// <summary>
        /// Propagates any sunlight nodes.
        /// </summary>
        public void PropagateSunLight(in Chunk chunk) {
            var voxelData = chunk.VoxelData;
            var sunLight = chunk.SunLight;
            var d = VoxelWorld.CHUNK_SIZE;
            PropagateLightNodes(voxelData, sunLight, d, true);
        }
        
        /// <summary>
        /// Propagates any block light nodes.
        /// </summary>
        public void PropagateBlockLight(in Chunk chunk) {
            var voxelData = chunk.VoxelData;
            var blockLight = chunk.BlockLight;
            var d = VoxelWorld.CHUNK_SIZE;
            PropagateLightNodes(voxelData, blockLight, d, false);
        }
    }
}