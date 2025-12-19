using System;

namespace VektorVoxels.Meshing {
    /// <summary>
    /// Cardinal and diagonal neighbor directions as a flags enum.
    /// Used for neighbor lookup indexing and bitmask operations.
    /// </summary>
    [Flags]
    public enum NeighborOffset {
        None = 0,
        
        North = 1,
        East = 2,
        South = 4,
        West = 8,
        
        NorthEast = North | East,
        SouthEast = South | East,
        SouthWest = South | West,
        NorthWest = North | West,
    }
}