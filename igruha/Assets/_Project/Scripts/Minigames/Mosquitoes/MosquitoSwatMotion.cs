using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    // Upper-body action owned by this minigame. The shared punch/controller stays intact.
    public sealed class MosquitoSwatMotion
    {
        private const float WindupFraction = .48f, FollowThroughFraction = 1.44f, RecoveryFraction = 2.56f;
        private readonly Transform frame, spine, chest, neck;
        private readonly MosquitoPoseLimb right, left;
        private readonly List<Transform> bones = new List<Transform>();
        private readonly List<Quaternion> rest = new List<Quaternion>();
        private readonly Quaternion[][] keys = new Quaternion[3][];
        private Quaternion[] animatedPose;
        private bool posed;
        private float elapsed, impact;
        public bool Active { get; private set; }
        public int Starts { get; private set; }

        public MosquitoSwatMotion(Animator animator, Transform frame)
        {
            this.frame = frame;
            if (animator == null || !animator.isHuman) return;
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            right = new MosquitoPoseLimb(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            left = new MosquitoPoseLimb(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone == null || spine == null || !bone.IsChildOf(spine)) continue;
                bones.Add(bone); rest.Add(bone.localRotation);
            }
            animatedPose = new Quaternion[bones.Count];
            if (!right.Valid || !left.Valid || chest == null) return;
            Quaternion[] openHand = OpenHand(animator);
            float reach = Vector3.Distance(right.Upper.position, right.Lower.position) + Vector3.Distance(right.Lower.position, right.End.position);
            Vector3 center = chest.position;
            for (int key = 0; key < keys.Length; key++)
            {
                Restore();
                for (int i = 0; i < bones.Count; i++)
                    if (bones[i] != right.End && bones[i].IsChildOf(right.End)) bones[i].localRotation = openHand[i];
                float twist = key == 0 ? 22 : key == 1 ? -12 : -28;
                Turn(spine, frame.up, twist * .45f); Turn(chest, frame.up, twist * .55f);
                Turn(chest, frame.right, key == 0 ? -4 : 8);
                Turn(neck, frame.up, -twist * .6f);
                Vector3 target = key == 0 ? new Vector3(.78f, .36f, -.18f) :
                    key == 1 ? new Vector3(-.15f, -.12f, .90f) : new Vector3(-.72f, -.42f, .38f);
                Vector3 direction = key == 0 ? frame.up : (frame.forward - frame.right * .3f).normalized;
                right.Reach(center + frame.TransformDirection(target) * reach,
                    center + (frame.right - frame.up * .3f) * reach, right.SupportRotation(direction));
                left.Reach(center + (-frame.right * .55f - frame.up * .52f + frame.forward * .12f) * reach,
                    center - (frame.right + frame.up * .4f) * reach, left.SupportRotation(frame.forward));
                keys[key] = new Quaternion[bones.Count];
                for (int i = 0; i < bones.Count; i++) keys[key][i] = bones[i].localRotation;
            }
            Restore();
        }
        private Quaternion[] OpenHand(Animator animator)
        {
            // Some personal idle clips hold a fist. Retarget only the finger muscles
            // once, cache the open palm, and restore every transform exactly.
            var transforms = animator.GetComponentsInChildren<Transform>();
            var rotations = new Quaternion[transforms.Length];
            var positions = new Vector3[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            { rotations[i] = transforms[i].localRotation; positions[i] = transforms[i].localPosition; }
            var result = new Quaternion[bones.Count];
            using (var handler = new HumanPoseHandler(animator.avatar, animator.transform))
            {
                try
                {
                    var pose = new HumanPose(); handler.GetHumanPose(ref pose);
                    string[] names = HumanTrait.MuscleName;
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (!names[i].StartsWith("Right ")) continue;
                        if (names[i].EndsWith(" Stretched")) pose.muscles[i] = .55f;
                        else if (names[i].EndsWith(" Spread")) pose.muscles[i] = .08f;
                    }
                    handler.SetHumanPose(ref pose);
                    for (int i = 0; i < bones.Count; i++) result[i] = bones[i].localRotation;
                }
                finally
                {
                    for (int i = 0; i < transforms.Length; i++)
                    { transforms[i].localRotation = rotations[i]; transforms[i].localPosition = positions[i]; }
                }
            }
            return result;
        }
        private static void Turn(Transform bone, Vector3 axis, float angle)
        { if (bone != null) bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation; }
        private void Restore() { for (int i = 0; i < bones.Count; i++) bones[i].localRotation = rest[i]; }
        public void Begin(float impactDelay)
        {
            if (keys[0] == null) return;
            Starts++;
            impact = Mathf.Max(.05f, impactDelay); elapsed = 0; Active = true;
        }
        public void RestoreAnimationPose()
        {
            if (!posed) return;
            for (int i = 0; i < bones.Count; i++) bones[i].localRotation = animatedPose[i];
            posed = false;
        }
        public void Cancel() { RestoreAnimationPose(); Active = false; }
        public void Tick(float dt)
        {
            if (!Active) return;
            elapsed += dt;
            float t = elapsed / impact;
            for (int i = 0; i < bones.Count; i++)
            {
                // Animator has supplied this frame's locomotion before LateUpdate.
                Quaternion baseline = bones[i].localRotation;
                animatedPose[i] = baseline;
                if (t < WindupFraction) bones[i].localRotation = Quaternion.Slerp(baseline, keys[0][i], Mathf.SmoothStep(0, 1, t / WindupFraction));
                else if (t < 1) { float strike = Mathf.InverseLerp(WindupFraction, 1, t); bones[i].localRotation = Quaternion.Slerp(keys[0][i], keys[1][i], strike * strike); }
                else if (t < FollowThroughFraction) bones[i].localRotation = Quaternion.Slerp(keys[1][i], keys[2][i], Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1, FollowThroughFraction, t)));
                else bones[i].localRotation = Quaternion.Slerp(keys[2][i], baseline, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(FollowThroughFraction, RecoveryFraction, t)));
            }
            posed = t < RecoveryFraction;
            if (t >= RecoveryFraction) Active = false;
        }
    }
}
