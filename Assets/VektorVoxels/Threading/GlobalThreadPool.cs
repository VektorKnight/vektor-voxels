using System;
using System.Collections.Concurrent;
using UnityEngine;
using VektorVoxels.Threading.Jobs;

namespace VektorVoxels.Threading {
    /// <summary>
    /// Provides an instance of a thread-pool as a singleton instance in a Unity context.
    /// Also provides queues for pushing callbacks to the Unity main thread.
    /// Jobs don't necessarily have to use the queues for main-thread callbacks.
    /// A sync context could be used instead.
    /// </summary>
    public class GlobalThreadPool : MonoBehaviour {
        public static GlobalThreadPool Instance { get; private set; }
        public static uint ThreadCount { get; private set; }

        private static int _throttledUpdatesPerTick;
        
        /// <summary>
        /// Maximum number of invocations executed on the main thread from the throttled queue per tick.
        /// Tick rate is tied to Unity's FixedUpdate which has been adjusted to run at 60 FPS.
        /// </summary>
        public static int ThrottledUpdatesPerTick {
            get => _throttledUpdatesPerTick;
            set => _throttledUpdatesPerTick = Mathf.Clamp(value, 1, int.MaxValue);
        }

        private bool _initialized;
        private ThreadPool _threadPool;
        private ConcurrentQueue<Action> _mainQueue;
        private ConcurrentQueue<Action> _mainQueueThrottled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeSingleton() {
            if (Instance != null) {
                Debug.LogWarning("Global thread pool instance detected in scene. \n" +
                                 "Please remove the instance from the scene for proper initialization.");
                return;
            }

            Instance = new GameObject("[Global Thread Pool]").AddComponent<GlobalThreadPool>();
            Instance.Initialize();
            DontDestroyOnLoad(Instance);
        }

        private void Initialize() {
            // Allocate 3/4 of the system's thread count with a minimum of 2.
            ThreadCount = (uint)Mathf.Max(SystemInfo.processorCount * 3 / 4, 2);
            _threadPool = new ThreadPool(ThreadCount, ThreadConfig.Default());
            _mainQueue = new ConcurrentQueue<Action>();
            _mainQueueThrottled = new ConcurrentQueue<Action>();
            _initialized = true;
            
            Debug.Log($"[Global Thread Pool] Initialized with {ThreadCount} threads.");
        }
        
        /// <summary>
        /// Immediately queues the provided job for execution on the pool.
        /// </summary>
        public static PoolJob DispatchJob(PoolJob item) {
            Instance._threadPool.EnqueueWorkItem(item);
            return item;
        }
        
        /// <summary>
        /// Queues a given action to be executed on the main thread.
        /// </summary>
        public static void DispatchOnMain(Action a, QueueType queue) {
            switch (queue) {
                case QueueType.Normal:
                    Instance._mainQueue.Enqueue(a);
                    break;
                case QueueType.Throttled:
                    Instance._mainQueueThrottled.Enqueue(a);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(queue), queue, null);
            }
        }

        private void Update() {
            if (!_initialized) return;
            
            // Process the regular queue.
            while (_mainQueue.TryDequeue(out var a)) {
                a.Invoke();
            }
        }

        private void FixedUpdate() {
            // Process the throttled queue.
            var count = _throttledUpdatesPerTick;
            while (_mainQueueThrottled.TryDequeue(out var a)) {
                a.Invoke();
                count--;
                
                if (count <= 0) {
                    break;
                }
            }
        }
    }
}