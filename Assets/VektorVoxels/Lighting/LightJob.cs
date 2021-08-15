using System;
using Unity.Jobs;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Threading;

namespace VektorVoxels.Lighting {
    public readonly struct LightJob : IPoolJob {
        private readonly long _id;
        private readonly Chunk _chunk;
        private readonly NeighborSet _neighbors;
        private readonly LightMapper _lightMapper;
        private readonly LightPass _pass;
        private readonly Action _callBack;

        public long Id => _id;

        public LightJob(long id, Chunk chunk, NeighborSet neighbors, LightMapper lightMapper, LightPass pass, Action callBack) {
            _id = id;
            _chunk = chunk;
            _neighbors = neighbors;
            _lightMapper = lightMapper;
            _pass = pass;
            _callBack = callBack;
        }

        public void Execute() {
            // Abort the job if the chunk's counter is != the assigned id.
            if (_chunk.JobCounter != _id) {
                Debug.LogWarning($"Aborting orphaned job with ID: {_id}");
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
            
            GlobalThreadPool.QueueOnMain(_callBack);
            
            _chunk.ThreadLock.ExitWriteLock();
        }
    }
}