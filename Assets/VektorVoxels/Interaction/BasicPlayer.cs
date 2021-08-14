using UnityEngine;

namespace VektorVoxels.Interaction {
    [RequireComponent(typeof(CharacterController))]
    public class BasicPlayer : MonoBehaviour {
        [Header("Movement")] 
        [SerializeField] private float _moveSpeed = 5f;

    }
}