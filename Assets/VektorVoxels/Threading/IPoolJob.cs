namespace VektorVoxels.Threading {
    /// <summary>
    /// Any job that is meant to be executed by the threadpool.
    /// </summary>
    public interface IPoolJob {
        /// <summary>
        /// Identifier, usually a counter.
        /// </summary>
        long Id { get; }
        
        /// <summary>
        /// Do your main work here.
        /// Anything used within this method should be thread-safe.
        /// </summary>
        void Execute();
    }
}