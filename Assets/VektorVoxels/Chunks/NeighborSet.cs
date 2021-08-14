namespace VektorVoxels.Chunks {
    public readonly struct NeighborSet {
        public readonly Chunk North;
        public readonly Chunk East;
        public readonly Chunk South;
        public readonly Chunk West;
        public readonly NeighborFlags Flags;

        public NeighborSet(Chunk north, Chunk east, Chunk south, Chunk west, NeighborFlags flags) {
            North = north;
            East = east;
            South = south;
            West = west;
            Flags = flags;
        }

        public NeighborSet(in Chunk[] neighbors, NeighborFlags flags) {
            North = neighbors[0];
            East = neighbors[1];
            South = neighbors[2];
            West = neighbors[3];
            Flags = flags;
        }
    }
}