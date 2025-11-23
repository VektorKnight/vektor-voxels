using System;

namespace VektorVoxels.Threading.Jobs {
    /// <summary>
    /// Simple job that executes an action on the thread pool.
    /// </summary>
    public class ActionJob : VektorJob {
        private readonly Action _work;
        private readonly Action _onComplete;

        public ActionJob(Action work, Action onComplete = null) {
            _work = work;
            _onComplete = onComplete;
        }

        public override void Execute() {
            _work?.Invoke();
            if (_onComplete != null) {
                DispatchToMain(_onComplete, QueueType.Default);
            }
        }
    }
}
