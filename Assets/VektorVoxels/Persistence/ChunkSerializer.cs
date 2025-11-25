using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Persistence {
    /// <summary>
    /// Handles RLE compression and serialization of chunk voxel data.
    /// Format: Y-columns encoded with run-length encoding.
    /// </summary>
    public static class ChunkSerializer {
        private const byte FORMAT_VERSION = 1;

        /// <summary>
        /// Serializes chunk voxel data to a compressed byte array.
        /// </summary>
        public static byte[] Serialize(Vector2Int chunkPos, VoxelData[] voxelData) {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // Header
            writer.Write(FORMAT_VERSION);
            writer.Write(chunkPos.x);
            writer.Write(chunkPos.y);

            var dimensions = VoxelWorld.CHUNK_SIZE;

            // Encode each Y-column with RLE
            for (var z = 0; z < dimensions.x; z++) {
                for (var x = 0; x < dimensions.x; x++) {
                    WriteColumn(writer, voxelData, x, z, dimensions);
                }
            }

            return ms.ToArray();
        }

        /// <summary>
        /// Deserializes chunk voxel data from a compressed byte array.
        /// </summary>
        public static bool Deserialize(byte[] data, out Vector2Int chunkPos, VoxelData[] voxelData, Dictionary<ushort, ushort> idRemap) {
            chunkPos = Vector2Int.zero;

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            // Header
            var version = reader.ReadByte();
            if (version != FORMAT_VERSION) {
                Debug.LogError($"[ChunkSerializer] Unsupported format version: {version}");
                return false;
            }

            chunkPos = new Vector2Int(reader.ReadInt32(), reader.ReadInt32());

            var dimensions = VoxelWorld.CHUNK_SIZE;

            // Decode each Y-column
            for (var z = 0; z < dimensions.x; z++) {
                for (var x = 0; x < dimensions.x; x++) {
                    if (!ReadColumn(reader, voxelData, x, z, dimensions, idRemap)) {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void WriteColumn(BinaryWriter writer, VoxelData[] voxelData, int x, int z, Vector2Int dimensions) {
            var runs = new List<(ushort id, byte orientation, byte count)>();

            ushort currentId = 0;
            byte currentOrientation = 0;
            byte count = 0;

            for (var y = 0; y < dimensions.y; y++) {
                var index = VoxelUtility.VoxelIndex(x, y, z, dimensions);
                var voxel = voxelData[index];

                if (y == 0) {
                    currentId = voxel.Id;
                    currentOrientation = (byte)voxel.Orientation;
                    count = 1;
                }
                else if (voxel.Id == currentId && (byte)voxel.Orientation == currentOrientation && count < 255) {
                    count++;
                }
                else {
                    runs.Add((currentId, currentOrientation, count));
                    currentId = voxel.Id;
                    currentOrientation = (byte)voxel.Orientation;
                    count = 1;
                }
            }

            // Add final run
            runs.Add((currentId, currentOrientation, count));

            // Write run count and runs
            writer.Write((byte)runs.Count);
            foreach (var run in runs) {
                writer.Write(run.id);
                writer.Write(run.orientation);
                writer.Write(run.count);
            }
        }

        private static bool ReadColumn(BinaryReader reader, VoxelData[] voxelData, int x, int z, Vector2Int dimensions, Dictionary<ushort, ushort> idRemap) {
            var runCount = reader.ReadByte();
            var y = 0;

            for (var r = 0; r < runCount; r++) {
                var savedId = reader.ReadUInt16();
                var orientation = (FacingDirection)reader.ReadByte();
                var count = reader.ReadByte();

                // Remap ID if needed
                ushort id = savedId;
                if (idRemap != null && idRemap.TryGetValue(savedId, out var remappedId)) {
                    id = remappedId;
                }

                // Get voxel data from definition (restores flags and color)
                VoxelData voxel;
                if (id == 0) {
                    voxel = Voxels.VoxelData.Empty();
                }
                else {
                    var def = VoxelTable.GetVoxelDefinition(id);
                    voxel = def.GetDataInstance(orientation);
                }

                // Fill the run
                for (var i = 0; i < count; i++) {
                    if (y >= dimensions.y) {
                        Debug.LogError($"[ChunkSerializer] Column overflow at ({x}, {z})");
                        return false;
                    }

                    var index = VoxelUtility.VoxelIndex(x, y, z, dimensions);
                    voxelData[index] = voxel;
                    y++;
                }
            }

            if (y != dimensions.y) {
                Debug.LogError($"[ChunkSerializer] Column height mismatch at ({x}, {z}): expected {dimensions.y}, got {y}");
                return false;
            }

            return true;
        }
    }
}
