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
        public KnockdownType FallType { get; private set; }
        public float ContactYaw { get; private set; }

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
        private Quaternion modelLocalRotation, contactModelRotation;
        private bool hasModelFacingOverride;
        private RigidbodyInterpolation interpolationBeforeContact;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private PitBear debugBear;
        private CircusBearMotion debugMotion;
        private Vector3 debugHitPoint, debugPredictedPosition;
        private float debugPredictedYaw, debugReportAt;
        private bool debugReportPending;
#endif

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            input = GetComponent<PlayerInputReader>();
            body = GetComponent<Rigidbody>();
            ownCollider = GetComponent<Collider>();
            animator = GetComponentInChildren<Animator>();
        }

        public static void CaptureContact(PlayerController player, Vector3 impulse,
            out KnockdownType type, out float yaw)
        {
            Vector3 facing = player.Facing;
            yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            type = Vector3.Dot(impulse, facing) < 0f ? KnockdownType.FlyBack : KnockdownType.FallForward;
        }

        public void Eliminate(Vector3 hitPoint, Vector3 impulse, KnockdownType type, float contactYaw)
        {
            if (IsEliminated) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debugHitPoint = hitPoint;
            debugPredictedPosition = motor.Position;
            debugPredictedYaw = Mathf.Atan2(motor.Facing.x, motor.Facing.z) * Mathf.Rad2Deg;
#endif
            IsEliminated = true;
            FallType = type;
            ContactYaw = contactYaw;
            hidden = false;
            elapsed = 0;
            motorWasEnabled = motor.enabled;
            wasLocked = motor.MovementLocked;
            inputWasEnabled = input != null && input.enabled;
            wasKinematic = body != null && body.isKinematic;
            if(body!=null)interpolationBeforeContact=body.interpolation;
            colliderWasEnabled = ownCollider != null && ownCollider.enabled;
            motor.MovementLocked = true;
            if (input != null && input.LocallyControlled) input.enabled = false;

            fallState = type == KnockdownType.FlyBack ? FlyBack : FallForward;
            Quaternion contactRotation = Quaternion.Euler(0, contactYaw, 0);
            hasModelFacingOverride = animator != null && animator.transform != transform;
            if (hasModelFacingOverride)
            {
                modelLocalRotation = animator.transform.localRotation;
                contactModelRotation = contactRotation * Quaternion.Inverse(transform.rotation) * animator.transform.rotation;
            }
            // Remote bodies are kinematic; only their owner applies the impulse.
            if (motorWasEnabled && body != null && !body.isKinematic)
            {
                // The contact event names the server's hit position. The owning
                // runner can be metres ahead after a stalled frame or late packet.
                // This authoritative hit starts the finale at the bear's contact,
                // regardless of prediction distance; a cap would preserve a false gap.
                motor.TeleportTo(hitPoint, contactRotation);
                // Rigidbody interpolation can retain the old render transform
                // until the next physics tick. This is the owning avatar only.
                transform.SetPositionAndRotation(hitPoint,contactRotation);
                motor.ApplyImpulse(impulse, type);
                motor.Knockdown(type);
            }

            if (animator != null && animator.isInitialized && animator.HasState(0, fallState))
            {
                if (hasModelFacingOverride) animator.transform.rotation = contactModelRotation;
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
            Trace("Contact; collapse begins, local motor=" + motorWasEnabled + "; type=" + type + "; yaw=" + contactYaw.ToString("F1"));
        }

        private void LateUpdate()
        {
            if (!IsPresenting) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugReportPending && Time.time >= debugReportAt)
            {
                debugReportPending = false;
                TraceContactPose("impact+0.05");
            }
#endif
            elapsed += Time.deltaTime;
            if (animator != null && fallLength > 0)
            {
                // Keep the server's contact heading on the visual model while
                // delayed owner rotation packets catch up. Remote physics is untouched.
                if (hasModelFacingOverride) animator.transform.rotation = contactModelRotation;
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
                if (hasModelFacingOverride) animator.transform.localRotation = modelLocalRotation;
                animator.speed = animatorSpeed;
                animator.Play("Idle", 0, 0);
            }
            motor.MovementLocked = wasLocked;
            motor.enabled = motorWasEnabled;
            if (input != null && input.LocallyControlled) input.enabled = inputWasEnabled;
            if(body!=null)body.interpolation=interpolationBeforeContact;
            IsEliminated = false;
            hidden = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debugReportPending = false;
#endif
        }

        private void OnDisable() => Restore();

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void Trace(string message) => Debug.Log("[CircusKnockout " + Time.time.ToString("F2") + "] " + name + ": " + message, this);

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public void TraceImpact(PitBear bear)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debugBear = bear;
            debugMotion = bear != null ? bear.GetComponentInChildren<CircusBearMotion>(true) : null;
            debugReportAt = Time.time + .05f;
            debugReportPending = true;
            TraceContactPose("impact");
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void TraceContactPose(string moment)
        {
            string pose = animator != null && animator.isInitialized
                ? animator.GetCurrentAnimatorStateInfo(0).shortNameHash + ":" + animator.GetCurrentAnimatorStateInfo(0).normalizedTime.ToString("F3") : "none";
            Trace(moment + "; hit=" + debugHitPoint.ToString("F3") + "; predicted=" + debugPredictedPosition.ToString("F3") +
                "; physics=" + motor.Position.ToString("F3") + "; visual=" + transform.position.ToString("F3") +
                "; yaw=" + ContactYaw.ToString("F1") + "; predictedYaw=" + debugPredictedYaw.ToString("F1") +
                "; type=" + FallType + "; pose=" + pose +
                "; bear=" + (debugBear != null ? debugBear.transform.position.ToString("F3") : "none") +
                "; visualBear=" + (debugBear != null ? debugBear.PresentationRoot.position.ToString("F3") : "none") +
                "; age=" + (debugBear != null ? debugBear.AttackAge.ToString("F3") : "none") +
                "; paw=" + (debugMotion != null ? debugMotion.StrikePawCenter.ToString("F3") : "none") +
                "; distance=" + (debugMotion != null ? debugMotion.ContactDistance.ToString("F4") : "none"));
        }
#endif

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
