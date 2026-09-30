using System.Collections.Generic;
using Igruha.Core.Items;
using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Two handed cart grip over the sampled humanoid pose, also on remote avatars.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(30)]
    public sealed class WaterCartGripPose : MonoBehaviour
    {
        private const float BlendSeconds = 0.18f;
        private const float HandSpacing = 0.115f;
        private const float GripHeight = 0.04f;
        private const float BodyLean = 8f;
        private const float ComfortableReach = 0.85f;
        private const float MaxStanceAdjustment = 0.3f;
        private const float ElbowOutward = 0.25f;
        private const float ReachMargin = 0.001f;
        private const float Epsilon = 0.00001f;
        private sealed class Arm
        {
            public Transform Upper, Lower, Hand;
            public Quaternion PalmBasis;
            public Quaternion UpperRest, LowerRest;
            public readonly Quaternion[] FingerRest = new Quaternion[12];
            public Vector3 PalmOffset;
            public readonly Transform[] Fingers = new Transform[12];
        }
        private readonly Arm[] arms = new Arm[2];
        private readonly Transform[] changedBones = new Transform[32];
        private readonly Quaternion[] sampledRotations = new Quaternion[32];
        private int changedCount;
        private PlayerController player;
        private Animator animator;
        private Transform spine;
        private MultiCarryObject cart;
        private AnimatorCullingMode previousCulling;
        private bool forceAnimation, written;
        private Quaternion sampledModelRotation;
        private Vector3 sampledModelPosition;
        private Quaternion modelRestRotation;
        private Vector3 stanceOffset;
        private float blend;
        private Dictionary<Transform, Quaternion> bindRotations;
        private Vector3 anchor, inward;
        public float Weight => blend;
        public float MaxPalmError { get; private set; }

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;
            modelRestRotation = Quaternion.Inverse(transform.rotation) * animator.transform.rotation;
            bindRotations = ReadBindRotations();
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            Track(spine);
            arms[0] = BindArm(true);
            arms[1] = BindArm(false);
            bindRotations = null;
        }

        // Use imported skin bind poses, never the current walk-frame's arm twist.
        private Dictionary<Transform, Quaternion> ReadBindRotations()
        {
            var result = new Dictionary<Transform, Quaternion>();
            foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skin.sharedMesh == null) continue;
                var bones = skin.bones;
                var poses = skin.sharedMesh.bindposes;
                var indices = new Dictionary<Transform, int>();
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null) indices[bones[i]] = i;
                for (int i = 0; i < bones.Length && i < poses.Length; i++)
                    if (bones[i] != null && bones[i].parent != null &&
                        indices.TryGetValue(bones[i].parent, out int parent) && parent < poses.Length)
                        result[bones[i]] = (poses[parent] * poses[i].inverse).rotation;
            }
            return result;
        }

        private Quaternion RestOf(Transform bone) =>
            bindRotations.TryGetValue(bone, out var rotation) ? rotation : bone.localRotation;

        private void Track(Transform bone)
        {
            if (bone != null) changedBones[changedCount++] = bone;
        }

        private Arm BindArm(bool left)
        {
            var arm = new Arm
            {
                Upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm),
                Lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm),
                Hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand)
            };
            if (arm.Upper == null || arm.Lower == null || arm.Hand == null) return null;
            Track(arm.Upper); Track(arm.Lower); Track(arm.Hand);
            arm.UpperRest = RestOf(arm.Upper);
            arm.LowerRest = RestOf(arm.Lower);
            var middle = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var index = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var little = animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            Vector3 forward = middle != null ? middle.position - arm.Hand.position : arm.Hand.position - arm.Lower.position;
            Vector3 normal = index != null && little != null
                ? Vector3.Cross(index.position - little.position, forward) * (left ? -1f : 1f) : transform.up;
            arm.PalmBasis = Quaternion.Inverse(arm.Hand.rotation) * Quaternion.LookRotation(forward, normal);
            arm.PalmOffset = arm.Hand.InverseTransformPoint(arm.Hand.position + forward * 0.75f);
            HumanBodyBones first = left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal;
            // Humanoid's four non-thumb fingers each contain proximal/intermediate/distal bones.
            for (int i = 0; i < arm.Fingers.Length; i++)
            {
                arm.Fingers[i] = animator.GetBoneTransform(first + i);
                Track(arm.Fingers[i]);
                if (arm.Fingers[i] != null) arm.FingerRest[i] = RestOf(arm.Fingers[i]);
            }
            return arm;
        }

        public void Hold(MultiCarryObject owner)
        {
            cart = owner;
            if (animator == null || forceAnimation) return;
            previousCulling = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            forceAnimation = true;
        }

        public void Release(MultiCarryObject owner)
        {
            if (cart == owner) cart = null;
        }

        private void Update() => RestoreSample();

        private void LateUpdate()
        {
            if (animator == null || !animator.isHuman || !animator.isActiveAndEnabled || arms[0] == null || arms[1] == null) return;
            int slot = -1;
            if (cart != null && cart.isActiveAndEnabled)
                for (int i = 0; i < cart.HandleCount; i++) if (cart.CarrierAt(i) == player) { slot = i; break; }
            if (slot >= 0)
            {
                anchor = cart.HandleAnchor(slot) + Vector3.up * GripHeight;
                inward = cart.HandleAnchor(slot) - cart.StationOf(slot);
                inward.y = 0f; inward.Normalize();
            }
            blend = Mathf.MoveTowards(blend, slot >= 0 && !player.IsKnockedDown ? 1f : 0f, Time.deltaTime / BlendSeconds);
            if (blend <= 0f)
            {
                RestoreCulling(); stanceOffset = Vector3.zero; MaxPalmError = 0f;
                return;
            }
            sampledModelRotation = animator.transform.localRotation;
            sampledModelPosition = animator.transform.localPosition;
            for (int i = 0; i < changedCount; i++) sampledRotations[i] = changedBones[i].localRotation;
            written = true;
            // The cart is the reference for the visible body, including turns and strafing.
            // A spring between gameplay roots is not an arm: do not show its stretch on the rig.
            animator.transform.rotation = Quaternion.Slerp(animator.transform.rotation,
                Quaternion.LookRotation(inward, Vector3.up) * modelRestRotation, blend);
            Vector3 right = Vector3.Cross(Vector3.up, inward).normalized;
            if (spine != null) spine.rotation = Quaternion.AngleAxis(BodyLean * blend, right) * spine.rotation;
            if (slot >= 0)
            {
                stanceOffset = cart.StationOf(slot) - transform.position;
                stanceOffset.y = 0f;
            }
            animator.transform.position += stanceOffset * blend;
            // Leave room to bend the elbows. This signed correction also keeps tall carriers
            // from standing too close. It is recomputed from the sampled pose, never accumulated.
            float adjustment = 0f;
            for (int i = 0; i < arms.Length; i++)
            {
                Arm arm = arms[i];
                Vector3 grip = anchor + right * ((i == 0 ? -1f : 1f) * HandSpacing);
                Quaternion rotation = Quaternion.LookRotation(inward, Vector3.up) * Quaternion.Inverse(arm.PalmBasis);
                Vector3 offset = grip - rotation * Vector3.Scale(arm.PalmOffset, arm.Hand.lossyScale) - arm.Upper.position;
                float reach = (Vector3.Distance(arm.Upper.position, arm.Lower.position) +
                    Vector3.Distance(arm.Lower.position, arm.Hand.position)) * ComfortableReach;
                float forward = Vector3.Dot(offset, inward);
                float transverse = Mathf.Max(0f, offset.sqrMagnitude - forward * forward);
                adjustment += forward - Mathf.Sqrt(Mathf.Max(0f, reach * reach - transverse));
            }
            animator.transform.position += inward * (Mathf.Clamp(adjustment / arms.Length,
                -MaxStanceAdjustment, MaxStanceAdjustment) * blend);
            MaxPalmError = 0f;
            for (int i = 0; i < arms.Length; i++)
            {
                Arm arm = arms[i];
                float side = i == 0 ? -1f : 1f;
                Vector3 grip = anchor + right * (side * HandSpacing);
                Quaternion handRotation = Quaternion.LookRotation(inward, Vector3.up) * Quaternion.Inverse(arm.PalmBasis);
                Vector3 wrist = grip - handRotation * Vector3.Scale(arm.PalmOffset, arm.Hand.lossyScale);
                Quaternion rawUpper = arm.Upper.localRotation, rawLower = arm.Lower.localRotation;
                arm.Upper.localRotation = arm.UpperRest;
                arm.Lower.localRotation = arm.LowerRest;
                Solve(arm, wrist, right * (side * ElbowOutward) - Vector3.up);
                arm.Upper.localRotation = Quaternion.Slerp(rawUpper, arm.Upper.localRotation, blend);
                arm.Lower.localRotation = Quaternion.Slerp(rawLower, arm.Lower.localRotation, blend);
                arm.Hand.rotation = Quaternion.Slerp(arm.Hand.rotation, handRotation, blend);
                // Curl fingers around the horizontal rubber bar after aligning the palm.
                Vector3 curlAxis = Vector3.Cross(inward, Vector3.down);
                for (int j = 0; j < arm.Fingers.Length; j++)
                    if (arm.Fingers[j] != null)
                    {
                        Quaternion raw = arm.Fingers[j].localRotation;
                        arm.Fingers[j].localRotation = arm.FingerRest[j];
                        arm.Fingers[j].rotation = Quaternion.AngleAxis(j % 3 == 1 ? 65f : 40f, curlAxis) * arm.Fingers[j].rotation;
                        arm.Fingers[j].localRotation = Quaternion.Slerp(raw, arm.Fingers[j].localRotation, blend);
                    }
                MaxPalmError = Mathf.Max(MaxPalmError, Vector3.Distance(arm.Hand.TransformPoint(arm.PalmOffset), grip));
            }
        }

        private static void Solve(Arm arm, Vector3 target, Vector3 hint)
        {
            Vector3 shoulder = arm.Upper.position;
            float upper = Vector3.Distance(shoulder, arm.Lower.position);
            float lower = Vector3.Distance(arm.Lower.position, arm.Hand.position);
            Vector3 ray = target - shoulder;
            if (ray.sqrMagnitude < Epsilon || upper < Epsilon || lower < Epsilon) return;
            float distance = Mathf.Clamp(ray.magnitude, Mathf.Abs(upper - lower) + ReachMargin, upper + lower - ReachMargin);
            Vector3 direction = ray.normalized;
            float along = (upper * upper + distance * distance - lower * lower) / (2f * distance);
            float across = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector3 bend = Vector3.ProjectOnPlane(hint, direction).normalized;
            if (bend.sqrMagnitude < Epsilon) bend = Vector3.Cross(direction, Vector3.forward).normalized;
            Vector3 elbow = shoulder + direction * along + bend * across;
            arm.Upper.rotation = Quaternion.FromToRotation(arm.Lower.position - shoulder, elbow - shoulder) * arm.Upper.rotation;
            arm.Lower.rotation = Quaternion.FromToRotation(arm.Hand.position - arm.Lower.position,
                shoulder + direction * distance - arm.Lower.position) * arm.Lower.rotation;
        }

        private void RestoreSample()
        {
            if (!written || animator == null) return;
            animator.transform.localRotation = sampledModelRotation;
            animator.transform.localPosition = sampledModelPosition;
            for (int i = 0; i < changedCount; i++)
                if (changedBones[i] != null) changedBones[i].localRotation = sampledRotations[i];
            written = false;
        }

        private void RestoreCulling()
        {
            if (forceAnimation && animator != null) animator.cullingMode = previousCulling;
            forceAnimation = false;
        }

        private void OnDisable()
        {
            RestoreSample(); RestoreCulling();
            cart = null; blend = 0f;
        }
    }
}
