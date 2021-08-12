namespace VoxelLighting.Chunks {
    public enum ChunkState {
        Uninitialized,    
        TerrainGeneration,      
        LightFirstPass,
        WaitingForNeighbors,
        LightFinalPass,
        Meshing,          
        Ready             
    }
}