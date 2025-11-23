using System;
using System.Collections.Generic;

namespace VektorVoxels.Persistence {
    /// <summary>
    /// Serializable world metadata stored in world.json.
    /// </summary>
    [Serializable]
    public class WorldSaveData {
        public string Name;
        public int Seed;
        public string CreatedAt;
        public string LastSaved;
        public Dictionary<ushort, string> VoxelMapping;

        public WorldSaveData() {
            VoxelMapping = new Dictionary<ushort, string>();
        }

        public WorldSaveData(string name, int seed) {
            Name = name;
            Seed = seed;
            CreatedAt = DateTime.UtcNow.ToString("o");
            LastSaved = CreatedAt;
            VoxelMapping = new Dictionary<ushort, string>();
        }
    }
}
