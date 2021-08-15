using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VektorVoxels.Lighting;

namespace VektorVoxels.Voxels {
    /// <summary>
    /// Hard-coded voxel definitions cause I'm lazy.
    /// (Maybe an automatic system would be less work overall, oh well)
    /// </summary>
    public static class VoxelTable {
        /// <summary>
        /// Gets the name of a voxel by its ID.
        /// </summary>
        public static string GetVoxelName(uint id) {
            return _runtimeVoxels[id - 1].Name.ToLower();
        }
        
        /// <summary>
        /// Gets the ID of a voxel by its lower-case name.
        /// </summary>
        public static uint GetVoxelId(string name) {
            return _nameIdMap[name];
        }
        
        /// <summary>
        /// Gets a voxel definition by its ID.
        /// </summary>
        public static VoxelDefinition GetVoxelDefinition(uint id) {
            return _runtimeVoxels[id - 1];
        }
        
        /// <summary>
        /// Tries to find a voxel definition by name.
        /// Name lookups should always be lower-case.
        /// Do not use this function in any tight loops.
        /// Grab and cache the ID of the voxel you need.
        /// </summary>
        public static VoxelDefinition GetVoxelDefinition(string name) {
            var id = _nameIdMap[name];
            return _runtimeVoxels[id - 1];
        }

        private static VoxelDefinition[] _runtimeVoxels;
        private static Dictionary<string, uint> _nameIdMap;
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Initialize() {
            _runtimeVoxels = new VoxelDefinition[UserVoxels.Length];
            _nameIdMap = new Dictionary<string, uint>(UserVoxels.Length);
            
            // User IDs start at 1 as 0 us reserved for air/null.
            uint id = 1;
            foreach (var userVoxel in UserVoxels) {
                _runtimeVoxels[id - 1] = new VoxelDefinition(id, userVoxel);
                _nameIdMap.Add(userVoxel.Name.ToLower(), id);
                id++;
            }
            
            Debug.Log($"[Voxel Table]: Successfully registered {UserVoxels.Length} voxel definitions.");
        }

        /// <summary>
        /// Define your voxels here.
        /// Avoid duplicate names.
        /// </summary>
        private static readonly VoxelDefinition[] UserVoxels = {
            new VoxelDefinition(
                "Grass", 
                VoxelFlags.None, 
                Color16.Clear(), 
                new Vector2(0, 0), 
                new Vector2(1, 0)
            ),
            new VoxelDefinition(
                "Dirt", 
                VoxelFlags.None, 
                Color16.Clear(), 
                new Vector2(2, 0), 
                new Vector2(2, 0)
            ),
            new VoxelDefinition(
                "Gravel", 
                VoxelFlags.None, 
                Color16.Clear(), 
                new Vector2(3, 0), 
                new Vector2(3, 0)
            ),
            new VoxelDefinition(
                "Sand", 
                VoxelFlags.None, 
                Color16.Clear(), 
                new Vector2(4, 0), 
                new Vector2(4, 0)
            ),
            new VoxelDefinition(
                "Stone", 
                VoxelFlags.None, 
                Color16.Clear(), 
                new Vector2(5, 0), 
                new Vector2(5, 0)
            ),
            new VoxelDefinition(
                "Bedrock", 
                VoxelFlags.Unbreakable, 
                Color16.Clear(), 
                new Vector2(6, 0), 
                new Vector2(6, 0)
            ),
            new VoxelDefinition(
                "Glass", 
                VoxelFlags.AlphaRender, 
                Color16.Clear(), 
                new Vector2(0, 6), 
                new Vector2(0, 6)
            ),
            new VoxelDefinition(
                "Glass Red", 
                VoxelFlags.AlphaRender, 
                new Color16(15, 0, 0, 0).ToAttenuation(), 
                new Vector2(3, 6), 
                new Vector2(3, 6)
            ),
            new VoxelDefinition(
                "Glass Green", 
                VoxelFlags.AlphaRender, 
                new Color16(0, 15, 0, 0).ToAttenuation(), 
                new Vector2(6, 6), 
                new Vector2(6, 6)
            ),
            new VoxelDefinition(
                "Glass Blue", 
                VoxelFlags.AlphaRender, 
                new Color16(0, 0, 15, 0).ToAttenuation(), 
                new Vector2(9, 6), 
                new Vector2(9, 6)
            ),
            new VoxelDefinition(
                "Glowstone", 
                VoxelFlags.LightSource, 
                new Color16(15, 0, 0, 0), 
                new Vector2(0, 7), 
                new Vector2(0, 7)
            ),
            new VoxelDefinition(
                "Bluestone", 
                VoxelFlags.LightSource, 
                new Color16(0, 0, 15, 0), 
                new Vector2(0, 7), 
                new Vector2(0, 7)
            ),
        };
}
}