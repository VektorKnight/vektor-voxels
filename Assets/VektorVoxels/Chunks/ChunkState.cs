namespace VektorVoxels.Chunks {
    public enum ChunkState {
        Uninitialized,    
        TerrainGeneration,
        Lighting,
        WaitingForNeighbors,
        Meshing,          
        Ready,
        Inactive
    }
}