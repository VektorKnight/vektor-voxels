using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VektorVoxels.Lighting;

namespace VektorVoxels.Voxels {
    /// <summary>
    /// Central voxel registry with O(1) lookup by ID and name.
    /// IDs start at 1 (0 is reserved for air/null).
    /// Can load from ScriptableObject database or fallback to hardcoded definitions.
    /// </summary>
    public static class VoxelTable {
        // Path to default database in Resources folder
        private const string DATABASE_RESOURCE_PATH = "VoxelDatabase";

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
        public static int VoxelCount => _runtimeVoxels != null ? _runtimeVoxels.Length : 0;

        /// <summary>
        /// Tries to find a voxel definition by internal name.
        /// Do not use this function in any tight loops.
        /// Grab and cache the ID of the voxel you need.
        /// </summary>
        public static VoxelDefinition GetVoxelDefinition(string name) {
            var id = _nameIdMap[name];
            return _runtimeVoxels[id - 1];
        }

        // Runtime voxel array indexed by (id - 1). ID 0 is air/null.
        private static VoxelDefinition[] _runtimeVoxels;
        // Name-to-ID cache for string lookups.
        private static Dictionary<string, uint> _nameIdMap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Initialize() {
            // Try to load from ScriptableObject database first
            var database = Resources.Load<VoxelDatabaseAsset>(DATABASE_RESOURCE_PATH);

            if (database != null && database.Voxels.Count > 0) {
                LoadFromDatabase(database);
                Debug.Log($"[Voxel Table]: Loaded {_runtimeVoxels.Length} voxels from database asset.");
            }
            else {
                // Fallback to hardcoded definitions
                LoadFromHardcoded();
                Debug.Log($"[Voxel Table]: Loaded {_runtimeVoxels.Length} voxels from hardcoded table.");
            }
        }

        private static void LoadFromDatabase(VoxelDatabaseAsset database) {
            _runtimeVoxels = database.ToRuntimeDefinitions();
            _nameIdMap = new Dictionary<string, uint>(_runtimeVoxels.Length);

            for (int i = 0; i < _runtimeVoxels.Length; i++) {
                var name = _runtimeVoxels[i].InternalName;
                if (string.IsNullOrEmpty(name)) {
                    Debug.LogError($"[Voxel Table]: Voxel at index {i} has null/empty InternalName. Skipping.");
                    continue;
                }
                _nameIdMap.Add(name, (uint)(i + 1));
            }
        }

        private static void LoadFromHardcoded() {
            _runtimeVoxels = new VoxelDefinition[UserVoxels.Length];
            _nameIdMap = new Dictionary<string, uint>(UserVoxels.Length);

            // User IDs start at 1 as 0 is reserved for air/null.
            ushort id = 1;
            foreach (var userVoxel in UserVoxels) {
                _runtimeVoxels[id - 1] = new VoxelDefinition(id, userVoxel);
                _nameIdMap.Add(userVoxel.InternalName, id);
                id++;
            }
        }

        /// <summary>
        /// Manually load from a specific database. Useful for runtime switching.
        /// </summary>
        public static void LoadDatabase(VoxelDatabaseAsset database) {
            if (database == null || database.Voxels.Count == 0) {
                Debug.LogError("[Voxel Table]: Cannot load null or empty database");
                return;
            }

            LoadFromDatabase(database);
            Debug.Log($"[Voxel Table]: Reloaded {_runtimeVoxels.Length} voxels from database.");
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
                VoxelColor.Clear(),
                new [] {
                    new Vector2(1, 0),
                    new Vector2(1, 0),
                    new Vector2(1, 0),
                    new Vector2(1, 0),
                    new Vector2(0, 0),
                    new Vector2(2, 0)
                }
            ),
            new VoxelDefinition(
                "dirt", "Dirt",
                VoxelFlags.None,
                FacingDirection.North,
                VoxelColor.Clear(),
                new Vector2(2, 0)
            ),
            new VoxelDefinition(
                "gravel", "Gravel",
                VoxelFlags.None,
                FacingDirection.North,
                VoxelColor.Clear(),
                new Vector2(3, 0)
            ),
            new VoxelDefinition(
                "sand", "Sand",
                VoxelFlags.None,
                FacingDirection.North,
                VoxelColor.Clear(),
                new Vector2(4, 0)
            ),
            new VoxelDefinition(
                "stone", "Stone",
                VoxelFlags.None,
                FacingDirection.North,
                VoxelColor.Clear(),
                new Vector2(5, 0)
            ),
            new VoxelDefinition(
                "bedrock", "Bedrock",
                VoxelFlags.None,
                FacingDirection.North,
                VoxelColor.Clear(),
                new Vector2(6, 0)
            ),
            new VoxelDefinition(
                "cobblestone", "Cobblestone",
                VoxelFlags.None,
                FacingDirection.North,
                VoxelColor.Clear(),
                new Vector2(0, 1)
            ),
            new VoxelDefinition(
                "glass", "Glass",
                VoxelFlags.AlphaRender,
                FacingDirection.North,
                VoxelColor.White(),  // Full pass-through
                new Vector2(0, 6)
            ),
            new VoxelDefinition(
                "glass_red", "Red Glass",
                VoxelFlags.AlphaRender,
                FacingDirection.North,
                new VoxelColor(255, 64, 64),  // Pass red, block most green/blue
                new Vector2(3, 6)
            ),
            new VoxelDefinition(
                "glass_green", "Green Glass",
                VoxelFlags.AlphaRender,
                FacingDirection.North,
                new VoxelColor(64, 255, 64),  // Pass green, block most red/blue
                new Vector2(6, 6)
            ),
            new VoxelDefinition(
                "glass_blue", "Blue Glass",
                VoxelFlags.AlphaRender,
                FacingDirection.North,
                new VoxelColor(64, 64, 255),  // Pass blue, block most red/green
                new Vector2(9, 6)
            ),
            new VoxelDefinition(
                "lightstone", "Lightstone",
                VoxelFlags.LightSource,
                FacingDirection.North,
                new VoxelColor(255, 255, 255),
                new Vector2(0, 7)
            ),
            new VoxelDefinition(
                "lightstone_red", "Red Lightstone",
                VoxelFlags.LightSource,
                FacingDirection.North,
                new VoxelColor(255, 0, 0),
                new Vector2(1, 7)
            ),
            new VoxelDefinition(
                "lightstone_green", "Green Lightstone",
                VoxelFlags.LightSource,
                FacingDirection.North,
                new VoxelColor(0, 255, 0),
                new Vector2(2, 7)
            ),
            new VoxelDefinition(
                "lightstone_blue", "Blue Lightstone",
                VoxelFlags.LightSource,
                FacingDirection.North,
                new VoxelColor(0, 0, 255),
                new Vector2(3, 7)
            ),
            new VoxelDefinition(
                "lightstone_yellow", "Yellow Lightstone",
                VoxelFlags.LightSource,
                FacingDirection.North,
                new VoxelColor(255, 204, 0),
                new Vector2(4, 7)
            ),
            new VoxelDefinition(
                "lightstone_purple", "Purple Lightstone",
                VoxelFlags.LightSource,
                FacingDirection.North,
                new VoxelColor(119, 0, 255),
                new Vector2(5, 7)
            ),
        };
}
}