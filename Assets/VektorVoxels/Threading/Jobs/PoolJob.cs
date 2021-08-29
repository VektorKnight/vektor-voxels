using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace VektorVoxels.Threading.Jobs {
    /// <summary>
    /// Any job that is meant to be executed by the threadpool.
    /// Compatible with async/await.
    /// </summary>
    public abstract class PoolJob : INotifyCompletion {
        /// <summary>
        /// Context in which the job was constructed.
        /// </summary>
        protected readonly SynchronizationContext _context;
        private Action _continuation;
        
        /// <summary>
        /// Identifier, usually a counter.
        /// </summary>
        public long Id { get; }

        /// <summary>
        /// Completion state of the job.
        /// </summary>
        public JobCompletionState CompletionState { get; protected set; }
        
        protected PoolJob(long id) {
            _context = SynchronizationContext.Current;
            
            Id = id;
            CompletionState = JobCompletionState.None;
        }

        /// <summary>
        /// Do your main work here.
        /// Anything used within this method should be thread-safe.
        /// </summary>
        public virtual void Execute() { }
        
        /// <summary>
        /// Signals that the job has completed.
        /// You must call this when your job completes to allow await to work properly.
        /// </summary>
        protected void SignalCompletion(JobCompletionState completionState) {
            CompletionState = completionState;
            var continuation = Interlocked.Exchange(ref _continuation, null);
            if (continuation != null) {
                _context.Post(state => {
                    ((Action)state)();
                }, continuation);
            }
        }
        
        public bool IsCompleted => CompletionState != JobCompletionState.None;
        
        public JobCompletionState GetResult() {
            return CompletionState;
        }
        
        public void OnCompleted(Action continuation) {
            Volatile.Write(ref _continuation, continuation);
        }

        public PoolJob GetAwaiter() => this;
    }
}