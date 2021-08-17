using UnityEngine;
using VektorVoxels.Lighting;
using VektorVoxels.Meshing;

namespace VektorVoxels.Voxels {
    public sealed class VoxelDefinition {
        public readonly uint Id;
        public readonly string InternalName;
        public readonly string FriendlyName;
        public readonly VoxelFlags Flags;
        public readonly Color16 ColorData;
        
        // Top and Side texture rects.
        public readonly Rect[] TextureRects;

        /// <summary>
        /// Definition for a particular voxel type.
        /// Use this constructor to define your voxels.
        /// IDs are set automatically by order of definition at runtime.
        /// </summary>
        public VoxelDefinition(string internalName, string friendlyName, VoxelFlags flags, Color16 colorData, Vector2 atlasIndex) {
            Id = 0;
            InternalName = internalName;
            FriendlyName = friendlyName;
            Flags = flags;
            ColorData = colorData;
            
            // Generate rects for each index.
            TextureRects = new Rect[6];
            var uvWidth = CubicMesher.TEX_UV_WIDTH;
            atlasIndex *= uvWidth;
            for (var i = 0; i < 6; i++) {
                TextureRects[i] = new Rect(
                    atlasIndex.x, 1f - atlasIndex.y, 
                    uvWidth, -uvWidth
                );
            }
        }
        
        /// <summary>
        /// Definition for a particular voxel type.
        /// Use this constructor to define your voxels.
        /// IDs are set automatically by order of definition at runtime.
        /// </summary>
        public VoxelDefinition(string internalName, string friendlyName, VoxelFlags flags, Color16 colorData, Vector2[] atlasIndices) {
            Id = 0;
            InternalName = internalName;
            FriendlyName = friendlyName;
            Flags = flags;
            ColorData = colorData;
            
            // Generate rects for each index.
            TextureRects = new Rect[6];
            var uvWidth = CubicMesher.TEX_UV_WIDTH;
            for (var i = 0; i < 6; i++) {
                var atlasIndex = atlasIndices[i] * uvWidth;
                TextureRects[i] = new Rect(
                    atlasIndex.x, 1f - atlasIndex.y, 
                    uvWidth, -uvWidth
                );
            }
        }

        /// <summary>
        /// Creates a runtime voxel definition from a user-defined source.
        /// This is not meant to be called by the user.
        /// </summary>
        public VoxelDefinition(uint id, VoxelDefinition src) {
            Id = id;
            FriendlyName = src.FriendlyName;
            Flags = src.Flags;
            ColorData = src.ColorData;
            TextureRects = src.TextureRects;
        }

        /// <summary>
        /// Get a VoxelData instance from this voxel definition.
        /// </summary>
        /// <returns></returns>
        public VoxelData GetDataInstance() {
            return new VoxelData(Id, Flags, ColorData);
        }

        public Rect GetTextureRect(BlockSide side) {
            return TextureRects[(int)side];
        }
    }
}