using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Lighting;
using VektorVoxels.Meshing;
using VektorVoxels.Threading;
using VektorVoxels.Voxels;

namespace VektorVoxels.Tests {
    [RequireComponent(typeof(MeshFilter))]
    public class TestCube : MonoBehaviour {
        public Vector3Int Dimensions = new Vector3Int(32, 32, 32);

        private LightMapper _lightMapper;
        private CubicMesher _mesher;
        
        // Voxel grid data.
        private VoxelData[] _voxelData;
        private HeightData[] _heightMap;
        private Color16[] _blockLight;
        private Color16[] _sunLight;

        private Mesh _mesh;
        
        private void Start() {
            _lightMapper = new LightMapper();
            _mesher = new CubicMesher();
            _voxelData = new VoxelData[Dimensions.x * Dimensions.y * Dimensions.z];
            _heightMap = new HeightData[Dimensions.x * Dimensions.z];
            _blockLight = new Color16[Dimensions.x * Dimensions.y * Dimensions.z];
            _sunLight = new Color16[Dimensions.x * Dimensions.y * Dimensions.z];
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.Space)) {
                GlobalThreadPool.QueueWorkItem(() => {
                    for (var z = 0; z < Dimensions.z; z++) {
                        for (var x = 0; x < Dimensions.x; x++) {
                            _voxelData[VoxelUtility.VoxelIndex(x, 0, z, in Dimensions)] = VoxelTable.ById(1).GetDataInstance();
                            _heightMap[VoxelUtility.HeightIndex(x, z, Dimensions.x)] = new HeightData(1, true);
                        }
                    }
                    
                    // Yikes
                    _voxelData[VoxelUtility.VoxelIndex(0, 2, 0, in Dimensions)] = VoxelTable.ById(11).GetDataInstance();
                    
                    _voxelData[VoxelUtility.VoxelIndex(0, 1, 2, in Dimensions)] = VoxelTable.ById(10).GetDataInstance();
                    _voxelData[VoxelUtility.VoxelIndex(1, 1, 2, in Dimensions)] = VoxelTable.ById(10).GetDataInstance();
                    _voxelData[VoxelUtility.VoxelIndex(2, 1, 2, in Dimensions)] = VoxelTable.ById(5).GetDataInstance();
                    _voxelData[VoxelUtility.VoxelIndex(2, 1, 0, in Dimensions)] = VoxelTable.ById(8).GetDataInstance();
                    _voxelData[VoxelUtility.VoxelIndex(2, 1, 1, in Dimensions)] = VoxelTable.ById(8).GetDataInstance();
                    
                    _voxelData[VoxelUtility.VoxelIndex(0, 2, 2, in Dimensions)] = VoxelTable.ById(10).GetDataInstance();
                    _voxelData[VoxelUtility.VoxelIndex(1, 2, 2, in Dimensions)] = VoxelTable.ById(10).GetDataInstance();
                    _voxelData[VoxelUtility.VoxelIndex(2, 2, 2, in Dimensions)] = VoxelTable.ById(5).GetDataInstance();
                    _voxelData[VoxelUtility.VoxelIndex(2, 2, 0, in Dimensions)] = VoxelTable.ById(8).GetDataInstance();
                    _voxelData[VoxelUtility.VoxelIndex(2, 2, 1, in Dimensions)] = VoxelTable.ById(8).GetDataInstance();
                    
                    _voxelData[VoxelUtility.VoxelIndex(0, 3, 2, in Dimensions)] = VoxelTable.ById(10).GetDataInstance();
                    _voxelData[VoxelUtility.VoxelIndex(1, 3, 2, in Dimensions)] = VoxelTable.ById(10).GetDataInstance();
                    _voxelData[VoxelUtility.VoxelIndex(2, 3, 2, in Dimensions)] = VoxelTable.ById(5).GetDataInstance();
                    _voxelData[VoxelUtility.VoxelIndex(2, 3, 0, in Dimensions)] = VoxelTable.ById(8).GetDataInstance();
                    _voxelData[VoxelUtility.VoxelIndex(2, 3, 1, in Dimensions)] = VoxelTable.ById(8).GetDataInstance();
                    
                    for (var z = 0; z < 3; z++) {
                        for (var x = 0; x < 3; x++) {
                            _voxelData[VoxelUtility.VoxelIndex(x, 4, z, in Dimensions)] = VoxelTable.ById(5).GetDataInstance();
                            _heightMap[VoxelUtility.HeightIndex(x, z, Dimensions.x)] = new HeightData(5, true);
                        }
                    }
                    
                    //_lightMapper.InitializeSunLightFirstPass(_voxelData, _heightMap, _sunLight, Dimensions);
                    //_lightMapper.InitializeBlockLightFirstPass(_voxelData, Dimensions);
                    
                    //_lightMapper.PropagateSunLight(_voxelData, _sunLight, Dimensions);
                    //_lightMapper.PropagateBlockLight(_voxelData, _blockLight, Dimensions);
                    //_mesher.GenerateMeshData(_voxelData, _blockLight, _sunLight, Dimensions);
                    
                    // Set mesh data on main thread once previous routines have completed.
                    GlobalThreadPool.QueueOnMain(() => {
                        _mesher.SetMeshData(ref _mesh);
                        GetComponent<MeshFilter>().mesh = _mesh;
                    });
                });
            }
        }
    }
}