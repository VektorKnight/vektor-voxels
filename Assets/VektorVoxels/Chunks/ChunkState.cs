namespace VektorVoxels.Chunks {
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