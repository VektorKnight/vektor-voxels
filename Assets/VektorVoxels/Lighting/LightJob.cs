﻿using System;
using System.Diagnostics;
using Unity.Jobs;
using UnityEngine;
using VektorVoxels.Config;
using VektorVoxels.Threading;
using VektorVoxels.Threading.Jobs;
using VektorVoxels.World.Chunks;
using Debug = UnityEngine.Debug;

namespace VektorVoxels.Lighting {
    public class LightJob : VektorJob {
        private readonly long _id;
        private readonly Chunk _chunk;
        private readonly NeighborSet _neighbors;
        private readonly LightPass _pass;
        private readonly Action _callBack;

        public LightJob(long id, Chunk chunk, NeighborSet neighbors, LightPass pass, Action callBack) {
            _id = id;
            _chunk = chunk;
            _neighbors = neighbors;
            _pass = pass;
            _callBack = callBack;
        }

        public override void Execute() {
            // Abort the job if the chunk's counter is != the assigned id.
            if (_chunk.JobCounter != _id) {
                Debug.LogWarning($"Aborting orphaned job with ID: {_id}");
                SignalCompletion(JobCompletionState.Aborted);
                return;
            }
            
            if (_chunk.ThreadLock.TryEnterWriteLock(GlobalConstants.JOB_LOCK_TIMEOUT_MS)) {
                var lightMapper = LightMapper.LocalThreadInstance;
                switch (_pass) {
                    case LightPass.None:
                        break;
                    case LightPass.First:
                        lightMapper.InitializeSunLightFirstPass(_chunk);
                        lightMapper.InitializeBlockLightFirstPass(_chunk);
                        lightMapper.PropagateSunLight(_chunk);
                        lightMapper.PropagateBlockLight(_chunk);
                        break;
                    case LightPass.Second:
                        lightMapper.InitializeNeighborLightPass(_chunk, _neighbors);
                        lightMapper.PropagateSunLight(_chunk);
                        lightMapper.PropagateBlockLight(_chunk);
                        break;
                    case LightPass.Third:
                        lightMapper.InitializeNeighborLightPass(_chunk, _neighbors);
                        lightMapper.PropagateSunLight(_chunk);
                        lightMapper.PropagateBlockLight(_chunk);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                
                _chunk.ThreadLock.ExitWriteLock();

                // Signal completion.
                SignalCompletion(JobCompletionState.Completed);
            
                // Invoke callback on main if specified.
                if (_callBack != null) {
                    GlobalThreadPool.DispatchOnMain(_callBack, QueueType.Default);
                }
            }
            else {
                Debug.LogError("Light job failed to acquire a write lock within the specified timeout!\n" +
                               "Application will exit.");
                
                SignalCompletion(JobCompletionState.Aborted);
                
                // This honestly gets us into an invalid state that cannot be recovered from
                // so the application will just exit by default.
                _context.Post((state) => {
                    if (Application.isEditor) {
                        Debug.Break();
                    }
                    else {
                        Application.Quit();
                    }
                }, null);
            }
        }
    }
}