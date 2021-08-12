using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace VektorVoxels.Threading {
    public class GlobalThreadPool : MonoBehaviour {
        public static GlobalThreadPool Instance { get; private set; }

        private bool _initialized;
        private ThreadPool _threadPool;
        private ConcurrentQueue<Action> _mainQueue;

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
            var threadCount = SystemInfo.processorCount * 3 / 4;
            _threadPool = new ThreadPool((uint)threadCount, ThreadConfig.Default());
            _mainQueue = new ConcurrentQueue<Action>();
            _initialized = true;
            
            Debug.Log($"[Global Thread Pool] Initialized with {threadCount} threads.");
        }

        public void EnqueueWorkItem(Action workItem) {
            _threadPool.EnqueueWorkItem(workItem);
        }

        public void QueueOnMain(Action a) {
            _mainQueue.Enqueue(a);
        }

        private void Update() {
            if (!_initialized) return;

            while (_mainQueue.TryDequeue(out var a)) {
                a.Invoke();
            }
        }
    }
}