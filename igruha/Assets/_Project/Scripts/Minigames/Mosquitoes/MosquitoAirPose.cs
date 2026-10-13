using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    // Anatomical directions in miniature space; imported bone axes differ across the roster.
    public sealed class MosquitoAirPose
    {
        private const float HoverRate = 2.6f, ArmSway = .08f, LegSway = .06f;
        private readonly Transform root, neck;
        private readonly Transform[] upperArms = new Transform[2], forearms = new Transform[2];
        private readonly Transform[] hands = new Transform[2], thighs = new Transform[2];
        private readonly Transform[] shins = new Transform[2], feet = new Transform[2];

        public MosquitoAirPose(Transform root, Animator animator)
        {
            this.root = root;
            if (animator == null || !animator.isHuman) return;
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            Bind(animator, 0, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            Bind(animator, 1, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        }

        private void Bind(Animator animator, int side, HumanBodyBones arm, HumanBodyBones forearm,
            HumanBodyBones hand, HumanBodyBones thigh, HumanBodyBones shin, HumanBodyBones foot)
        {
            upperArms[side] = animator.GetBoneTransform(arm); forearms[side] = animator.GetBoneTransform(forearm);
            hands[side] = animator.GetBoneTransform(hand); thighs[side] = animator.GetBoneTransform(thigh);
            shins[side] = animator.GetBoneTransform(shin); feet[side] = animator.GetBoneTransform(foot);
        }

        public void Apply(Vector3 velocity, Vector3 acceleration, float time, float bodyPitch)
        {
            float drive = Mathf.Clamp(velocity.z, -1, 1);
            float brake = Mathf.Clamp01(-acceleration.z);
            float lift = Mathf.Clamp(velocity.y, -1, 1);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1 : 1;
                float sway = Mathf.Sin(time * HoverRate + i * 1.8f);
                Aim(upperArms[i], forearms[i], new Vector3(side * (.44f + brake * .25f), -.78f + lift * .14f, .20f - drive * .25f));
                Aim(forearms[i], hands[i], new Vector3(side * .20f, -.50f, .75f + sway * ArmSway + brake * .18f));
                Aim(thighs[i], shins[i], new Vector3(side * .13f, -.95f, .20f - drive * .12f + sway * LegSway));
                Aim(shins[i], feet[i], new Vector3(side * .06f, -.40f - brake * .35f, -.90f - drive * .12f));
                if (feet[i] != null)
                    feet[i].rotation = Quaternion.AngleAxis(-drive * 12 + sway * 3, root.right) * feet[i].rotation;
            }
            if (neck != null)
                neck.rotation = Quaternion.AngleAxis(-bodyPitch * .65f + Mathf.Sin(time * HoverRate) * 1.5f, root.right) * neck.rotation;
        }

        private void Aim(Transform bone, Transform child, Vector3 direction)
        {
            if (bone == null || child == null) return;
            bone.rotation = Quaternion.FromToRotation(child.position - bone.position, root.TransformDirection(direction)) * bone.rotation;
        }
    }
}
