namespace VektorVoxels.Chunks {
    public enum ChunkState {
        Uninitialized,    
        TerrainGeneration,      
        LightFirstPass,
        WaitingForNeighbors,
        LightSecondPass,
        Meshing,          
        Ready             
    }
}