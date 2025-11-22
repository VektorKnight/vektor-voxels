using System.Collections.Generic;
using UnityEngine;

namespace VektorVoxels.Voxels {
    /// <summary>
    /// ScriptableObject containing all voxel definitions for the game.
    /// Assign this to VoxelTable to load voxels at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "VoxelDatabase", menuName = "Vektor Voxels/Voxel Database", order = 1)]
    public class VoxelDatabaseAsset : ScriptableObject {
        [Tooltip("Texture atlas containing all voxel textures")]
        public Texture2D TextureAtlas;

        [Tooltip("Size of each tile in pixels")]
        public int TileSize = 16;

        [Tooltip("All voxel definitions in this database")]
        public List<VoxelDefinitionData> Voxels = new List<VoxelDefinitionData>();

        /// <summary>
        /// Number of tiles per row in the atlas.
        /// </summary>
        public int TilesPerRow => TextureAtlas != null ? TextureAtlas.width / TileSize : 16;

        /// <summary>
        /// Number of tiles per column in the atlas.
        /// </summary>
        public int TilesPerColumn => TextureAtlas != null ? TextureAtlas.height / TileSize : 16;

        /// <summary>
        /// Converts all definitions to runtime VoxelDefinition array.
        /// IDs are assigned sequentially starting at 1.
        /// </summary>
        public VoxelDefinition[] ToRuntimeDefinitions() {
            var definitions = new VoxelDefinition[Voxels.Count];
            for (int i = 0; i < Voxels.Count; i++) {
                definitions[i] = Voxels[i].ToVoxelDefinition((ushort)(i + 1));
            }
            return definitions;
        }

        /// <summary>
        /// Validates the database for common issues.
        /// Returns list of warning/error messages.
        /// </summary>
        public List<string> Validate() {
            var issues = new List<string>();
            var names = new HashSet<string>();

            for (int i = 0; i < Voxels.Count; i++) {
                var voxel = Voxels[i];

                // Check for empty names
                if (string.IsNullOrWhiteSpace(voxel.InternalName)) {
                    issues.Add($"Voxel at index {i} has empty internal name");
                }
                else {
                    // Check for duplicates
                    if (!names.Add(voxel.InternalName.ToLower())) {
                        issues.Add($"Duplicate internal name: '{voxel.InternalName}'");
                    }

                    // Check for spaces/uppercase in internal name
                    if (voxel.InternalName.Contains(" ")) {
                        issues.Add($"Internal name '{voxel.InternalName}' contains spaces");
                    }
                }

                // Validate texture coordinates
                if (TextureAtlas != null) {
                    if (voxel.UseSingleTexture) {
                        if (voxel.SingleTexture.x >= TilesPerRow || voxel.SingleTexture.y >= TilesPerColumn) {
                            issues.Add($"Voxel '{voxel.InternalName}' texture out of bounds");
                        }
                    }
                    else {
                        for (int f = 0; f < 6; f++) {
                            if (voxel.FaceTextures[f].x >= TilesPerRow || voxel.FaceTextures[f].y >= TilesPerColumn) {
                                issues.Add($"Voxel '{voxel.InternalName}' face {f} texture out of bounds");
                            }
                        }
                    }
                }
            }

            return issues;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Populates database from hardcoded VoxelTable definitions.
        /// Editor only - used for migration.
        /// </summary>
        [ContextMenu("Import from Hardcoded Table")]
        public void ImportFromHardcodedTable() {
            Voxels.Clear();
            var definitions = VoxelTable.Voxels;
            if (definitions == null) return;

            foreach (var def in definitions) {
                Voxels.Add(VoxelDefinitionData.FromVoxelDefinition(def));
            }

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"Imported {Voxels.Count} voxels from hardcoded table");
        }
#endif
    }
}
