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
        private const float BodyLean = 16f;
        private const float TurnRate = 540f;
        private const float MaxReachAssist = 0.35f;
        private const float ReachResponse = 18f;
        private const float ReachMargin = 0.001f;
        private const float Epsilon = 0.00001f;
        private sealed class Arm
        {
            public Transform Upper, Lower, Hand;
            public Quaternion PalmBasis;
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
        private float reachAssist;
        private float blend, yaw;
        private Vector3 anchor, inward;
        public float Weight => blend;
        public float MaxPalmError { get; private set; }

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            Track(spine);
            arms[0] = BindArm(true);
            arms[1] = BindArm(false);
        }

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
                RestoreCulling(); yaw = 0f; reachAssist = 0f; MaxPalmError = 0f;
                return;
            }
            sampledModelRotation = animator.transform.localRotation;
            sampledModelPosition = animator.transform.localPosition;
            for (int i = 0; i < changedCount; i++) sampledRotations[i] = changedBones[i].localRotation;
            written = true;
            // Front carriers face the cart while walking backwards; physics and camera keep their heading.
            float desiredYaw = Vector3.SignedAngle(transform.forward, inward, Vector3.up);
            yaw = Mathf.MoveTowardsAngle(yaw, desiredYaw, TurnRate * Time.deltaTime);
            animator.transform.rotation = Quaternion.AngleAxis(yaw * blend, Vector3.up) * animator.transform.rotation;
            Vector3 right = Vector3.Cross(Vector3.up, inward).normalized;
            if (spine != null) spine.rotation = Quaternion.AngleAxis(BodyLean * blend, right) * spine.rotation;
            // The owner's body can lead the interpolated cart. Shift only the sampled model
            // within its capsule instead of stretching arm bones or moving gameplay/camera roots.
            float needed = 0f;
            for (int i = 0; i < arms.Length; i++)
            {
                Arm arm = arms[i];
                Vector3 grip = anchor + right * ((i == 0 ? -1f : 1f) * HandSpacing);
                Quaternion rotation = Quaternion.LookRotation(inward, Vector3.up) * Quaternion.Inverse(arm.PalmBasis);
                Vector3 offset = grip - rotation * Vector3.Scale(arm.PalmOffset, arm.Hand.lossyScale) - arm.Upper.position;
                float reach = Vector3.Distance(arm.Upper.position, arm.Lower.position) +
                    Vector3.Distance(arm.Lower.position, arm.Hand.position) - ReachMargin;
                float forward = Vector3.Dot(offset, inward);
                float transverse = Mathf.Max(0f, offset.sqrMagnitude - forward * forward);
                needed = Mathf.Max(needed, forward - Mathf.Sqrt(Mathf.Max(0f, reach * reach - transverse)));
            }
            reachAssist = Mathf.Lerp(reachAssist, Mathf.Clamp(needed, 0f, MaxReachAssist), 1f - Mathf.Exp(-ReachResponse * Time.deltaTime));
            animator.transform.position += inward * (reachAssist * blend);
            MaxPalmError = 0f;
            for (int i = 0; i < arms.Length; i++)
            {
                Arm arm = arms[i];
                float side = i == 0 ? -1f : 1f;
                Vector3 grip = anchor + right * (side * HandSpacing);
                Quaternion handRotation = Quaternion.LookRotation(inward, Vector3.up) * Quaternion.Inverse(arm.PalmBasis);
                Vector3 wrist = grip - handRotation * Vector3.Scale(arm.PalmOffset, arm.Hand.lossyScale);
                Solve(arm, Vector3.Lerp(arm.Hand.position, wrist, blend), right * side - Vector3.up * 0.6f);
                arm.Hand.rotation = Quaternion.Slerp(arm.Hand.rotation, handRotation, blend);
                // Curl fingers around the horizontal rubber bar after aligning the palm.
                Vector3 curlAxis = Vector3.Cross(inward, Vector3.down);
                for (int j = 0; j < arm.Fingers.Length; j++)
                    if (arm.Fingers[j] != null)
                        arm.Fingers[j].rotation = Quaternion.AngleAxis((j % 3 == 1 ? 65f : 40f) * blend, curlAxis) * arm.Fingers[j].rotation;
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
