using System.Threading;

namespace VektorVoxels.Threading.Jobs {
    /// <summary>
    /// A test/example job that just waits for some duration in milliseconds.
    /// </summary>
    public sealed class TestJob : PoolJob {
        private readonly int _milliseconds;
        
        public TestJob(long id, int milliseconds) : base(id) {
            _milliseconds = milliseconds;
        }
        
        // Do your work here.
        public override void Execute() {
            // Do some work.
            Thread.Sleep(_milliseconds);

            // Signal completion when we're done.
            // This should always be called with the proper state even if the job fails.
            SignalCompletion(JobCompletionState.Completed);
        }
    }
}