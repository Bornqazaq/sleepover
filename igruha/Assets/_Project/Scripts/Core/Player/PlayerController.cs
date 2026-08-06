using UnityEngine;
using UnityEngine.InputSystem;

namespace Igruha.Core.Player
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference jumpAction;
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float jumpImpulse = 5f;
        [SerializeField] private float groundCheckDistance = 0.2f;
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private float rotationSpeed = 720f;
        [SerializeField] private Animator animator;

        private static readonly int SpeedParameterHash = Animator.StringToHash("Speed");
        private static readonly int JumpParameterHash = Animator.StringToHash("Jump");

        private Rigidbody rb;
        private CapsuleCollider capsuleCollider;
        private Vector2 moveInput;
        private bool jumpRequested;
        private Quaternion targetRotation;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsuleCollider = GetComponent<CapsuleCollider>();
            targetRotation = rb.rotation;
        }

        private void OnEnable()
        {
            moveAction.action.Enable();
            jumpAction.action.Enable();
        }

        private void OnDisable()
        {
            moveAction.action.Disable();
            jumpAction.action.Disable();
        }

        private void Update()
        {
            moveInput = moveAction.action.ReadValue<Vector2>();
            jumpRequested |= jumpAction.action.WasPerformedThisFrame();

            animator.SetFloat(SpeedParameterHash, moveInput.magnitude);
        }

        private void FixedUpdate()
        {
            Vector3 moveDirection = new Vector3(moveInput.x, 0f, moveInput.y);
            Vector3 horizontalVelocity = moveDirection * moveSpeed;
            rb.linearVelocity = new Vector3(horizontalVelocity.x, rb.linearVelocity.y, horizontalVelocity.z);

            if (moveDirection.sqrMagnitude > 0f)
            {
                targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            }

            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));

            if (jumpRequested)
            {
                jumpRequested = false;

                if (IsGrounded())
                {
                    rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                    rb.AddForce(Vector3.up * jumpImpulse, ForceMode.Impulse);
                    animator.SetTrigger(JumpParameterHash);
                }
            }
        }

        private bool IsGrounded()
        {
            Vector3 origin = capsuleCollider.bounds.center;
            float castDistance = capsuleCollider.bounds.extents.y - capsuleCollider.radius + groundCheckDistance;
            return Physics.SphereCast(origin, capsuleCollider.radius, Vector3.down, out _, castDistance, groundLayer);
        }
    }
}
