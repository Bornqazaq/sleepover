using UnityEngine;
using System.Collections.Generic;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>Local foot contact for the minigame's held poses, without editing the clips or avatar.</summary>
    internal sealed class HoleInWallPoseContact
    {
        private const float SoleClearance = .005f;
        private readonly Transform model;
        private readonly Transform player;
        private readonly Foot[] feet;
        private Vector3 appliedOffset;

        private sealed class Foot
        {
            internal Transform Bone;
            internal Vector3 Sole, Forward, Up;
        }

        internal HoleInWallPoseContact(Animator animator, Transform playerRoot)
        {
            player = playerRoot;
            if (animator == null || !animator.isHuman) return;
            model = animator.transform;
            var reference = new Dictionary<string, SkeletonBone>();
            foreach (var bone in animator.avatar.humanDescription.skeleton) reference[bone.name] = bone;
            feet = new Foot[2];
            for (int i = 0; i < feet.Length; i++)
            {
                Transform bone = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                if (bone == null) continue;
                var chain = new Stack<Transform>();
                for (var t = bone; t != null && t != model; t = t.parent) chain.Push(t);
                Matrix4x4 bind = model.localToWorldMatrix;
                bool complete = true;
                while (chain.Count > 0)
                {
                    var t = chain.Pop();
                    if (!reference.TryGetValue(t.name, out var rest)) { complete = false; break; }
                    bind *= Matrix4x4.TRS(rest.position, rest.rotation, rest.scale);
                }
                if (!complete) continue;
                // The Avatar reference skeleton is unanimated and independent of the
                // FBX renderer's extra axis/centimetre transform. Never sample a Run heel.
                Matrix4x4 inverse = bind.inverse;
                Vector3 sole = bind.GetColumn(3);
                sole.y = player.position.y;
                feet[i] = new Foot { Bone = bone, Sole = inverse.MultiplyPoint3x4(sole),
                    Forward = inverse.MultiplyVector(model.forward).normalized,
                    Up = inverse.MultiplyVector(Vector3.up).normalized };
            }
        }

        internal void RestoreOffset()
        {
            if (model != null) model.position -= appliedOffset;
            appliedOffset = Vector3.zero;
        }

        internal void Apply()
        {
            if (model == null || feet == null) return;
            float lowest = float.PositiveInfinity;
            foreach (var foot in feet)
            {
                if (foot == null) continue;
                Vector3 forward = Vector3.ProjectOnPlane(foot.Bone.TransformDirection(foot.Forward), Vector3.up);
                if (forward.sqrMagnitude < .0001f) forward = model.forward;
                // Keep the pose's foot yaw; remove only the pitch/roll that raises the heel.
                foot.Bone.rotation = Quaternion.LookRotation(forward, Vector3.up) *
                    Quaternion.Inverse(Quaternion.LookRotation(foot.Forward, foot.Up));
                lowest = Mathf.Min(lowest, foot.Bone.TransformPoint(foot.Sole).y);
            }
            if (float.IsInfinity(lowest)) return;
            // Move the whole visual together, including both shoes. Moving Hips alone
            // would stretch the ankle seam and separate trouser cuffs from the footwear.
            appliedOffset = Vector3.up * (player.position.y + SoleClearance - lowest);
            model.position += appliedOffset;
        }
    }
}
