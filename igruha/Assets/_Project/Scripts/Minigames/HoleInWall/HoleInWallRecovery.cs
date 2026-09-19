using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>Server-scheduled water boost. Only the owner moves the body;
    /// ordinary player replication carries the path to the other machines.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class HoleInWallRecovery : MonoBehaviour
    {
        private const float GatherEnd = .12f;
        private const float RiseEnd = .65f;
        private const float DeckClearance = 1.05f;
        private const float ArcHeight = .35f;

        [SerializeField] private HoleInWallReturnJet jet;
        private PlayerController avatar;
        private Rigidbody body;
        private Transform leftFoot, rightFoot, leftHand, rightHand, head;
        private double settleUntil;
        private bool driven, wasEnabled, hadGravity, hadCollision, wasLocked;
        private Vector3 from, launch, crest, landing;
        private Quaternion facing;
        private double start, end;
        public bool Active { get; private set; }

        public void Begin(PlayerController player, Vector3 source, Vector3 destination,
            float clearZ, float waterY, double startTime, double endTime)
        {
            Cancel();
            if (endTime <= NetworkClock.Now) return;
            avatar = player;
            if (avatar == null) return;
            body = avatar.GetComponent<Rigidbody>();
            var animator = avatar.GetComponentInChildren<Animator>();
            leftFoot = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
            rightFoot = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightFoot) : null;
            leftHand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            rightHand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            driven = WorldAuthority.DrivenHere(avatar.GetComponent<NetworkObject>());
            from = driven ? avatar.Position : source;
            landing = destination;
            // Rise outside the deck before travelling onto it. Nothing solid is
            // placed on this route or in the camera's permanent foreground.
            launch = new Vector3(from.x, from.y, Mathf.Min(from.z, clearZ));
            crest = new Vector3(launch.x, destination.y + DeckClearance, launch.z);
            facing = Quaternion.LookRotation(Vector3.forward);
            start = startTime; end = endTime;
            Active = true;
            if (driven)
            {
                wasEnabled = avatar.enabled; wasLocked = avatar.MovementLocked;
                hadGravity = body.useGravity; hadCollision = body.detectCollisions;
                avatar.TeleportTo(from, facing); // Same position: clear the finished fall, no jump.
                avatar.MovementLocked = true;
                avatar.enabled = false;
                // Keep the owner body dynamic so a server teleport arriving just
                // before the final local physics step can safely clear velocity.
                body.useGravity = false;
                body.detectCollisions = false;
            }
            jet.Begin(launch, waterY);
        }

        private void FixedUpdate()
        {
            if (!Active) { SupportLanding(); return; }
            if (avatar == null) { Cancel(); return; }
            float t = Progress;
            Vector3 position = PositionAt(t);
            if (t >= RiseEnd) position.y = Mathf.Max(position.y, SupportedLandingY());
            if (driven)
            {
                if (!body.isKinematic) body.linearVelocity = Vector3.zero;
                body.MovePosition(position);
                body.MoveRotation(facing);
            }
            if (t >= 1) Complete();
        }

        private float Progress => Mathf.Clamp01((float)((NetworkClock.Now - start) /
            System.Math.Max(.05, end - start)));

        private Vector3 PositionAt(float progress)
        {
            if (progress < GatherEnd)
                return Vector3.Lerp(from, launch, Mathf.SmoothStep(0, 1, progress / GatherEnd));
            if (progress < RiseEnd)
                return Vector3.Lerp(launch, crest,
                    Mathf.SmoothStep(0, 1, (progress - GatherEnd) / (RiseEnd - GatherEnd)));
            float t = Mathf.SmoothStep(0, 1, (progress - RiseEnd) / (1 - RiseEnd));
            Vector3 position = Vector3.Lerp(crest, landing, t);
            position.y += Mathf.Sin(t * Mathf.PI) * ArcHeight;
            return position;
        }

        private void LateUpdate()
        {
            if (!Active) { SupportLanding(); return; }
            if (avatar == null) { Cancel(); return; }
            Vector3 support = avatar.transform.position;
            // The wake follows the lowest sole while the unchanged fall/get-up
            // animation runs. It is water, with no rail, deck or collider to clip.
            if (leftFoot != null) support.y = Mathf.Min(support.y, leftFoot.position.y - .1f);
            if (rightFoot != null) support.y = Mathf.Min(support.y, rightFoot.position.y - .1f);
            jet.Draw(support, Progress);
        }

        // The frozen get-up clips briefly put soles/hands below the capsule root.
        // Support the body through that last blend, instead of burying it in the
        // deck or editing the animation. This is local owner physics, replicated
        // by the ordinary player transform, and does not take away movement.
        private float SupportedLandingY()
        {
            float root = avatar.transform.position.y;
            float bottom = root;
            if (leftFoot != null) bottom = Mathf.Min(bottom, leftFoot.position.y - .1f);
            if (rightFoot != null) bottom = Mathf.Min(bottom, rightFoot.position.y - .1f);
            if (leftHand != null) bottom = Mathf.Min(bottom, leftHand.position.y - .06f);
            if (rightHand != null) bottom = Mathf.Min(bottom, rightHand.position.y - .06f);
            if (head != null) bottom = Mathf.Min(bottom, head.position.y - .23f);
            return landing.y + root - bottom;
        }

        private void SupportLanding()
        {
            if (!driven || avatar == null || body == null || NetworkClock.Now >= settleUntil) return;
            Vector3 position = body.position;
            Vector2 distance = new Vector2(position.x - landing.x, position.z - landing.z);
            if (avatar.IsKnockedDown || distance.sqrMagnitude > 2.25f || position.y < landing.y - .15f)
            { settleUntil = 0; return; }
            float supported = SupportedLandingY();
            if (position.y >= supported) return;
            position.y = supported;
            body.position = position;
            if (!body.isKinematic && body.linearVelocity.y < 0)
                body.linearVelocity = new Vector3(body.linearVelocity.x, 0, body.linearVelocity.z);
        }

        public void Complete()
        {
            if (!Active) return;
            if (driven && avatar != null && body != null)
            {
                Vector3 position = landing;
                position.y = SupportedLandingY();
                body.position = position;
                body.rotation = facing;
                settleUntil = end + 1.2;
            }
            RestoreMotor();
        }

        private void RestoreMotor()
        {
            if (Active && driven && avatar != null && body != null)
            {
                body.detectCollisions = hadCollision;
                if (!body.isKinematic) body.linearVelocity = Vector3.zero;
                avatar.enabled = wasEnabled;
                avatar.MovementLocked = wasLocked;
                body.useGravity = hadGravity;
            }
            Active = false;
            if (jet != null) jet.Clear();
        }

        public void Cancel()
        {
            RestoreMotor();
            settleUntil = 0;
        }

        private void OnDisable() => Cancel();
    }
}
