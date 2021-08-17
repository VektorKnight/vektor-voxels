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
            return _runtimeVoxels[id - 1].FriendlyName.ToLower();
        }
        
        /// <summary>
        /// Gets the ID of a voxel by its internal name.
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

        public static VoxelDefinition[] Voxels => _runtimeVoxels;
        public static int VoxelCount => _runtimeVoxels.Length;
        
        /// <summary>
        /// Tries to find a voxel definition by internal name.
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
                _nameIdMap.Add(userVoxel.InternalName, id);
                id++;
            }
            
            Debug.Log($"[Voxel Table]: Successfully registered {UserVoxels.Length} voxel definitions.");
        }

        /// <summary>
        /// Define your voxels here.
        /// Avoid duplicate names or things will explode.
        /// </summary>
        private static readonly VoxelDefinition[] UserVoxels = {
            new VoxelDefinition(
                "grass", "Grass", 
                VoxelFlags.None, 
                FacingDirection.North,
                Color16.Clear(), 
                new Vector2(0, 0)
            ),
            new VoxelDefinition(
                "dirt", "Dirt", 
                VoxelFlags.None,
                FacingDirection.North,
                Color16.Clear(), 
                new Vector2(2, 0)
            ),
            new VoxelDefinition(
                "gravel", "Gravel", 
                VoxelFlags.None,
                FacingDirection.North,
                Color16.Clear(), 
                new Vector2(3, 0)
            ),
            new VoxelDefinition(
                "sand", "Sand", 
                VoxelFlags.None,
                FacingDirection.North,
                Color16.Clear(), 
                new Vector2(4, 0)
            ),
            new VoxelDefinition(
                "stone", "Stone", 
                VoxelFlags.None, 
                FacingDirection.North,
                Color16.Clear(), 
                new Vector2(5, 0)
            ),
            new VoxelDefinition(
                "bedrock", "Bedrock", 
                VoxelFlags.None, 
                FacingDirection.North,
                Color16.Clear(), 
                new Vector2(6, 0)
            ),
            new VoxelDefinition(
                "glass", "Glass", 
                VoxelFlags.AlphaRender, 
                FacingDirection.North,
                Color16.Clear(), 
                new Vector2(0, 6)
            ),
            new VoxelDefinition(
                "glass_red", "Red Glass", 
                VoxelFlags.AlphaRender, 
                FacingDirection.North,
                new Color16(15, 0, 0, 0).ToAttenuation(), 
                new Vector2(3, 6)
            ),
            new VoxelDefinition(
                "glass_green", "Green Glass", 
                VoxelFlags.AlphaRender, 
                FacingDirection.North,
                new Color16(0, 15, 0, 0).ToAttenuation(), 
                new Vector2(6, 6)
            ),
            new VoxelDefinition(
                "glass_blue", "Blue Glass", 
                VoxelFlags.AlphaRender, 
                FacingDirection.North,
                new Color16(0, 0, 15, 0).ToAttenuation(), 
                new Vector2(9, 6)
            ),
            new VoxelDefinition(
                "lightstone", "Lightstone", 
                VoxelFlags.LightSource,
                FacingDirection.North,
                new Color16(15, 15, 15, 0), 
                new Vector2(0, 7)
            ),
            new VoxelDefinition(
                "lightstone_red", "Red Lightstone", 
                VoxelFlags.LightSource,
                FacingDirection.North,
                new Color16(15, 3, 3, 0), 
                new Vector2(0, 7)
            ),
            new VoxelDefinition(
                "lightstone_green", "Green Lightstone", 
                VoxelFlags.LightSource,
                FacingDirection.North,
                new Color16(3, 15, 3, 0), 
                new Vector2(0, 7)
            ),
            new VoxelDefinition(
                "lightstone_blue", "Blue Lightstone", 
                VoxelFlags.LightSource,
                FacingDirection.North,
                new Color16(3, 3, 15, 0), 
                new Vector2(0, 7)
            ),
            new VoxelDefinition(
                "lightstone_yellow", "Yellow Lightstone", 
                VoxelFlags.LightSource,
                FacingDirection.North,
                new Color16(15, 15, 3, 0), 
                new Vector2(0, 7)
            ),
            new VoxelDefinition(
                "lightstone_purple", "Purple Lightstone", 
                VoxelFlags.LightSource,
                FacingDirection.North,
                new Color16(15, 3, 15, 0), 
                new Vector2(0, 7)
            ),
        };
}
}