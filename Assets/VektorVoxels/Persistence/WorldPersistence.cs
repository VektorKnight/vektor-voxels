using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Threading;
using VektorVoxels.Voxels;

namespace VektorVoxels.Persistence {
    /// <summary>
    /// Manages world persistence: saving/loading world metadata and chunk data.
    /// Supports async chunk saving via thread pool.
    /// </summary>
    public class WorldPersistence {
        private readonly string _worldsRoot;
        private string _currentWorldPath;
        private string _chunksPath;
        private WorldSaveData _worldData;
        private VoxelIdRemapper _idRemapper;

        public WorldSaveData WorldData => _worldData;
        public bool IsWorldLoaded => _worldData != null;
        public string CurrentWorldName => _worldData?.Name;

        public WorldPersistence() {
            _worldsRoot = Path.Combine(Application.persistentDataPath, "worlds");
            _idRemapper = new VoxelIdRemapper();
        }

        /// <summary>
        /// Gets all available world names.
        /// </summary>
        public string[] GetAvailableWorlds() {
            if (!Directory.Exists(_worldsRoot)) {
                return Array.Empty<string>();
            }

            var dirs = Directory.GetDirectories(_worldsRoot);
            var worlds = new List<string>();

            foreach (var dir in dirs) {
                var worldJsonPath = Path.Combine(dir, "world.json");
                if (File.Exists(worldJsonPath)) {
                    worlds.Add(Path.GetFileName(dir));
                }
            }

            return worlds.ToArray();
        }

        /// <summary>
        /// Creates a new world with the given name and seed.
        /// </summary>
        public bool CreateWorld(string name, int seed) {
            var worldPath = Path.Combine(_worldsRoot, SanitizeWorldName(name));

            if (Directory.Exists(worldPath)) {
                Debug.LogError($"[WorldPersistence] World already exists: {name}");
                return false;
            }

            try {
                Directory.CreateDirectory(worldPath);
                Directory.CreateDirectory(Path.Combine(worldPath, "chunks"));

                _worldData = new WorldSaveData(name, seed);
                _worldData.VoxelMapping = VoxelIdRemapper.BuildCurrentMapping();

                _currentWorldPath = worldPath;
                _chunksPath = Path.Combine(worldPath, "chunks");

                SaveWorldMetadata();
                Debug.Log($"[WorldPersistence] Created world: {name} at {worldPath}");
                return true;
            }
            catch (Exception e) {
                Debug.LogError($"[WorldPersistence] Failed to create world: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loads an existing world by name.
        /// Returns false if the world doesn't exist or has missing voxels.
        /// </summary>
        public bool LoadWorld(string name, out List<string> missingVoxels) {
            missingVoxels = null;
            var worldPath = Path.Combine(_worldsRoot, SanitizeWorldName(name));
            var worldJsonPath = Path.Combine(worldPath, "world.json");

            if (!File.Exists(worldJsonPath)) {
                Debug.LogError($"[WorldPersistence] World not found: {name}");
                return false;
            }

            try {
                var json = File.ReadAllText(worldJsonPath);
                _worldData = JsonUtility.FromJson<WorldSaveData>(json);

                // Rebuild voxel mapping from JSON (JsonUtility doesn't serialize dictionaries)
                _worldData.VoxelMapping = DeserializeVoxelMapping(json);

                // Build ID remap table
                if (!_idRemapper.BuildRemapTable(_worldData.VoxelMapping)) {
                    missingVoxels = _idRemapper.MissingVoxels;
                    Debug.LogError($"[WorldPersistence] Cannot load world '{name}': missing voxel definitions");
                    _worldData = null;
                    return false;
                }

                _currentWorldPath = worldPath;
                _chunksPath = Path.Combine(worldPath, "chunks");

                Debug.Log($"[WorldPersistence] Loaded world: {name}" +
                          (_idRemapper.NeedsRemapping ? $" (remapping {_idRemapper.RemapTable.Count} voxel IDs)" : ""));
                return true;
            }
            catch (Exception e) {
                Debug.LogError($"[WorldPersistence] Failed to load world: {e.Message}");
                _worldData = null;
                return false;
            }
        }

        /// <summary>
        /// Unloads the current world.
        /// </summary>
        public void UnloadWorld() {
            _worldData = null;
            _currentWorldPath = null;
            _chunksPath = null;
        }

        /// <summary>
        /// Saves the world metadata (world.json).
        /// </summary>
        public void SaveWorldMetadata() {
            if (_worldData == null) return;

            _worldData.LastSaved = DateTime.UtcNow.ToString("o");

            try {
                var json = SerializeWorldData(_worldData);
                var path = Path.Combine(_currentWorldPath, "world.json");
                File.WriteAllText(path, json);
            }
            catch (Exception e) {
                Debug.LogError($"[WorldPersistence] Failed to save world metadata: {e.Message}");
            }
        }

        /// <summary>
        /// Checks if a chunk file exists for the given position.
        /// </summary>
        public bool ChunkExists(Vector2Int chunkPos) {
            if (_chunksPath == null) return false;
            var path = GetChunkPath(chunkPos);
            return File.Exists(path);
        }

        /// <summary>
        /// Loads chunk data from disk.
        /// Returns null if chunk doesn't exist.
        /// </summary>
        public bool LoadChunk(Vector2Int chunkPos, VoxelData[] voxelData) {
            var path = GetChunkPath(chunkPos);

            if (!File.Exists(path)) {
                return false;
            }

            try {
                var data = File.ReadAllBytes(path);
                var remapTable = _idRemapper.NeedsRemapping ? _idRemapper.RemapTable : null;

                if (!ChunkSerializer.Deserialize(data, out var loadedPos, voxelData, remapTable)) {
                    Debug.LogError($"[WorldPersistence] Failed to deserialize chunk at {chunkPos}");
                    return false;
                }

                if (loadedPos != chunkPos) {
                    Debug.LogWarning($"[WorldPersistence] Chunk position mismatch: expected {chunkPos}, got {loadedPos}");
                }

                return true;
            }
            catch (Exception e) {
                Debug.LogError($"[WorldPersistence] Failed to load chunk {chunkPos}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Saves chunk data to disk synchronously.
        /// </summary>
        public void SaveChunk(Vector2Int chunkPos, VoxelData[] voxelData) {
            if (_chunksPath == null) return;

            try {
                var data = ChunkSerializer.Serialize(chunkPos, voxelData);
                var path = GetChunkPath(chunkPos);
                File.WriteAllBytes(path, data);
            }
            catch (Exception e) {
                Debug.LogError($"[WorldPersistence] Failed to save chunk {chunkPos}: {e.Message}");
            }
        }

        /// <summary>
        /// Saves chunk data to disk asynchronously via thread pool.
        /// </summary>
        public void SaveChunkAsync(Vector2Int chunkPos, VoxelData[] voxelData, Action onComplete = null) {
            if (_chunksPath == null) return;

            // Make a copy of voxel data for async write
            var dataCopy = new VoxelData[voxelData.Length];
            Array.Copy(voxelData, dataCopy, voxelData.Length);

            var path = GetChunkPath(chunkPos);

            GlobalThreadPool.DispatchAction(() => {
                try {
                    var serialized = ChunkSerializer.Serialize(chunkPos, dataCopy);
                    File.WriteAllBytes(path, serialized);
                }
                catch (Exception e) {
                    Debug.LogError($"[WorldPersistence] Failed to save chunk {chunkPos} async: {e.Message}");
                }
            }, onComplete);
        }

        /// <summary>
        /// Deletes a world and all its data.
        /// </summary>
        public bool DeleteWorld(string name) {
            var worldPath = Path.Combine(_worldsRoot, SanitizeWorldName(name));

            if (!Directory.Exists(worldPath)) {
                return false;
            }

            try {
                Directory.Delete(worldPath, true);
                Debug.Log($"[WorldPersistence] Deleted world: {name}");
                return true;
            }
            catch (Exception e) {
                Debug.LogError($"[WorldPersistence] Failed to delete world: {e.Message}");
                return false;
            }
        }

        private string GetChunkPath(Vector2Int chunkPos) {
            return Path.Combine(_chunksPath, $"chunk_{chunkPos.x}_{chunkPos.y}.bin");
        }

        private string SanitizeWorldName(string name) {
            // Remove invalid path characters
            foreach (var c in Path.GetInvalidFileNameChars()) {
                name = name.Replace(c, '_');
            }
            return name;
        }

        // Custom serialization for world data since JsonUtility doesn't handle dictionaries
        private string SerializeWorldData(WorldSaveData data) {
            var mappingJson = new List<string>();
            foreach (var kvp in data.VoxelMapping) {
                mappingJson.Add($"\"{kvp.Key}\": \"{kvp.Value}\"");
            }

            return $@"{{
    ""Name"": ""{data.Name}"",
    ""Seed"": {data.Seed},
    ""CreatedAt"": ""{data.CreatedAt}"",
    ""LastSaved"": ""{data.LastSaved}"",
    ""VoxelMapping"": {{
        {string.Join(",\n        ", mappingJson)}
    }}
}}";
        }

        private Dictionary<ushort, string> DeserializeVoxelMapping(string json) {
            var mapping = new Dictionary<ushort, string>();

            // Simple parser for VoxelMapping section
            var mappingStart = json.IndexOf("\"VoxelMapping\"");
            if (mappingStart < 0) return mapping;

            var braceStart = json.IndexOf('{', mappingStart);
            var braceEnd = json.IndexOf('}', braceStart);
            if (braceStart < 0 || braceEnd < 0) return mapping;

            var content = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
            var pairs = content.Split(',');

            foreach (var pair in pairs) {
                var parts = pair.Split(':');
                if (parts.Length != 2) continue;

                var keyStr = parts[0].Trim().Trim('"');
                var value = parts[1].Trim().Trim('"');

                if (ushort.TryParse(keyStr, out var key)) {
                    mapping[key] = value;
                }
            }

            return mapping;
        }
    }
}
