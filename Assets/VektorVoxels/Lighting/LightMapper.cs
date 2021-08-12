using System.Collections.Generic;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Meshing;
using VektorVoxels.Voxels;

namespace VektorVoxels.Lighting {
    /// <summary>
    /// Performs lightmapping for a voxel grid.
    /// </summary>
    public sealed class LightMapper {
        private static readonly Vector3Int[] _sunNeighbors = {
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

        /// <summary>
        /// Scans the voxel grid and sets any light values above the heightmap to Color16.White().
        /// propagation nodes are then placed wherever the heightmap value borders open air.
        /// </summary>
        public void InitializeSunLightFirstPass(in VoxelData[] voxelData, in HeightData[] heightMap, in Color16[] sunLight, Vector3Int d) {
            _sunNodes.Clear();
            var vp = Vector3Int.zero;
            for (var z = 0; z < d.z; z++) {
                for (var x = 0; x < d.x; x++) {
                    // Grab current height data.
                    ref var heightData = ref heightMap[VoxelUtility.HeightIndex(x, z, d.x)];
                    
                    // Skip clean height values.
                    if (!heightData.Dirty) {
                        continue;
                    }
                    
                    // Iterate the entire column.
                    for (var y = d.y - 1; y >= 0; y--) {
                        // Grab current voxel and heightmap value.
                        vp.x = x; vp.y = y; vp.z = z;
                        var vpi = VoxelUtility.VoxelIndex(x, y, z, in d);

                        // Clear dirty flag.
                        heightData.Dirty = false;
                        
                        // Set any values below the heightmap to zero and continue.
                        if (y < heightData.Value) {
                            sunLight[vpi] = Color16.Clear();
                            continue;
                        }
                        
                        // Set all values above the heightmap to full white.
                        sunLight[vpi] = new Color16(15, 15, 15, 0);
                        
                        // Check neighbors for null/non-opaque blocks below the heightmap.
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
                            var neighborHeight = heightMap[VoxelUtility.HeightIndex(np.x, np.z, d.x)].Value;

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
        
        /// <summary>
        /// Propagates any sunlight nodes.
        /// </summary>
        public void PropagateSunLight(in VoxelData[] voxelData, in Color16[] sunLight, Vector3Int d) {
            PropagateLightNodes(voxelData, sunLight, d, true);
        }
        
        /// <summary>
        /// Propagates any block light nodes.
        /// </summary>
        public void PropagateBlockLight(in VoxelData[] voxelData, in Color16[] blockLight, Vector3Int d) {
            PropagateLightNodes(voxelData, blockLight, d, false);
        }
        
        /// <summary>
        /// Performs light propagation on a given voxel data set and lightmap.
        /// </summary>
        private void PropagateLightNodes(in VoxelData[] voxelData, in Color16[] lightMap, Vector3Int d, bool sun) {
            var stack = sun ? _sunNodes : _blockNodes;
            while (stack.Count > 0) {
                // Grab node and sample lightmap at the node position.
                var node = stack.Pop();
                var cpi = VoxelUtility.VoxelIndex(in node.Position, in d);
                var current = lightMap[cpi];

                // Write max of the current light and node values.
                current = Color16.Max(current, node.Value);
                lightMap[cpi] = current;
                
                // Decompose the light here to avoid unnecessary bit ops till the packed value is needed.
                node.Value.Decompose(out var nr, out var ng, out var nb, out _);
                
                // Done propagating block light if the node value is <=1 on all channels.
                if (!sun && nr <= 1 && ng <= 1 && nb <= 1) {
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
                    var neighborLight = lightMap[npi];

                    // Skip if neighbor is opaque.
                    if (neighbor.Id != 0 && (neighbor.Flags & VoxelFlags.AlphaRender) == 0) {
                        continue;
                    }
                    
                    // Decrement light on propagation unless the light type is sun and we're propagating down.
                    int dr, dg, db;
                    if (sun && i == 5) {
                        dr = nr;
                        dg = ng;
                        db = nb;
                    }
                    else {
                        dr = nr - 1;
                        dg = ng - 1;
                        db = nb - 1;
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
    }
}