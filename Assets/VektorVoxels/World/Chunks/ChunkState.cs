namespace VektorVoxels.World.Chunks {
    public enum ChunkState {
        Uninitialized,
        TerrainGeneration,
        Lighting,
        WaitingForNeighbors,
        Meshing,          
        Active,
        Inactive
    }
}