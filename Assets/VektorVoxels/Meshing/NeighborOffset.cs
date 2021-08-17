using System;

namespace VektorVoxels.Meshing {
    // Pretty much used to clean up the neighbor data function.
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