using System;
using System.Runtime.InteropServices;
using VektorVoxels.Lighting;
using VektorVoxels.Voxels;

namespace VektorVoxels.World.Chunks {
    public sealed class ChunkData {
        private readonly uint _id;
        
        private readonly VoxelData[] _voxelData;
        private readonly HeightData[] _heightData;
        
        private readonly Color16[] _skyLight;
        private readonly Color16[] _blockLight;

        public VoxelData[] VoxelData => _voxelData;
        public HeightData[] HeightData => _heightData;
        public Color16[] SkyLight => _skyLight;
        public Color16[] BlockLight => _blockLight;

        public ChunkData(uint id) {
            _id = id;

            _voxelData = new VoxelData[Chunk.WIDTH * Chunk.WIDTH * Chunk.HEIGHT];
            _heightData = new HeightData[Chunk.WIDTH * Chunk.HEIGHT];

            _skyLight = new Color16[_voxelData.Length];
            _blockLight = new Color16[_voxelData.Length];
        }
        
        /// <summary>
        /// Copies voxel and height data to a byte buffer for serialization.
        /// </summary>
        public void CopyToBuffer(byte[] destination) {
            if (destination == null) {
                throw new ArgumentNullException(nameof(destination));
            }
            
            var voxelSizeBytes = Chunk.WIDTH * Chunk.WIDTH * Chunk.HEIGHT * Marshal.SizeOf<VoxelData>();
            var heightSizeBytes = Chunk.WIDTH * Chunk.HEIGHT * Marshal.SizeOf<HeightData>();

            if (destination.Length != voxelSizeBytes + heightSizeBytes) {
                throw new ArgumentException("Destination is not the correct size!", nameof(destination));
            }
            
            // Not many other ways to convert the data to bytes for serialization.
            unsafe {
                // Pin destination buffer.
                fixed (byte* dst = destination) {
                    // Pin and copy in voxel data as bytes.
                    fixed (VoxelData* src = _voxelData) {
                        Buffer.MemoryCopy(src, dst, destination.Length, voxelSizeBytes);
                    }
                    
                    // Pin and copy in height data after the voxel data.
                    fixed (HeightData* src = _heightData) {
                        Buffer.MemoryCopy(src, dst + voxelSizeBytes, destination.Length, heightSizeBytes);
                    }
                }
            }
        }
        
        /// <summary>
        /// Copies voxel and height data from a byte buffer for deserialization.
        /// </summary>
        public void CopyFromBuffer(byte[] source) {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }
            
            var voxelSizeBytes = Chunk.WIDTH * Chunk.WIDTH * Chunk.HEIGHT * Marshal.SizeOf<VoxelData>();
            var heightSizeBytes = Chunk.WIDTH * Chunk.HEIGHT * Marshal.SizeOf<HeightData>();

            if (source.Length != voxelSizeBytes + heightSizeBytes) {
                throw new ArgumentException("Source is not the correct size!", nameof(source));
            }
            
            unsafe {
                // Pin source buffer.
                fixed (byte* src = source) {
                    // Pin and copy in voxel data as bytes.
                    fixed (VoxelData* dst = _voxelData) {
                        Buffer.MemoryCopy(src, dst, voxelSizeBytes, voxelSizeBytes);
                    }
                    
                    // Pin and copy in height data after the voxel data.
                    fixed (HeightData* dst = _heightData) {
                        Buffer.MemoryCopy(src + voxelSizeBytes, dst, heightSizeBytes, heightSizeBytes);
                    }
                }
            }
        }
    }
}