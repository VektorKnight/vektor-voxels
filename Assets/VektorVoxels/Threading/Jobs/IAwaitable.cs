using System;
using System.Runtime.CompilerServices;

namespace VektorVoxels.Threading.Jobs {
    public interface IAwaitable : INotifyCompletion {
        bool IsCompleted { get; }
        void GetResult();
        public IAwaitable GetAwaiter();
    }
    
    public interface IAwaitable<out T> : INotifyCompletion {
        bool IsCompleted { get; }
        T GetResult();
        public IAwaitable<T> GetAwaiter();
    }
}