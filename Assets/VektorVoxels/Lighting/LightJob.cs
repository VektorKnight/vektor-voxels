using System;
using Unity.Jobs;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Threading;

namespace VektorVoxels.Lighting {
    public class LightJob : PoolJob {
        private readonly Chunk _chunk;
        private readonly NeighborSet _neighbors;
        private readonly LightMapper _lightMapper;
        private readonly LightPass _pass;
        private readonly Action _callBack;

        public LightJob(long id, Chunk chunk, NeighborSet neighbors, LightMapper lightMapper, LightPass pass, Action callBack) : base(id) {
            _chunk = chunk;
            _neighbors = neighbors;
            _lightMapper = lightMapper;
            _pass = pass;
            _callBack = callBack;
        }

        public override void Execute() {
            // Abort the job if the chunk's counter is != the assigned id.
            if (_chunk.JobCounter != Id) {
                Debug.LogWarning($"Aborting orphaned job with ID: {Id}");
                SignalCompletion(JobCompletionState.Aborted);
                return;
            }
            
            _chunk.ThreadLock.EnterWriteLock();
            switch (_pass) {
                case LightPass.None:
                    break;
                case LightPass.First:
                    _lightMapper.InitializeSunLightFirstPass(_chunk);
                    _lightMapper.PropagateSunLight(_chunk);
                    _lightMapper.InitializeBlockLightFirstPass(_chunk);
                    _lightMapper.PropagateBlockLight(_chunk);
                    break;
                case LightPass.Second:
                    _lightMapper.InitializeNeighborLightPass(_chunk, _neighbors);
                    _lightMapper.PropagateSunLight(_chunk);
                    _lightMapper.PropagateBlockLight(_chunk);
                    break;
                case LightPass.Third:
                    _lightMapper.InitializeNeighborLightPass(_chunk, _neighbors);
                    _lightMapper.PropagateSunLight(_chunk);
                    _lightMapper.PropagateBlockLight(_chunk);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            _chunk.ThreadLock.ExitWriteLock();
            
            // Signal completion.
            SignalCompletion(JobCompletionState.Completed);
            
            // Invoke callback on main if specified.
            if (_callBack != null) {
                GlobalThreadPool.QueueOnMain(_callBack);
            }
        }
    }
}