using UnityEngine;

namespace VektorVoxels.Interaction {
    public class SelectionIndicator : MonoBehaviour {
        
        
        
        private LineRenderer _facingLine;

        private void Awake() {
            _facingLine = GetComponent<LineRenderer>();
            _facingLine.positionCount = 2;
        }
        
        public void SetPositionAndFacing(Vector3 position, Vector3 normal) {
            transform.position = position;
            
            _facingLine.SetPosition(0, transform.position + 0.5f * normal);
            _facingLine.SetPosition(1, transform.position + 1.5f * normal);
        }
    }
}