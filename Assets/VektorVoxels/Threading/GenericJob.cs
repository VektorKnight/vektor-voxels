using System;
using UnityEngine;

namespace VektorVoxels.Threading {
    /// <summary>
    /// Generic job wrapper that can execute any action.
    /// </summary>
    public sealed class GenericJob : PoolJob {
        private readonly Action _work;
        
        public GenericJob(Action work) : base(0) {
            _work = work ?? throw new ArgumentNullException(nameof(work));
        }

        public override void Execute() {
            try {
                _work?.Invoke();
                SignalCompletion(JobCompletionState.Completed);
            }
            catch (Exception e) {
                Debug.LogException(e);
                SignalCompletion(JobCompletionState.Aborted);
                throw;
            }
        }
    }
}