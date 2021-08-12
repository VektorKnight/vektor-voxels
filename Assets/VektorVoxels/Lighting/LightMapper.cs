using System.Collections.Generic;
using UnityEngine;
using VektorVoxels.Meshing;
using VektorVoxels.Voxels;

namespace VektorVoxels.Lighting {
    /// <summary>
    /// Performs lightmapping for a voxel grid.
    /// </summary>
    public sealed class LightMapper {
        private readonly Stack<LightNode> _blockNodes;
        private readonly Stack<LightNode> _sunNodes;

        public LightMapper() {
            _blockNodes = new Stack<LightNode>();
            _sunNodes = new Stack<LightNode>();
        }

        private static readonly Vector3Int[] _sunNeighbors = {
            Vector3Int.forward, 
            Vector3Int.right, 
            Vector3Int.back, 
            Vector3Int.left
        };

        /// <summary>
        /// Scans the voxel grid and sets any light values above the heightmap to Color16.White().
        /// propagation nodes are then placed wherever the heightmap value borders open air.
        /// </summary>
        public void InitializeSunLightFirstPass(in VoxelData[] voxelData, in byte[] heightMap, in Color16[] sunLight, Vector3Int d) {
            var vp = Vector3Int.zero;
            for (var y = 0; y < d.y; y++) {
                for (var z = 0; z < d.z; z++) {
                    for (var x = 0; x < d.x; x++) {
                        // Grab current voxel and heightmap value.
                        vp.x = x; vp.y = y; vp.z = z;
                        var vpi = VoxelUtility.VoxelIndex(x, y, z, in d);
                        var mapHeight = heightMap[VoxelUtility.HeightIndex(x, z, d.x)];
                        
                        // Set any values below the heightmap to zero and continue.
                        if (y < mapHeight) {
                            sunLight[vpi] = Color16.Clear();
                            continue;
                        }
                        
                        // Set all values above the heightmap to full white.
                        sunLight[vpi] = new Color16(15, 15, 15, 0);
                        
                        // Only scan neighbors when at the heightmap value.
                        //if (y != mapHeight) continue;
                        
                        // Check neighbors for null/translucent blocks below the heightmap (caverns).
                        // Propagation nodes will be queued for these locations.
                        for (var i = 0; i < 4; i++) {
                            var np = _sunNeighbors[i] + vp;
                            var npi = VoxelUtility.VoxelIndex(in np, in d);
                            
                            // Skip out of bounds positions.
                            if (!VoxelUtility.InLocalGrid(in np, in d)) {
                                continue;
                            }
                            
                            // Grab height value of neighbor position.
                            ref readonly var neighbor = ref voxelData[npi];
                            var neighborHeight = heightMap[VoxelUtility.HeightIndex(np.x, np.z, d.x)];
                            
                            // Cavern exists if the neighbor voxel is below the height value at that position and null or translucent.
                            if (np.y < neighborHeight && (neighbor.Id == 0 || (neighbor.Flags & VoxelFlags.AlphaRender) != 0)) {
                                // Place a node if any neighbor is below the heightmap and null/translucent.
                                _sunNodes.Push(new LightNode(vp, new Color16(15, 15, 15, 0)));

                                // Done if any neighbor leads to a cavern.
                                break;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Scans the voxel grid and generates initial nodes for any light source voxels.
        /// </summary>
        public void InitializeBlockLightFirstPass(in VoxelData[] voxelGrid, Vector3Int d) {
            _blockNodes.Clear();
            for (var y = 0; y < d.y; y++) {
                for (var z = 0; z < d.z; z++) {
                    for (var x = 0; x < d.x; x++) {
                        ref readonly var voxel = ref voxelGrid[x + d.x * (y + d.y * z)];
                        if ((voxel.Flags & VoxelFlags.LightSource) == 0) continue;
                
                        _blockNodes.Push(new LightNode(new Vector3Int(x, y, z), voxel.ColorData));
                    }
                }
            }
        }

        public void PropagateSunLight(in VoxelData[] voxelData, in Color16[] sunLight, Vector3Int d) {
            while (_sunNodes.Count > 0) {
                // Grab node and sample lightmap at the node position.
                var node = _sunNodes.Pop();
                var cpi = VoxelUtility.VoxelIndex(in node.Position, in d);
                var current = sunLight[cpi];

                // Write max of the current light and node values.
                current = Color16.Max(current, node.Value);
                sunLight[cpi] = current;
                
                // Decompose the light here to avoid unnecessary bit ops till the packed value is needed.
                node.Value.Decompose(out var dr, out var dg, out var db, out _);
                
                if (dr + dg + db == 0) {
                    continue;
                }

                // Process each neighbor.
                for (var i = 0; i < 6; i++) {
                    // Grab neighbor voxel and light depending on locality.
                    // Voxels outside the current grid will be skipped.
                    var np = MeshTables.VoxelNeighbors[i] + node.Position;
                    var npi = VoxelUtility.VoxelIndex(in np, in d);
                    
                    if (!VoxelUtility.InLocalGrid(in np, in d)) {
                        continue;
                    }
                    
                    var neighbor = voxelData[npi];
                    var neighborLight = sunLight[npi];

                    // Skip if neighbor is opaque.
                    if (neighbor.Id != 0 && (neighbor.Flags & VoxelFlags.AlphaRender) == 0) {
                        continue;
                    }
                    
                    // Only decrement on all directions but down.
                    if (i != 5) {
                        dr--; dg--; db--;
                    }

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
                    // Prevents propagating through a superior light source or backwards.
                    neighborLight.Decompose(out var nlr, out var nlg, out var nlb, out _);
                    if (nlr >= ar && nlg >= ag && nlb >= ab) {
                        continue;
                    }
                    
                    // Push a new node to the stack.
                    _sunNodes.Push(new LightNode(np, new Color16(ar, ag, ab, 0)));
                }
            }
        }

        /// <summary>
        /// Propagates all block light nodes.
        /// Call this after initialization.
        /// </summary>
        public void PropagateBlockLight(in VoxelData[] voxelData, in Color16[] blockLight, Vector3Int d) {
            while (_blockNodes.Count > 0) {
                // Grab node and sample lightmap at the node position.
                var node = _blockNodes.Pop();
                var cpi = VoxelUtility.VoxelIndex(in node.Position, in d);
                var current = blockLight[cpi];

                // Write max of the current block light and node values.
                current = Color16.Max(current, node.Value);
                blockLight[cpi] = current;
                
                // Decompose the light here to avoid unnecessary bit ops till the packed value is needed.
                node.Value.Decompose(out var dr, out var dg, out var db, out _);
                
                // Decrement node RGB values before neighbor propagation.
                // Clamping happens in the neighbor loop so no need to do it here.
                dr--; dg--; db--;
                
                if (dr + dg + db == 0) {
                    continue;
                }

                // Process each neighbor.
                for (var i = 0; i < 6; i++) {
                    // Grab neighbor voxel and light depending on locality.
                    // Voxels outside the current grid will be skipped.
                    var np = MeshTables.VoxelNeighbors[i] + node.Position;
                    var npi = VoxelUtility.VoxelIndex(in np, in d);
                    
                    if (!VoxelUtility.InLocalGrid(in np, in d)) {
                        continue;
                    }
                    
                    var neighbor = voxelData[npi];
                    var neighborLight = blockLight[npi];

                    // Skip if neighbor is opaque.
                    if (neighbor.Id != 0 && (neighbor.Flags & VoxelFlags.AlphaRender) == 0) {
                        continue;
                    }

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
                    // Prevents propagating through a superior light source or backwards.
                    neighborLight.Decompose(out var nlr, out var nlg, out var nlb, out _);
                    if (nlr >= ar && nlg >= ag && nlb >= ab) {
                        continue;
                    }
                    
                    // Push a new node to the stack.
                    _blockNodes.Push(new LightNode(np, new Color16(ar, ag, ab, 0)));
                }
            }
        }
    }
}