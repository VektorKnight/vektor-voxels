namespace VektorVoxels.Chunks {
    /// <summary>
    /// Chunk lifecycle state machine. Transitions flow:
    /// Uninitialized -> TerrainGeneration -> Lighting -> WaitingForNeighbors -> Meshing -> Ready.
    /// WaitingForNeighbors gates progression until all neighbors complete their lighting pass.
    /// Chunks transition to Inactive when out of view, preserving data for quick reactivation.
    /// </summary>
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