namespace VektorVoxels.Config {
    /// <summary>
    /// Contains global configuration constants for the engine.
    /// Modifying these may affect threading, memory usage, and stability.
    /// </summary>
    public static class GlobalConstants {
        /// <summary>
        /// Some CPUs have absurd thread counts and the engine just won't scale to them very well.
        /// This sets an absolute maximum thread count for the pool to avoid wasting resources.
        /// </summary>
        public const int JOB_MAX_THREADS = 8;
        
        /// <summary>
        /// Maximum amount of time (ms) a job will wait for a lock before aborting.
        /// </summary>
        public const int JOB_LOCK_TIMEOUT_MS = 10000;
    }
}