using System;

namespace VektorVoxels.Chunks {
    public readonly struct NeighborSet {
        public readonly Chunk North;
        public readonly Chunk East;
        public readonly Chunk South;
        public readonly Chunk West;
        
        public readonly Chunk NorthEast;
        public readonly Chunk SouthEast;
        public readonly Chunk SouthWest;
        public readonly Chunk NorthWest;
        
        public readonly NeighborFlags Flags;

        public NeighborSet(in Chunk[] neighbors, NeighborFlags flags) {
            if (neighbors.Length < 8) {
                throw new ArgumentException("Neighbor buffer must have a length of 8");
            }
            
            North = neighbors[0];
            East = neighbors[1];
            South = neighbors[2];
            West = neighbors[3];

            NorthEast = neighbors[4];
            SouthEast = neighbors[5];
            SouthWest = neighbors[6];
            NorthWest = neighbors[7];
            
            Flags = flags;
        }
    }
}