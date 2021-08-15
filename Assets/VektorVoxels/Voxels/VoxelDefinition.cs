using UnityEngine;
using VektorVoxels.Lighting;

namespace VektorVoxels.Voxels {
    public sealed class VoxelDefinition {
        public readonly uint Id;
        public readonly string Name;
        public readonly VoxelFlags Flags;
        public readonly Color16 ColorData;
        
        // Top and Side texture rects.
        public readonly Vector2 AtlasA, AtlasB;
        
        /// <summary>
        /// Definition for a particular voxel type.
        /// Use this constructor to define your voxels.
        /// IDs are set automatically by order of definition at runtime.
        /// </summary>
        public VoxelDefinition(string name, VoxelFlags flags, Color16 colorData, Vector2 atlasA, Vector2 atlasB) {
            Id = 0;
            Name = name;
            Flags = flags;
            ColorData = colorData;
            AtlasA = atlasA;
            AtlasB = atlasB;
        }
        
        /// <summary>
        /// Creates a runtime voxel definition from a user-defined source.
        /// This is not meant to be called by the user.
        /// </summary>
        public VoxelDefinition(uint id, VoxelDefinition src) {
            Id = id;
            Name = src.Name;
            Flags = src.Flags;
            ColorData = src.ColorData;
            AtlasA = src.AtlasA;
            AtlasB = src.AtlasB;
        }

        /// <summary>
        /// Get a VoxelData instance from this voxel definition.
        /// </summary>
        /// <returns></returns>
        public VoxelData GetDataInstance() {
            return new VoxelData(Id, Flags, ColorData);
        }
    }
}