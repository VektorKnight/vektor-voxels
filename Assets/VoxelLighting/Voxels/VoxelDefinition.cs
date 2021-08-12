using UnityEngine;
using VoxelLighting.Lighting;

namespace VoxelLighting.Voxels {
    public sealed class VoxelDefinition {
        public readonly uint Id;
        public readonly string Name;
        public readonly VoxelFlags Flags;
        public readonly Color16 ColorData;
        
        // Top and Side texture rects.
        public readonly Vector2 AtlasA, AtlasB;

        public VoxelDefinition(uint id, string name, VoxelFlags flags, Color16 colorData, Vector2 atlasA, Vector2 atlasB) {
            Id = id;
            Name = name;
            Flags = flags;
            ColorData = colorData;
            AtlasA = atlasA;
            AtlasB = atlasB;
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