using UnityEngine;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>Moves the corpse-only cargo collider in the physics step.</summary>
    public sealed class MemoryRunBodyContactFollower : MonoBehaviour
    {
        private const float WrapDistance = 2f;
        [SerializeField] private Transform target;
        private Rigidbody body;
        private void Awake() => body = GetComponent<Rigidbody>();
        private void FixedUpdate()
        {
            if (target == null || body == null) return;
            // Cargo wraps to the beginning of the conveyor; never sweep through the entire line.
            if ((target.position - body.position).sqrMagnitude > WrapDistance * WrapDistance)
            {
                body.position = target.position;
                body.rotation = target.rotation;
            }
            else
            {
                body.MovePosition(target.position);
                body.MoveRotation(target.rotation);
            }
        }
    }
}
