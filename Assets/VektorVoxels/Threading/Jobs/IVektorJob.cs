namespace VektorVoxels.Threading.Jobs {
    public interface IVektorJob {
        JobCompletionState CompletionState { get; }
        void Execute();
        void SignalCompletion(JobCompletionState completionState);
    }
}