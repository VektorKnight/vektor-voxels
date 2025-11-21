using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using VektorVoxels.Threading.Jobs;

namespace VektorVoxels.Threading {
    /// <summary>
    /// Represents a single worker thread in the thread pool.
    /// Implements progressive power management: spins when busy, yields when idle, and sleeps
    /// based on cycle counts configured in ThreadConfig. This reduces CPU usage without sacrificing
    /// responsiveness when work is available.
    /// </summary>
	public sealed class WorkerThread {
        private readonly BlockingCollection<IVektorJob> _workQueue;
        private readonly ThreadConfig _config;
        private readonly Thread _thread;
        
        // Tracks idle cycles without work. Used to determine when to transition to lower-power states.
        private uint _cycleCounter;
        private ThreadStatus _status;
        // Flag for graceful shutdown. Worker completes current job before checking and exiting.
        private bool _shuttingDown;
        private Exception _lastException;
        
        /// <summary>
        /// Configuration struct used by this thread.
        /// </summary>
        public ThreadConfig Config => _config;
        
        /// <summary>
        /// Current status of the thread.
        /// If the status is 'Error' more info can be obtained from the 'LastException' property.
        /// </summary>
        public ThreadStatus Status => _status;
        
        /// <summary>
        /// The last exception the thread encountered.
        /// </summary>
        public Exception LastException => _lastException;

        public WorkerThread(BlockingCollection<IVektorJob> workQueue, ThreadConfig config) {
            _workQueue = workQueue;
            _config = config;

            _thread = new Thread(WorkLoop) { IsBackground = true };
            _thread.Start();

            _cycleCounter = 0;
            _status = ThreadStatus.Spinning;
        }
        
        /// <summary>
        /// Tells the worker to shutdown at the next cycle.
        /// </summary>
        public void Shutdown() {
            _shuttingDown = true;
        }
        
        /// <summary>
        /// Immediately aborts the workers thread.
        /// </summary>
        public void Abort() {
            _status = ThreadStatus.Offline;
            _thread.Abort();
        }

        private void WorkLoop() {
            while (true) {
                // Shut down if there is no more work being added.
                if (_shuttingDown || _workQueue.IsCompleted) {
                    // Debug.Log("Thread pool worker shutting down.");
                    _status = ThreadStatus.Offline;
                    return;
                }
                
                // Check for work.
                var hasWork = _workQueue.TryTake(out var job);

                if (hasWork && job == null) {
                    Debug.LogError("Unexpected null job on pool!");
                    continue;
                }
                
                // Try to invoke the job and reset the cycle counter and thread status.
                if (hasWork) {
                    try {
                        job.Execute();

                        if (job.CompletionState == JobCompletionState.None) {
                            job.SignalCompletion(JobCompletionState.Completed);
                        }
                    }
                    catch (Exception e) {
                        Debug.LogError($"Worker thread has encountered an exception while processing a job.");
                        Debug.LogException(e);
                        _lastException = e;
                        
                        if (job.CompletionState == JobCompletionState.None) {
                            job.SignalCompletion(JobCompletionState.Aborted);
                        }
                    }
                    
                    _cycleCounter = 0;
                    _status = ThreadStatus.Spinning;
                    continue;
                }
                
                // Increment cycle counter if no work found.
                _cycleCounter++;
                
                // Branch based on current status.
                switch (_status) {
                    case ThreadStatus.Spinning:
                        if (_config.SpinCycles != -1 && _cycleCounter >= _config.SpinCycles) {
                            _cycleCounter = 0;
                            _status = ThreadStatus.Yielding;
                        }
                        break;
                    case ThreadStatus.Yielding:
                        if (_config.YieldCycles != -1 && _cycleCounter >= _config.YieldCycles) {
                            _cycleCounter = 0;
                            _status = ThreadStatus.Napping;
                        }
                        
                        Thread.Yield();
                        break;
                    case ThreadStatus.Napping:
                        if (_config.NapCycles != -1 && _cycleCounter >= _config.NapCycles) {
                            _cycleCounter = 0;
                            _status = ThreadStatus.Sleeping;
                        }
                        
                        Thread.Sleep(_config.NapInterval);
                        break;
                    case ThreadStatus.Sleeping:
                        Thread.Sleep(_config.SleepInterval);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}