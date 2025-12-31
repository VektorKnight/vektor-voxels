using System;
using UnityEngine;
using VektorVoxels.Lighting;

namespace VektorVoxels.Voxels {
    /// <summary>
    /// Serializable voxel definition for use in ScriptableObjects.
    /// Contains all data needed to define a voxel type.
    /// </summary>
    [Serializable]
    public class VoxelDefinitionData {
        [Tooltip("Internal name used for lookups (lowercase, no spaces)")]
        public string InternalName;

        [Tooltip("Voxel behavior flags")]
        public VoxelFlags Flags;

        [Tooltip("Default facing direction")]
        public FacingDirection Orientation = FacingDirection.North;

        [Tooltip("Light emission color (for light sources) or attenuation (for transparent blocks)")]
        public Color LightColor = Color.clear;

        [Tooltip("Use same texture for all faces")]
        public bool UseSingleTexture = true;

        [Tooltip("Texture tile coordinates (in tiles, not pixels)")]
        public Vector2Int SingleTexture;

        [Tooltip("Per-face texture coordinates: North, East, South, West, Top, Bottom")]
        public Vector2Int[] FaceTextures = new Vector2Int[6];

        /// <summary>
        /// Converts to runtime VoxelDefinition with assigned ID.
        /// </summary>
        public VoxelDefinition ToVoxelDefinition(ushort id) {
            // Convert Unity Color to LightColor (RGB565)
            // For translucent blocks, this is the pass-through tint (255 = full pass, 0 = full block)
            var lightColor = new Lighting.VoxelColor(
                (int)(LightColor.r * 255),
                (int)(LightColor.g * 255),
                (int)(LightColor.b * 255)
            );

            // Create definition without ID first, then wrap with ID
            VoxelDefinition baseDef;
            if (UseSingleTexture) {
                baseDef = new VoxelDefinition(
                    InternalName,
                    InternalName,
                    Flags,
                    Orientation,
                    lightColor,
                    new Vector2(SingleTexture.x, SingleTexture.y)
                );
            }
            else {
                var textures = new Vector2[6];
                for (int i = 0; i < 6; i++) {
                    textures[i] = new Vector2(FaceTextures[i].x, FaceTextures[i].y);
                }
                baseDef = new VoxelDefinition(
                    InternalName,
                    InternalName,
                    Flags,
                    Orientation,
                    lightColor,
                    textures
                );
            }

            // Wrap with ID
            return new VoxelDefinition(id, baseDef);
        }

        /// <summary>
        /// Creates a VoxelDefinitionData from existing hardcoded definition.
        /// </summary>
        public static VoxelDefinitionData FromVoxelDefinition(VoxelDefinition def) {
            var data = new VoxelDefinitionData {
                InternalName = def.InternalName,
                Flags = def.Flags,
                Orientation = def.Orientation
            };

            // Convert LightColor back to Unity Color
            def.ColorData.Decompose(out var r, out var g, out var b, out _);
            data.LightColor = new Color(r / 255f, g / 255f, b / 255f, 1f);

            // Extract texture coordinates from TextureRects
            // TextureRects are computed as: Rect(x * uvWidth, 1 - y * uvWidth, uvWidth, -uvWidth)
            // So to reverse: tileX = rect.x / uvWidth, tileY = (1 - rect.y) / uvWidth
            data.FaceTextures = new Vector2Int[6];

            if (def.TextureRects != null && def.TextureRects.Length >= 6) {
                // Get uvWidth from the rect width (it's stored as the width value)
                var uvWidth = Mathf.Abs(def.TextureRects[0].width);
                if (uvWidth > 0) {
                    for (int i = 0; i < 6; i++) {
                        var rect = def.TextureRects[i];
                        var tileX = Mathf.RoundToInt(rect.x / uvWidth);
                        var tileY = Mathf.RoundToInt((1f - rect.y) / uvWidth);
                        data.FaceTextures[i] = new Vector2Int(tileX, tileY);
                    }
                }
            }

            // Check if all face textures are the same
            var firstTex = data.FaceTextures[0];
            bool allSame = true;
            for (int i = 1; i < 6; i++) {
                if (data.FaceTextures[i] != firstTex) {
                    allSame = false;
                    break;
                }
            }

            data.UseSingleTexture = allSame;
            data.SingleTexture = firstTex;

            return data;
        }
    }
}
