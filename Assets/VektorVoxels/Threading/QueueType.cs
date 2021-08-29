namespace VektorVoxels.Threading {
    /// <summary>
    /// Queue type used by the thread-pool to handle updates pushed to main.
    /// </summary>
    public enum QueueType {
        Normal,
        Throttled
    }
}