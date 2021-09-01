using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Meshing;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Lighting {
    /// <summary>
    /// Performs lightmapping on chunks.
    /// </summary>
    public sealed class LightMapper {
        // Static thread-local instance for the job system.
        private static readonly ThreadLocal<LightMapper> _threadLocal = new ThreadLocal<LightMapper>(() => new LightMapper());
        public static LightMapper LocalThreadInstance => _threadLocal.Value;

        public static readonly Vector3Int[] _sunNeighbors = {
            Vector3Int.forward, 
            Vector3Int.right, 
            Vector3Int.back, 
            Vector3Int.left
        };
        
        private readonly Stack<LightNode> _blockNodes;
        private readonly Stack<LightNode> _sunNodes;

        public LightMapper() {
            _blockNodes = new Stack<LightNode>();
            _sunNodes = new Stack<LightNode>();
        }
        
        private void AcquireNeighborLocks(in NeighborSet neighbors) {
            if ((neighbors.Flags & NeighborFlags.North) != 0) {
                neighbors.North.ThreadLock.EnterReadLock();
            }
            
            if ((neighbors.Flags & NeighborFlags.East) != 0) {
                neighbors.East.ThreadLock.EnterReadLock();
            }
            
            if ((neighbors.Flags & NeighborFlags.South) != 0) {
                neighbors.South.ThreadLock.EnterReadLock();
            }
            
            if ((neighbors.Flags & NeighborFlags.West) != 0) {
                neighbors.West.ThreadLock.EnterReadLock();
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
        
        private void PropagateBoundarySun(in Chunk home, in Chunk neighbor, Vector2Int hp, Vector2Int np, Vector2Int d) {
            // Iterate column starting at the heightmap value.
            var neighborHeight = neighbor.HeightMap[VoxelUtility.HeightIndex(np.x, np.y, d.x)];
            for (var y = neighborHeight.Value + 1; y >= 0; y--) {
                var vpi = VoxelUtility.VoxelIndex(hp.x, y, hp.y, d);
                var voxel = home.VoxelData[vpi];
                    
                // Skip if voxel at boundary is opaque.
                if (voxel.Id != 0 && !voxel.HasFlag(VoxelFlags.AlphaRender)) {
                    continue;
                }
                    
                // Grab home and neighbor light values.
                var homeLight = home.SunLight[vpi];
                var northLight = neighbor.SunLight[VoxelUtility.VoxelIndex(np.x, y, np.y, d)];
                    
                northLight.Decompose(out var nlr, out var nlg, out var nlb, out _);
                    
                // Skip if neighbor light is <=1 on all channels.
                if (nlr <= 1 && nlg <= 1 && nlb <= 1) {
                    continue;
                }
                    
                homeLight.Decompose(out var hlr, out var hlg, out var hlb, out _);
                    
                // Skip if home light is greater or equal on all channels.
                if (hlr >= nlr && hlg >= nlg && hlb >= nlb) {
                    continue;
                }
                    
                // Place a propagation node with the decremented neighbor values.
                _sunNodes.Push(new LightNode(new Vector3Int(hp.x, y, hp.y), new Color16(nlr - 1, nlg - 1, nlb - 1, 0)));
            }
        }
        
        private void PropagateBoundaryBlock(in Chunk home, in Chunk neighbor, Vector2Int hp, Vector2Int np, Vector2Int d) {
            // Iterate column starting at the top.
            for (var y = d.y - 1; y >= 0; y--) {
                var vpi = VoxelUtility.VoxelIndex(hp.x, y, hp.y, d);
                var voxel = home.VoxelData[vpi];
                    
                // Skip if voxel at boundary is opaque.
                if (voxel.Id != 0 && !voxel.HasFlag(VoxelFlags.AlphaRender)) {
                    continue;
                }
                    
                // Grab home and neighbor light values.
                var homeLight = home.BlockLight[vpi];
                var northLight = neighbor.BlockLight[VoxelUtility.VoxelIndex(np.x, y, np.y, d)];
                    
                northLight.Decompose(out var nlr, out var nlg, out var nlb, out _);
                    
                // Skip if neighbor light is <=1 on all channels.
                if (nlr <= 1 && nlg <= 1 && nlb <= 1) {
                    continue;
                }
                    
                homeLight.Decompose(out var hlr, out var hlg, out var hlb, out _);
                    
                // Skip if home light is greater or equal on all channels.
                if (hlr >= nlr && hlg >= nlg && hlb >= nlb) {
                    continue;
                }
                
                // Decrement and clamp neighbor light.
                var dr = Mathf.Clamp(nlr - 1, 0, 15);
                var dg = Mathf.Clamp(nlg - 1, 0, 15);
                var db = Mathf.Clamp(nlb - 1, 0, 15);
                    
                // Place a propagation node with the decremented neighbor values.
                _blockNodes.Push(new LightNode(new Vector3Int(hp.x, y, hp.y), new Color16(dr, dg, db, 0)));
            }
        }
        
        /// <summary>
        /// Performs light propagation on a given voxel data set and lightmap.
        /// </summary>
        private void PropagateLightNodes(in VoxelData[] voxelData, in Color16[] lightMap, Vector2Int d, bool sun) {
            var stack = sun ? _sunNodes : _blockNodes;
            while (stack.Count > 0) {
                // Grab node and sample lightmap at the node position.
                var node = stack.Pop();
                var cpi = VoxelUtility.VoxelIndex(node.Position, d);
                var current = lightMap[cpi];

                // Write max of the current light and node values.
                current = Color16.Max(current, node.Value);
                lightMap[cpi] = current;
                
                // Decompose the light here to avoid unnecessary bit ops till the packed value is needed.
                node.Value.Decompose(out var nr, out var ng, out var nb, out _);
                
                // Done propagating block light if the node value is <=1 on all channels.
                if (nr <= 1 && ng <= 1 && nb <= 1) {
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
                    
                    // Decrement light on propagation unless the light type is sun and we're propagating down.
                    var dr = nr - 1;
                    var dg = ng - 1;
                    var db = nb - 1;
                    
                    // Decompose neighbor attenuation and subtract from decremented node values.
                    neighbor.ColorData.Decompose(out var nar, out var nag, out var nab, out _);
                    var ar = Mathf.Clamp(dr - nar, 0, 15);
                    var ag = Mathf.Clamp(dg - nag, 0, 15);
                    var ab = Mathf.Clamp(db - nab, 0, 15);

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
                    stack.Push(new LightNode(np, new Color16(ar, ag, ab, 0)));
                }
            }
        }

        /// <summary>
        /// Scans the voxel grid and sets any light values above the heightmap to Color16.White().
        /// propagation nodes are then placed wherever the heightmap value borders open air.
        /// </summary>
        public void InitializeSunLightFirstPass(in Chunk chunk) {
            var voxelData = chunk.VoxelData;
            var heightMap = chunk.HeightMap;
            var sunLight = chunk.SunLight;
            var d = WorldManager.CHUNK_SIZE;
            
            _sunNodes.Clear();
            var vp = Vector3Int.zero;
            for (var z = 0; z < d.x; z++) {
                for (var x = 0; x < d.x; x++) {
                    // Grab current height data.
                    ref var heightData = ref heightMap[VoxelUtility.HeightIndex(x, z, d.x)];
                    
                    // Determine max of the current height and its 4 immediate neighbors.
                    // This prevents unnecessary propagation checks.
                    var regionMax = (int)heightData.Value;
                    for (var nz = -1; nz <= 1; nz++) {
                        for (var nx = -1; nx <= 1; nx++) {
                            // Skip corners and center.
                            if (nx != 0 && nz != 0 || nx == 0 && nz == 0) {
                                continue;
                            }
                            
                            // Skip out of bounds cells.
                            if (!VoxelUtility.InLocalRect(x + nx, z + nz, d.x)) {
                                continue;
                            }
                            
                            // Get neighbor height and update max.
                            var nhi = VoxelUtility.HeightIndex(x + nx, z + nz, d.x);
                            regionMax = Mathf.Max(regionMax, heightMap[nhi].Value);
                        }
                    }

                    // Iterate the entire column.
                    for (var y = d.y - 1; y >= 0; y--) {
                        // Grab current voxel.
                        vp.x = x; vp.y = y; vp.z = z;
                        var vpi = VoxelUtility.VoxelIndex(x, y, z, d);
                        
                        // Values above the map get set to full sunlight.
                        // Values below are set to none or zero.
                        if (y > heightData.Value) {
                            sunLight[vpi] = new Color16(15, 15, 15, 0);
                        }
                        else {
                            sunLight[vpi] = Color16.Clear();
                        }

                        // Skip propagation on values above the region maximum or below the heightmap.
                        if (y > regionMax || y <= heightData.Value) continue;

                        // Check neighbors for null/non-opaque blocks below the heightmap.
                        // Propagation nodes will be queued for these locations.
                        for (var i = 0; i < 4; i++) {
                            var np = _sunNeighbors[i] + vp;
                            var npi = VoxelUtility.VoxelIndex(np, d);
                            
                            // Skip out of bounds positions.
                            if (!VoxelUtility.InLocalGrid(np, d)) {
                                continue;
                            }
                            
                            // Grab height value of neighbor position.
                            var neighbor = voxelData[npi];
                            var neighborHeight = heightMap[VoxelUtility.HeightIndex(np.x, np.z, d.x)].Value;

                            // Node will be placed if the neighbor voxel is below the height value at that position and null or translucent.
                            if (np.y < neighborHeight && (neighbor.IsNull || neighbor.HasFlag(VoxelFlags.AlphaRender))) {
                                // Place a node if any neighbor is below the heightmap and null/translucent.
                                _sunNodes.Push(new LightNode(vp, new Color16(15, 15, 15, 0)));

                                // Done if any neighbor leads to a cavern.
                                break;
                            }
                        }
                    }
                    
                    // Clear dirty flag.
                    heightData.Dirty = false;
                }
            }
        }

        /// <summary>
        /// Initializes nodes for any light spilling over from neighbors.
        /// All neighbors should be loaded and have completed the first lighting pass before calling this.
        /// </summary>
        public void InitializeNeighborLightPass(in Chunk chunk, NeighborSet neighbors) {
            AcquireNeighborLocks(in neighbors);
            
            _sunNodes.Clear();
            _blockNodes.Clear();
            var d = WorldManager.CHUNK_SIZE;
            
            // process columns along each neighbor boundary.
            for (var i = 0; i < d.x; i++) {
                // Northern boundary.
                if ((neighbors.Flags & NeighborFlags.North) != 0) {
                    PropagateBoundarySun(chunk, neighbors.North, new Vector2Int(i, d.x - 1), new Vector2Int(i, 0), d);
                    PropagateBoundaryBlock(chunk, neighbors.North, new Vector2Int(i, d.x - 1), new Vector2Int(i, 0), d);
                }

                // Eastern boundary.
                if ((neighbors.Flags & NeighborFlags.East) != 0) {
                    PropagateBoundarySun(chunk, neighbors.East, new Vector2Int(d.x - 1, i), new Vector2Int(0, i), d);
                    PropagateBoundaryBlock(chunk, neighbors.East, new Vector2Int(d.x - 1, i), new Vector2Int(0, i), d);
                }

                // Southern boundary.
                if ((neighbors.Flags & NeighborFlags.South) != 0) {
                    PropagateBoundarySun(chunk, neighbors.South, new Vector2Int(i, 0), new Vector2Int(i, d.x - 1), d);
                    PropagateBoundaryBlock(chunk, neighbors.South, new Vector2Int(i, 0), new Vector2Int(i, d.x - 1), d);
                }

                // Western boundary.
                if ((neighbors.Flags & NeighborFlags.West) != 0) {
                    PropagateBoundarySun(chunk, neighbors.West, new Vector2Int(0, i), new Vector2Int(d.x - 1, i), d);
                    PropagateBoundaryBlock(chunk, neighbors.West, new Vector2Int(0, i), new Vector2Int(d.x - 1, i), d);
                }
            }

            ReleaseNeighborLocks(in neighbors);
        }

        /// <summary>
        /// Scans the voxel grid and generates initial nodes for any light source voxels.
        /// </summary>
        public void InitializeBlockLightFirstPass(in Chunk chunk) {
            var voxelData = chunk.VoxelData;
            var d = WorldManager.CHUNK_SIZE;
            _blockNodes.Clear();
            for (var y = 0; y < d.y; y++) {
                for (var z = 0; z < d.x; z++) {
                    for (var x = 0; x < d.x; x++) {
                        var voxel = voxelData[VoxelUtility.VoxelIndex(x, y, z, d)];
                        chunk.BlockLight[VoxelUtility.VoxelIndex(x, y, z, d)] = Color16.Clear();
                        if ((voxel.Flags & VoxelFlags.LightSource) == 0) continue;
                
                        _blockNodes.Push(new LightNode(new Vector3Int(x, y, z), voxel.ColorData));
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
            var d = WorldManager.CHUNK_SIZE;
            PropagateLightNodes(voxelData, sunLight, d, true);
        }
        
        /// <summary>
        /// Propagates any block light nodes.
        /// </summary>
        public void PropagateBlockLight(in Chunk chunk) {
            var voxelData = chunk.VoxelData;
            var blockLight = chunk.BlockLight;
            var d = WorldManager.CHUNK_SIZE;
            PropagateLightNodes(voxelData, blockLight, d, false);
        }
    }
}