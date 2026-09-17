using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Igruha.Core.Player;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>Scene-owned visual override. Shared controllers and player prefabs stay intact.</summary>
    public sealed class MemoryRunFallPose : MonoBehaviour
    {
        private const float FallEntryY = -.25f;
        private const float FallClipStart = .18f;
        private const float FallClipEnd = .72f;
        private const float ClipSpeed = .85f;
        private const float MinimumImpulseMass = .1f;
        private const float ReturnMargin = .2f;
        private static readonly int IdleHash = Animator.StringToHash("Idle");
        private PlayerController motor;
        private Animator animator;
        private Rigidbody body;
        private Vector3 pendingImpulse;
        private AnimationClip clip;
        private PlayableGraph graph;
        private AnimationClipPlayable pose;
        private float gateZ, startedAt;
        private bool failed, previousLock, playing;
        private bool motorWasEnabled, gravityWasEnabled, collisionsWereEnabled, physicsOwner, startRagdoll;
        private Vector3 hipsOffset;
        private MemoryRunRagdoll ragdoll;
        public bool IsRagdoll => ragdoll != null && ragdoll.Active;
        public int RagdollBodies => ragdoll == null ? 0 : ragdoll.BodyCount;
        private Vector3 previousPosition;
        public bool IsPresenting => playing;
        public bool IsFailure => failed;

        public void Initialize(PlayerController player, AnimationClip fallingClip, float startGateZ)
        {
            motor = player;
            body = player.GetComponent<Rigidbody>();
            animator = player.GetComponentInChildren<Animator>();
            clip = fallingClip;
            ragdoll = new MemoryRunRagdoll(animator);
            gateZ = startGateZ;
            previousPosition = player.transform.position;
            player.Teleported += Teleported;
        }

        public void BeginFailure(Vector3 impulse)
        {
            if (failed) return;
            failed = true;
            pendingImpulse = impulse;
            previousLock = motor.MovementLocked;
            motor.MovementLocked = true;
            BeginPose();
            startRagdoll = true;
        }

        private void FixedUpdate()
        {
            if (startRagdoll)
            {
                startRagdoll = false;
                physicsOwner = motor.enabled && body != null && !body.isKinematic;
                Vector3 velocity = body != null && !body.isKinematic ? body.linearVelocity : Vector3.zero;
                if (pendingImpulse != Vector3.zero)
                {
                    velocity.y = Mathf.Max(0, velocity.y);
                    velocity += pendingImpulse / Mathf.Max(MinimumImpulseMass, body.mass);
                }
                if (graph.IsValid())
                {
                    pose.SetTime(FallClipStart);
                    graph.Evaluate(0);
                    graph.Destroy();
                }
                if (ragdoll.Begin(velocity, pendingImpulse))
                {
                    playing = true;
                    hipsOffset = ragdoll.HipsPosition - motor.transform.position;
                    motorWasEnabled = motor.enabled;
                    gravityWasEnabled = body.useGravity;
                    collisionsWereEnabled = body.detectCollisions;
                    motor.enabled = false;
                    body.detectCollisions = false;
                    if (physicsOwner)
                    {
                        body.useGravity = false;
                        body.linearVelocity = Vector3.zero;
                    }
                }
                else
                {
                    // A non-humanoid replacement still gets a usable fall and respawn.
                    playing = false;
                    BeginPose();
                    if (physicsOwner && pendingImpulse != Vector3.zero)
                        motor.ApplyImpulse(pendingImpulse, KnockdownType.FlyBack);
                }
                pendingImpulse = Vector3.zero;
            }
            if (IsRagdoll) ragdoll.Simulate();
            // Keep the owner's replicated root and camera with the physical body.
            // The other peers simulate only cosmetic joints; the root still comes over NGO.
            if (IsRagdoll && physicsOwner)
            {
                body.linearVelocity = Vector3.zero;
                body.position = ragdoll.HipsPosition - hipsOffset;
            }
        }

        private void BeginPose()
        {
            if (playing || animator == null || clip == null) return;
            graph = PlayableGraph.Create("MemoryRunFall");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            pose = AnimationClipPlayable.Create(graph, clip);
            pose.SetApplyFootIK(false);
            var output = AnimationPlayableOutput.Create(graph, "Fall", animator);
            output.SetSourcePlayable(pose);
            graph.Play();
            startedAt = Time.time;
            playing = true;
        }

        private void LateUpdate()
        {
            if (motor == null) return;
            Vector3 position = motor.transform.position;
            // Read replicated positions on remote avatars; their motor is deliberately disabled.
            bool belowRoute = position.z > gateZ && position.y < FallEntryY;
            if (!playing && belowRoute && position.y < previousPosition.y) BeginPose();
            if (playing)
            {
                bool returned = position.z < gateZ - ReturnMargin && position.y >= 0;
                bool landed = !failed && !belowRoute && position.y >= 0;
                if (returned || landed) Clear();
                else if (IsRagdoll) ragdoll.ApplyPose();
                else if (graph.IsValid())
                {
                    // Use the existing airborne portion, never its ground impact or get-up chain.
                    double time = Mathf.Min(FallClipEnd, FallClipStart + (Time.time - startedAt) * ClipSpeed);
                    pose.SetTime(time);
                    graph.Evaluate(0);
                }
            }
            previousPosition = position;
        }

        private void Teleported() => Clear();
        private void Clear()
        {
            if (graph.IsValid()) graph.Destroy();
            if (IsRagdoll)
            {
                ragdoll.Clear();
                if (body != null)
                {
                    body.useGravity = gravityWasEnabled;
                    body.detectCollisions = collisionsWereEnabled;
                    if (physicsOwner && !body.isKinematic) body.linearVelocity = Vector3.zero;
                }
                if (motor != null) motor.enabled = motorWasEnabled;
            }
            startRagdoll = false;
            if (playing && animator != null)
            {
                animator.ResetTrigger("KnockdownFront");
                animator.ResetTrigger("KnockdownBack");
                animator.Play(IdleHash, 0, 0);
            }
            playing = false;
            pendingImpulse = Vector3.zero;
            if (failed && motor != null) motor.MovementLocked = previousLock;
            failed = false;
        }
        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            if (motor != null) motor.Teleported -= Teleported;
            Clear();
        }
    }
}
