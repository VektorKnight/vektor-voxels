using System.Collections.Generic;
using VektorVoxels.Voxels;

namespace VektorVoxels.Persistence {
    /// <summary>
    /// Handles remapping voxel IDs between saved worlds and current voxel definitions.
    /// </summary>
    public class VoxelIdRemapper {
        private readonly Dictionary<ushort, ushort> _remapTable;
        private readonly List<string> _missingVoxels;

        public Dictionary<ushort, ushort> RemapTable => _remapTable;
        public List<string> MissingVoxels => _missingVoxels;
        public bool HasMissingVoxels => _missingVoxels.Count > 0;
        public bool NeedsRemapping => _remapTable.Count > 0;

        public VoxelIdRemapper() {
            _remapTable = new Dictionary<ushort, ushort>();
            _missingVoxels = new List<string>();
        }

        /// <summary>
        /// Builds the remap table from saved voxel mapping to current definitions.
        /// Returns false if there are missing voxels.
        /// </summary>
        public bool BuildRemapTable(Dictionary<ushort, string> savedMapping) {
            _remapTable.Clear();
            _missingVoxels.Clear();

            // Build current name-to-ID lookup
            var currentNameToId = new Dictionary<string, ushort>();
            var voxels = VoxelTable.Voxels;
            for (var i = 0; i < voxels.Length; i++) {
                currentNameToId[voxels[i].InternalName] = (ushort)(i + 1);
            }

            // Check each saved mapping
            foreach (var kvp in savedMapping) {
                var savedId = kvp.Key;
                var internalName = kvp.Value;

                if (!currentNameToId.TryGetValue(internalName, out var currentId)) {
                    _missingVoxels.Add($"{internalName} (saved ID: {savedId})");
                    continue;
                }

                // Only add to remap table if IDs differ
                if (savedId != currentId) {
                    _remapTable[savedId] = currentId;
                }
            }

            return !HasMissingVoxels;
        }

        /// <summary>
        /// Builds a voxel mapping from current definitions for saving.
        /// </summary>
        public static Dictionary<ushort, string> BuildCurrentMapping() {
            var mapping = new Dictionary<ushort, string>();
            var voxels = VoxelTable.Voxels;

            for (var i = 0; i < voxels.Length; i++) {
                mapping[(ushort)(i + 1)] = voxels[i].InternalName;
            }

            return mapping;
        }
    }
}
