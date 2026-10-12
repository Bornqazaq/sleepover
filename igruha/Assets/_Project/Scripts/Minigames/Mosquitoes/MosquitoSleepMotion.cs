using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    // A short bed performance with supported pelvis, planted hands and lifted feet.
    // Pose endpoints are built once per transition; rendering allocates nothing.
    public sealed class MosquitoSleepMotion
    {
        private const float SeatFraction = .32f, ReclineFraction = .72f;
        private const float ReclineDegrees = 52f, FootLift = .48f;
        private readonly Transform model, hips, chest, neck, head;
        private readonly Quaternion uprightRotation;
        private readonly Transform[] bones;
        private readonly MosquitoPoseLimb[] limbs;
        private Pose start, finish, seat, recline, rest;
        private bool lyingDown;

        public sealed class Pose
        {
            internal Vector3 Position, Hip;
            internal Quaternion Rotation;
            internal Quaternion[] Bones, EndRotations;
            internal Vector3[] Scales, Ends;
        }

        public MosquitoSleepMotion(Animator animator)
        {
            model = animator.transform;
            uprightRotation = model.localRotation;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            var list = new List<Transform>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null) list.Add(bone);
            }
            bones = list.ToArray();
            limbs = new[] {
                new MosquitoPoseLimb(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
                new MosquitoPoseLimb(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot),
                new MosquitoPoseLimb(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
                new MosquitoPoseLimb(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand)
            };
        }

        public Pose Capture()
        {
            var pose = new Pose { Position = model.position, Rotation = model.rotation, Hip = hips.position,
                Bones = new Quaternion[bones.Length], Scales = new Vector3[bones.Length],
                Ends = new Vector3[limbs.Length], EndRotations = new Quaternion[limbs.Length] };
            for (int i = 0; i < bones.Length; i++) { pose.Bones[i] = bones[i].localRotation; pose.Scales[i] = bones[i].localScale; }
            for (int i = 0; i < limbs.Length; i++) if (limbs[i].Valid)
            { pose.Ends[i] = limbs[i].End.position; pose.EndRotations[i] = limbs[i].End.rotation; }
            return pose;
        }

        public void SetRest() => rest = Capture();

        public void Begin(Pose from, Pose to, bool sleeping)
        {
            start = from; finish = to; lyingDown = sleeping;
            Pose standing = sleeping ? from : to;
            BuildSeat(standing);
            BuildRecline();
            Apply(start);
        }

        public void Apply(Pose pose)
        {
            model.SetPositionAndRotation(pose.Position, pose.Rotation);
            for (int i = 0; i < bones.Length; i++)
            { bones[i].localRotation = pose.Bones[i]; bones[i].localScale = pose.Scales[i]; }
        }

        private void BuildSeat(Pose standing)
        {
            Apply(standing);
            // Face the open side of the bed; the headboard remains to the sleeper's left.
            model.rotation = uprightRotation;
            Vector3 pelvis = new Vector3(rest.Hip.x, rest.Hip.y - .015f,
                MosquitoRoomLayout.BedZ + MosquitoRoomLayout.BedSize.z * .49f);
            model.position += pelvis - hips.position;
            for (int i = 0; i < limbs.Length; i++)
            {
                if (!limbs[i].Valid) continue;
                float side = i % 2 == 0 ? -1 : 1;
                Vector3 target;
                if (i < 2)
                {
                    float thigh = Vector3.Distance(limbs[i].Upper.position, limbs[i].Lower.position);
                    float shin = Vector3.Distance(limbs[i].Lower.position, limbs[i].End.position);
                    // Short characters dangle their feet; reaching for the floor would
                    // pull their thighs down through the mattress.
                    target = limbs[i].Upper.position + Vector3.forward * (thigh * .98f) - Vector3.up * (shin * .98f);
                }
                else target = pelvis + new Vector3(side * .30f, 0, -.09f);
                if (i >= 2) target.y = MosquitoRoomLayout.MattressHeight + .10f;
                Vector3 pole = pelvis + (i < 2 ? new Vector3(side * .22f, -.12f, .65f) : new Vector3(side * .65f, .18f, -.1f));
                limbs[i].Reach(target, pole, i >= 2 ? limbs[i].SupportRotation(Vector3.forward) : limbs[i].End.rotation);
            }
            seat = Capture();
        }

        private void BuildRecline()
        {
            Apply(rest);
            Vector3 pelvis = Vector3.Lerp(seat.Hip, rest.Hip, .80f);
            pelvis.y += .035f;
            model.rotation = Quaternion.AngleAxis(-ReclineDegrees, Vector3.forward) * rest.Rotation;
            model.position += pelvis - hips.position;
            for (int i = 0; i < limbs.Length; i++)
            {
                if (!limbs[i].Valid) continue;
                float side = i % 2 == 0 ? -1 : 1;
                Vector3 target = i < 2 ? rest.Ends[i] + Vector3.up * .05f :
                    pelvis + new Vector3(-.20f, 0, side * .30f);
                if (i >= 2) target.y = MosquitoRoomLayout.MattressHeight + .075f;
                limbs[i].Reach(target, pelvis + new Vector3(i < 2 ? .4f : -.4f, .5f, side * .5f),
                    i >= 2 ? limbs[i].SupportRotation(Vector3.right) : rest.EndRotations[i]);
            }
            recline = Capture();
        }

        public void Sample(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (lyingDown)
            {
                if (progress < SeatFraction) Blend(start, seat, progress / SeatFraction, 0);
                else if (progress < ReclineFraction) Blend(seat, recline, (progress - SeatFraction) / (ReclineFraction - SeatFraction), FootLift);
                else Blend(recline, finish, (progress - ReclineFraction) / (1 - ReclineFraction), .025f);
            }
            else
            {
                if (progress < .25f) Blend(start, recline, progress / .25f, .025f);
                else if (progress < .65f) Blend(recline, seat, (progress - .25f) / .4f, FootLift);
                else Blend(seat, finish, (progress - .65f) / .35f, 0);
            }
        }

        private void Blend(Pose a, Pose b, float t, float footLift)
        {
            float eased = Mathf.SmoothStep(0, 1, t);
            model.SetPositionAndRotation(Vector3.Lerp(a.Position, b.Position, eased), Quaternion.Slerp(a.Rotation, b.Rotation, eased));
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localRotation = Quaternion.Slerp(a.Bones[i], b.Bones[i], eased);
                bones[i].localScale = Vector3.Lerp(a.Scales[i], b.Scales[i], eased);
            }
            model.position += Vector3.Lerp(a.Hip, b.Hip, eased) - hips.position;
            for (int i = 0; i < limbs.Length; i++)
            {
                if (!limbs[i].Valid) continue;
                float arc = i < 2 ? footLift : footLift == 0 ? .14f : .03f;
                Vector3 target = Vector3.Lerp(a.Ends[i], b.Ends[i], eased) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * arc);
                Vector3 pole = limbs[i].Lower.position + (i < 2 ? Vector3.up * .35f : model.right * (i % 2 == 0 ? -.15f : .15f));
                limbs[i].Reach(target, pole, Quaternion.Slerp(a.EndRotations[i], b.EndRotations[i], eased));
            }
        }

        public void Breathe(float time, float reaction, float side)
        {
            Apply(rest);
            if (chest != null)
            {
                float breath = (Mathf.Sin(time * 1.55f) + 1) * .5f;
                chest.localScale = Vector3.Scale(chest.localScale, new Vector3(1 + breath * .006f, 1, 1 + breath * .012f));
            }
            if (neck != null) neck.rotation = Quaternion.AngleAxis(reaction * side * 3f, Vector3.up) * neck.rotation;
            if (head != null) head.rotation = Quaternion.AngleAxis(reaction * 2f, Vector3.forward) * head.rotation;
            var arm = limbs[side < 0 ? 2 : 3];
            if (arm.Valid) arm.Upper.rotation = Quaternion.AngleAxis(reaction * side * 2f, Vector3.right) * arm.Upper.rotation;
        }
    }
}
