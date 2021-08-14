using System;
using Unity.Jobs;
using VektorVoxels.Chunks;
using VektorVoxels.Threading;

namespace VektorVoxels.Lighting {
    public class LightJob : IWorkItem {
        private readonly Chunk _chunk;
        private NeighborSet _neighbors;
        private readonly LightPass _pass;
        private readonly LightMapper _lightMapper;
        private readonly Action _callBack;

        public NeighborSet Neighbors {
            get => _neighbors;
            set => _neighbors = value;
        }

        public LightJob(Chunk chunk, NeighborSet neighbors, LightPass pass, LightMapper lightMapper, Action callBack) {
            _chunk = chunk;
            _neighbors = neighbors;
            _pass = pass;
            _lightMapper = lightMapper;
            _callBack = callBack;
        }

        public void Execute() {
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