using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>Temporary passive visual body; it cannot hit players or decide round outcomes.</summary>
    internal sealed class MemoryRunRagdoll
    {
        private const int BodyCapacity = 11;
        private const float MinimumHeight = .8f;
        private const float HipsLengthRatio = .17f;
        private const float HipsRadiusRatio = .12f;
        private const float TorsoRadiusRatio = .13f;
        private const float HeadLengthRatio = .13f;
        private const float HeadRadiusRatio = .09f;
        private const float HipsMass = 4f, TorsoMass = 5f, HeadMass = 1.5f;
        private const float UpperLimbMass = 1f, LowerLimbMass = .7f;
        private const float LinearDrag = .04f, AngularDrag = .12f;
        private const int PositionIterations = 12, VelocityIterations = 4;
        private const float DepenetrationSpeed = 3f;
        private const float TwistLimit = 40f, SwingLimit = 55f;
        private const float JointProjectionDistance = .04f, JointProjectionAngle = 12f;
        private const float MinimumDirectionSquared = .01f, MinimumSupportNormalY = .5f;
        private const float Friction = .035f;
        private const float LimbRadiusRatio = .22f;
        private const float MinimumRadius = .055f;
        private const float SpinSpeed = 2.2f;
        private const float SlideAcceleration = 2.2f;
        private const float SlideSpeed = 2.4f;
        private const float SupportProbeLift = .1f;
        private const float SupportProbeDistance = .75f;
        private Vector3 slideDirection;
        private readonly int environmentMask = LayerMask.GetMask("Ground", "Cover", "PlayerBarrier", "Eliminated");
        private readonly List<Part> parts = new List<Part>(BodyCapacity);
        private readonly Animator animator;
        private GameObject root;
        private PhysicsMaterial material;
        private bool animatorWasEnabled;
        private Transform[] skeleton;
        private Vector3[] savedPositions;
        private Quaternion[] savedRotations;
        private sealed class Part
        {
            public Transform Bone;
            public Rigidbody Body;
            public Quaternion RotationOffset;
        }
        public bool Active => root != null;
        public int BodyCount => parts.Count;
        public Vector3 HipsPosition => parts[0].Body.position;

        public MemoryRunRagdoll(Animator target) => animator = target;

        public bool Begin(Vector3 velocity, Vector3 impulseDirection)
        {
            if (animator == null || !animator.isHuman || Active) return false;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (hips == null || spine == null || head == null) return false;
            skeleton = animator.GetComponentsInChildren<Transform>();
            savedPositions = new Vector3[skeleton.Length];
            savedRotations = new Quaternion[skeleton.Length];
            for (int i = 0; i < skeleton.Length; i++)
            {
                savedPositions[i] = skeleton[i].localPosition;
                savedRotations[i] = skeleton[i].localRotation;
            }
            animatorWasEnabled = animator.enabled;
            animator.enabled = false;
            root = new GameObject("MemoryRun_PassiveBody");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, animator.gameObject.scene);
            material = new PhysicsMaterial("MemoryRun_SlipperyBody")
            {
                dynamicFriction = Friction, staticFriction = Friction, bounciness = 0,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            float height = Mathf.Max(MinimumHeight, Vector3.Distance(hips.position, head.position) * 2f);
            Add(hips, hips.position + (spine.position - hips.position).normalized * height * HipsLengthRatio, height * HipsRadiusRatio, HipsMass, null);
            var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            Add(spine, neck != null ? neck.position : head.position, height * TorsoRadiusRatio, TorsoMass, parts[0]);
            Add(head, head.position + animator.transform.up * height * HeadLengthRatio, height * HeadRadiusRatio, HeadMass, parts[1]);
            Limb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, parts[1]);
            Limb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, parts[1]);
            Limb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, parts[0]);
            Limb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, parts[0]);
            slideDirection = Vector3.ProjectOnPlane(impulseDirection, Vector3.up).normalized;
            if (slideDirection.sqrMagnitude < MinimumDirectionSquared)
                slideDirection = hips.position.x < 0 ? Vector3.left : Vector3.right;
            Vector3 spin = Vector3.Cross(Vector3.up, impulseDirection.normalized) * SpinSpeed;
            foreach (Part part in parts)
            {
                part.Body.linearVelocity = velocity;
                part.Body.angularVelocity = spin;
            }
            return true;
        }

        private void Limb(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones tip, Part parent)
        {
            Transform a = animator.GetBoneTransform(upper), b = animator.GetBoneTransform(lower), c = animator.GetBoneTransform(tip);
            if (a == null || b == null || c == null) return;
            Add(a, b.position, Vector3.Distance(a.position, b.position) * LimbRadiusRatio, UpperLimbMass, parent);
            Add(b, c.position, Vector3.Distance(b.position, c.position) * LimbRadiusRatio, LowerLimbMass, parts[parts.Count - 1]);
        }

        private void Add(Transform bone, Vector3 end, float radius, float mass, Part parent)
        {
            var go = new GameObject(bone.name + "_Physics");
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
            go.transform.SetParent(root.transform);
            Vector3 segment = end - bone.position;
            go.transform.SetPositionAndRotation(bone.position, Quaternion.FromToRotation(Vector3.up, segment.normalized));
            var shape = go.AddComponent<CapsuleCollider>();
            shape.direction = 1;
            shape.radius = Mathf.Max(MinimumRadius, radius);
            shape.height = Mathf.Max(segment.magnitude, shape.radius * 2);
            shape.center = Vector3.up * segment.magnitude * .5f;
            shape.sharedMaterial = material;
            // No player contacts, self-collisions, triggers, or influence on gameplay.
            shape.includeLayers = LayerMask.GetMask("Eliminated");
            shape.excludeLayers = ~environmentMask;
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.linearDamping = LinearDrag;
            body.angularDamping = AngularDrag;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.solverIterations = PositionIterations;
            body.solverVelocityIterations = VelocityIterations;
            body.maxDepenetrationVelocity = DepenetrationSpeed;
            if (parent != null)
            {
                var joint = go.AddComponent<CharacterJoint>();
                joint.connectedBody = parent.Body;
                joint.axis = Vector3.right;
                joint.swingAxis = Vector3.forward;
                joint.lowTwistLimit = new SoftJointLimit { limit = -TwistLimit };
                joint.highTwistLimit = new SoftJointLimit { limit = TwistLimit };
                joint.swing1Limit = new SoftJointLimit { limit = SwingLimit };
                joint.swing2Limit = new SoftJointLimit { limit = TwistLimit };
                joint.enableProjection = true;
                joint.projectionDistance = JointProjectionDistance;
                joint.projectionAngle = JointProjectionAngle;
                joint.enablePreprocessing = false;
            }
            parts.Add(new Part { Bone = bone, Body = body, RotationOffset = Quaternion.Inverse(body.rotation) * bone.rotation });
        }

        public void Simulate()
        {
            if (!Active) return;
            // A small surface-only drift preserves the comic slide even after a flat impact.
            // In the air all joints are entirely passive; there are no pose drives.
            if (Vector3.Dot(parts[0].Body.linearVelocity, slideDirection) >= SlideSpeed) return;
            if (!Physics.Raycast(HipsPosition + Vector3.up * SupportProbeLift, Vector3.down,
                out RaycastHit hit, SupportProbeDistance, environmentMask, QueryTriggerInteraction.Ignore)
                || hit.normal.y < MinimumSupportNormalY) return;
            foreach (Part part in parts)
                part.Body.AddForce(slideDirection * SlideAcceleration, ForceMode.Acceleration);
        }

        public void ApplyPose()
        {
            if (!Active) return;
            // Parent-first world transforms preserve the original skin and unmodelled fingers/feet.
            foreach (Part part in parts)
                part.Bone.SetPositionAndRotation(part.Body.transform.position, part.Body.transform.rotation * part.RotationOffset);
        }

        public void Clear()
        {
            if (!Active) return;
            root.SetActive(false);
            Object.Destroy(root);
            Object.Destroy(material);
            root = null;
            for (int i = 0; i < skeleton.Length; i++)
                if (skeleton[i] != null)
                {
                    skeleton[i].localPosition = savedPositions[i];
                    skeleton[i].localRotation = savedRotations[i];
                }
            if (animator != null) animator.enabled = animatorWasEnabled;
            parts.Clear();
        }
    }
}
