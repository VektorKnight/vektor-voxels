using System;

namespace VektorVoxels.Chunks {
    /// <summary>
    /// Used for determining which neighbors are within bounds for meshing/lightmapping.
    /// </summary>
    [Flags]
    public enum NeighborFlags {
        None = 0,
        North = 1,
        East = 2,
        South = 4,
        West = 8
    }
}