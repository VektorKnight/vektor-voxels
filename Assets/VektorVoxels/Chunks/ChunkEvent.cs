namespace VektorVoxels.Chunks {
    /// <summary>
    /// Events queued on chunks, processed during OnTick() when no jobs are active.
    /// Unload: transitions to Inactive and disables rendering.
    /// Reload: re-queues lighting and meshing jobs for dirty chunks.
    /// ApplyEdits: reserved for future voxel edit batching.
    /// </summary>
    public enum ChunkEvent {
        Unload,
        Reload,
        ApplyEdits,
    }
}