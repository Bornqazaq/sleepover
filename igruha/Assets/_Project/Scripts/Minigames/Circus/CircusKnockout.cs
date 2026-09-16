using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>The circus hit has one clock: contact, collapse, then spectator.
    /// Uses the existing character fall clips without changing shared player assets.</summary>
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(PlayerController))]
    public sealed class CircusKnockout : MonoBehaviour
    {
        public const float PresentationSeconds = 1.55f;
        private const float LastFallPose = .94f;
        private static readonly int FlyBack = Animator.StringToHash("FlyBack");
        private static readonly int FallForward = Animator.StringToHash("FallForward");
        private static readonly int FrontTrigger = Animator.StringToHash("KnockdownFront");
        private static readonly int BackTrigger = Animator.StringToHash("KnockdownBack");

        public event Action<CircusKnockout> BodyHidden;
        public bool IsEliminated { get; private set; }
        public bool IsPresenting => IsEliminated && !hidden;

        private PlayerController motor;
        private PlayerInputReader input;
        private Rigidbody body;
        private Collider ownCollider;
        private Animator animator;
        private readonly List<Renderer> visuals = new List<Renderer>(8);
        private readonly List<Collider> ignored = new List<Collider>(8);
        private bool hidden, motorWasEnabled, wasLocked, inputWasEnabled, wasKinematic, colliderWasEnabled;
        private float elapsed, fallLength, animatorSpeed;
        private int fallState;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            input = GetComponent<PlayerInputReader>();
            body = GetComponent<Rigidbody>();
            ownCollider = GetComponent<Collider>();
            animator = GetComponentInChildren<Animator>();
        }

        public void Eliminate(Vector3 hitPoint, Vector3 impulse)
        {
            if (IsEliminated) return;
            IsEliminated = true;
            hidden = false;
            elapsed = 0;
            motorWasEnabled = motor.enabled;
            wasLocked = motor.MovementLocked;
            inputWasEnabled = input != null && input.enabled;
            wasKinematic = body != null && body.isKinematic;
            colliderWasEnabled = ownCollider != null && ownCollider.enabled;
            motor.MovementLocked = true;
            if (input != null && input.LocallyControlled) input.enabled = false;

            bool fromFront = Vector3.Dot(impulse.normalized, motor.Facing) < 0f;
            fallState = fromFront ? FlyBack : FallForward;
            // Remote bodies are kinematic; only their owner applies the impulse.
            if (motorWasEnabled && body != null && !body.isKinematic)
            {
                var type = fromFront ? KnockdownType.FlyBack : KnockdownType.FallForward;
                motor.ApplyImpulse(impulse, type);
                motor.Knockdown(type);
            }

            if (animator != null && animator.isInitialized && animator.HasState(0, fallState))
            {
                animatorSpeed = animator.speed;
                animator.ResetTrigger(FrontTrigger);
                animator.ResetTrigger(BackTrigger);
                animator.Play(fallState, 0, 0);
                animator.Update(0);
                fallLength = Mathf.Max(.1f, animator.GetCurrentAnimatorStateInfo(0).length);
                // Sample the fall on our contact clock. Automatic get-up transitions
                // must not let a eliminated runner stand up before the body disappears.
                animator.speed = 0;
            }
            else fallLength = 0;

            visuals.Clear();
            GetComponentsInChildren(true, visuals);
            for (int i = visuals.Count - 1; i >= 0; i--)
                if (!visuals[i].enabled) visuals.RemoveAt(i);
            IgnoreLivingPlayers();
            Trace("Contact; collapse begins, local motor=" + motorWasEnabled);
        }

        private void LateUpdate()
        {
            if (!IsPresenting) return;
            elapsed += Time.deltaTime;
            if (animator != null && fallLength > 0)
            {
                animator.Play(fallState, 0, Mathf.Min(elapsed / fallLength, LastFallPose));
                animator.Update(0);
            }
            if (elapsed < PresentationSeconds) return;

            motor.enabled = false;
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
            if (ownCollider != null) ownCollider.enabled = false;
            foreach (var visual in visuals) if (visual != null) visual.enabled = false;
            hidden = true;
            Trace("Body hidden; spectator event at " + elapsed.ToString("F2") + " s");
            BodyHidden?.Invoke(this);
        }

        public void Restore()
        {
            if (!IsEliminated) return;
            foreach (var other in ignored)
                if (other != null && ownCollider != null) Physics.IgnoreCollision(ownCollider, other, false);
            ignored.Clear();
            foreach (var visual in visuals) if (visual != null) visual.enabled = true;
            if (ownCollider != null) ownCollider.enabled = colliderWasEnabled;
            if (body != null) body.isKinematic = wasKinematic;
            // Hiding pauses the motor before its ordinary get-up timer expires.
            // Clear that timer through the existing local reset API before the
            // avatar returns to the hub; never wake a remote physics body.
            if (motorWasEnabled && body != null && !wasKinematic && motor.IsKnockedDown)
                motor.TeleportTo(transform.position, transform.rotation);
            if (animator != null && fallLength > 0)
            {
                animator.speed = animatorSpeed;
                animator.Play("Idle", 0, 0);
            }
            motor.MovementLocked = wasLocked;
            motor.enabled = motorWasEnabled;
            if (input != null && input.LocallyControlled) input.enabled = inputWasEnabled;
            IsEliminated = false;
            hidden = false;
        }

        private void OnDisable() => Restore();

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void Trace(string message) => Debug.Log("[CircusKnockout " + Time.time.ToString("F2") + "] " + name + ": " + message, this);

        private void IgnoreLivingPlayers()
        {
            if (ownCollider == null) return;
            foreach (var other in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (other == motor || !other.TryGetComponent(out Collider collider)) continue;
                Physics.IgnoreCollision(ownCollider, collider, true);
                ignored.Add(collider);
            }
        }
    }
}
