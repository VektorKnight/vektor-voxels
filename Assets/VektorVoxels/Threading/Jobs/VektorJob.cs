using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace VektorVoxels.Threading.Jobs {
    /// <summary>
    /// Generic job implementation with async/await support.
    /// Captures the synchronization context of the constructing thread to ensure continuations
    /// execute on the correct thread (typically Unity main thread).
    /// Implements IAwaitable for use with await in async contexts.
    /// </summary>
    public abstract class VektorJob<T> : IVektorJob, IAwaitable<T> {
        /// <summary>
        /// Synchronization context of the thread that created this job.
        /// Used to post completions back to the original context (usually Unity main thread).
        /// </summary>
        protected readonly SynchronizationContext _context;
        // Continuation action invoked when job completes. Set by OnCompleted(), posted via SignalCompletion().
        private Action _continuation;
        private T _result;

        /// <summary>
        /// Completion state of the job.
        /// </summary>
        public JobCompletionState CompletionState { get; protected set; }
        
        /// <summary>
        /// Create a new VektorJob instance.
        /// The context of the constructing thread is stored for async/await.
        /// Make sure you construct your job on the same thread that will await the result.
        /// </summary>
        protected VektorJob() {
            _context = SynchronizationContext.Current;
            _continuation = null;
            _result = default;
            CompletionState = JobCompletionState.None;
        }

        /// <summary>
        /// Do your main work here.
        /// Anything used within this method should be thread-safe.
        /// </summary>
        public abstract void Execute();
        
        /// <summary>
        /// Sets the result of the job.
        /// </summary>
        protected void SetResult(T result) {
            _result = result;
        }
        
        /// <summary>
        /// Signals that the job has completed.
        /// The thread-pool will call this automatically with a state of "Completed" unless you call it explicitly.
        /// </summary>
        public void SignalCompletion(JobCompletionState completionState) {
            if (completionState == JobCompletionState.None) {
                Debug.LogError("Signal completion called with a state of 'None'!");
                return;
            }
            
            if (CompletionState != JobCompletionState.None) {
                Debug.LogError("Signal completion called multiple times in job!");
                return;
            }
            
            CompletionState = completionState;
            var continuation = Interlocked.Exchange(ref _continuation, null);
            if (continuation != null) {
                _context.Post(state => {
                    ((Action)state)();
                }, continuation);
            }
        }
        
        /// <summary>
        /// Dispatches the job to be executed by the thread-pool.
        /// </summary>
        public VektorJob<T> Dispatch() => GlobalThreadPool.DispatchJob(this);
        
        /// <summary>
        /// Dispatches a given action to be executed on the context which created this job.
        /// </summary>
        /// <param name="a"></param>
        protected void DispatchToContext(Action a) {
            _context.Post(state => {
                a?.Invoke();
            }, null);    
        }
        
        /// <summary>
        /// Dispatches a given action to be executed on the Unity main thread.
        /// </summary>
        protected void DispatchToMain(Action a, QueueType queueType) {
            GlobalThreadPool.DispatchOnMain(a, queueType);
        }
        
        // Custom awaiter pattern.
        public bool IsCompleted => CompletionState != JobCompletionState.None;
        public virtual T GetResult() => _result;
        public void OnCompleted(Action continuation) => Volatile.Write(ref _continuation, continuation);
        public IAwaitable<T> GetAwaiter() => this;
        
        /// <summary>
        /// Blocks the caller until all specified jobs have completed.
        /// WARNING: Busy-wait implementation. Do not call from main thread as it will freeze the game.
        /// Prefer async/await with continuations for non-blocking waits.
        /// </summary>
        public static void WhenAll(IEnumerable<IVektorJob> jobs) {
            var complete = false;
            while (!complete) {
                complete = true;
                foreach (var job in jobs) {
                    if (job.CompletionState != JobCompletionState.None) continue;
                    complete = false;
                    break;
                }
            }
        }
    }
    
    /// <summary>
    /// Basic implementation of a VektorJob who's result is just the completion state.
    /// </summary>
    public abstract class VektorJob : VektorJob<JobCompletionState> {
        public override JobCompletionState GetResult() => CompletionState;
    }
}