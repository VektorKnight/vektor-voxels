using UnityEngine;

namespace VektorVoxels.Voxels.Foliage {
    public class GrassMesh : ICustomMesh {
        private readonly Vector3[] _vertices;
        private readonly Vector3[] _normals;
        private readonly Vector2[] _texCoords;
        private readonly int[] _triangles;
        private readonly float _height;
        private readonly Rect _textureRect;

        public Vector3[] Vertices => _vertices;
        public Vector3[] Normals => _normals;
        public Vector2[] TexCoords => _texCoords;
        public int[] Triangles => _triangles;
        public float Height => _height;

        public GrassMesh(float height, Rect textureRect) {
            _height = height;
            _textureRect = textureRect;
        }
    }
}