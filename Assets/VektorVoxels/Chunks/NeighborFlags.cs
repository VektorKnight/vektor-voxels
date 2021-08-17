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
        West = 8,
        
        // Corner neighbors are separate cause only the mesher needs them.
        NorthEast = 16,
        SouthEast = 32,
        SouthWest = 64,
        NorthWest = 128
    }
}