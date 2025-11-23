using UnityEngine;
using VektorVoxels.Lighting;
using VektorVoxels.Meshing;

namespace VektorVoxels.Voxels {
    public sealed class VoxelDefinition {
        public readonly ushort Id;
        public readonly string InternalName;
        public readonly string FriendlyName;
        public readonly VoxelFlags Flags;
        public readonly FacingDirection Orientation;
        public readonly VoxelColor ColorData;
        
        // Top and Side texture rects.
        public readonly Rect[] TextureRects;

        /// <summary>
        /// Definition for a particular voxel type.
        /// Use this constructor to define your voxels.
        /// IDs are set automatically by order of definition at runtime.
        /// </summary>
        public VoxelDefinition(string internalName, string friendlyName, VoxelFlags flags, FacingDirection orientation, VoxelColor colorData, Vector2 atlasIndex) {
            Id = 0;
            InternalName = internalName;
            FriendlyName = friendlyName;
            Flags = flags;
            Orientation = orientation;
            ColorData = colorData;
            
            // Generate rects for each index.
            TextureRects = new Rect[6];
            var uvWidth = VisualMesher.TEX_UV_WIDTH;
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
        public VoxelDefinition(string internalName, string friendlyName, VoxelFlags flags, FacingDirection orientation, VoxelColor colorData, Vector2[] atlasIndices) {
            Id = 0;
            InternalName = internalName;
            FriendlyName = friendlyName;
            Flags = flags;
            Orientation = orientation;
            ColorData = colorData;

            // Generate rects for each index.
            TextureRects = new Rect[6];
            var uvWidth = VisualMesher.TEX_UV_WIDTH;
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
        public VoxelDefinition(ushort id, VoxelDefinition src) {
            Id = id;
            InternalName = src.InternalName;
            FriendlyName = src.FriendlyName;
            Flags = src.Flags;
            Orientation = src.Orientation;
            ColorData = src.ColorData;
            TextureRects = src.TextureRects;
        }

        /// <summary>
        /// Get a VoxelData instance from this voxel definition.
        /// </summary>
        public VoxelData GetDataInstance() {
            return new VoxelData(Id, Flags, Orientation, ColorData);
        }
        
        /// <summary>
        /// Get a VoxelData instance from this voxel definition with a custom orientation.
        /// </summary>
        public VoxelData GetDataInstance(FacingDirection orientation) {
            return new VoxelData(Id, Flags, orientation, ColorData);
        }
        
        /// <summary>
        /// Returns the texture atlas rect for the desired face.
        /// </summary>
        public Rect GetTextureRect(FacingDirection side) {
            return TextureRects[(int)side];
        }
    }
}