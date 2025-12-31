using System.Runtime.InteropServices;

namespace VektorVoxels.Chunks {
    /// <summary>
    /// Height map entry for a single column. Stores the Y-level of the highest solid voxel.
    /// Burst-compatible: uses byte instead of bool for Dirty flag.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HeightData {
        public byte Value;
        /// <summary>
        /// Dirty flag: 0 = clean, non-zero = dirty.
        /// Uses byte instead of bool for Burst compatibility.
        /// </summary>
        public byte Dirty;

        public HeightData(byte value, bool dirty) {
            Value = value;
            Dirty = dirty ? (byte)1 : (byte)0;
        }

        public HeightData(byte value, byte dirty) {
            Value = value;
            Dirty = dirty;
        }

        /// <summary>
        /// Returns true if this column is marked dirty.
        /// </summary>
        public bool IsDirty => Dirty != 0;

        /// <summary>
        /// Marks this column as dirty.
        /// </summary>
        public void MarkDirty() => Dirty = 1;

        /// <summary>
        /// Marks this column as clean.
        /// </summary>
        public void MarkClean() => Dirty = 0;
    }
}
