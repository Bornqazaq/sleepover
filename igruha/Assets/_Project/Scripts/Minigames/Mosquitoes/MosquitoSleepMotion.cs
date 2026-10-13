using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    // A short bed performance with supported pelvis, planted hands and lifted feet.
    // Pose endpoints are built once per transition; rendering allocates nothing.
    public sealed class MosquitoSleepMotion
    {
        private const float ReadyFraction = .18f, SeatFraction = .38f, ReclineFraction = .84f;
        private const float ReclineDegrees = 22f, FootLift = .48f, StepLift = .10f;
        private const float SupportRelease = .61f, HandLift = .06f;
        private const float HeadSettleStart = .87f, NeckAnticipation = 12f;
        private readonly Transform model, hips, chest, neck, head;
        private readonly Quaternion uprightRotation;
        private readonly Transform[] bones;
        private readonly MosquitoPoseLimb[] limbs;
        private Pose start, finish, ready, seat, recline, rest;
        private bool lyingDown, startled;

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

        public void Begin(Pose from, Pose to, bool sleeping, bool forcedWake = false)
        {
            start = from; finish = to; lyingDown = sleeping; startled = forcedWake;
            Pose standing = sleeping ? from : to;
            BuildSeat(standing);
            BuildReady(standing);
            BuildRecline();
            Apply(start);
        }

        private void BuildReady(Pose standing)
        {
            Apply(standing);
            model.rotation = uprightRotation;
            Vector3 pelvis = new Vector3(seat.Hip.x, standing.Hip.y,
                MosquitoRoomLayout.BedZ + MosquitoRoomLayout.BedSize.z * .5f + .45f);
            model.position += pelvis - hips.position;
            ready = Capture();
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
            float path = lyingDown ? progress : 1 - (startled ? Mathf.Pow(progress, .68f) : progress);
            Pose standing = lyingDown ? start : finish;
            Pose sleeping = lyingDown ? finish : start;
            if (path < ReadyFraction) Blend(standing, ready, path / ReadyFraction);
            else if (path < SeatFraction) Blend(ready, seat, Range(path, ReadyFraction, SeatFraction));
            else if (path < ReclineFraction) Blend(seat, recline, Range(path, SeatFraction, ReclineFraction));
            else Blend(recline, sleeping, Range(path, ReclineFraction, 1));

            for (int i = 0; i < limbs.Length; i++)
            {
                if (!limbs[i].Valid) continue;
                if (i < 2)
                {
                    if (path < ReadyFraction)
                        Reach(i, standing, ready, Range(path, i == 0 ? 0 : .07f, i == 0 ? .12f : ReadyFraction), StepLift);
                    else if (path < SeatFraction)
                        Reach(i, ready, seat, Range(path, ReadyFraction, SeatFraction), 0);
                    else
                        Reach(i, seat, sleeping, Range(path, i == 0 ? .38f : .56f, i == 0 ? .69f : .87f), FootLift);
                }
                else
                {
                    if (path < SeatFraction)
                        Reach(i, standing, seat, Range(path, i == 2 ? .09f : .18f, SeatFraction), HandLift);
                    else if (i == 2 && path < SupportRelease)
                        Reach(i, seat, seat, 0, 0);
                    else
                        Reach(i, seat, sleeping, Range(path, i == 2 ? SupportRelease : .48f, i == 2 ? .96f : .88f), HandLift);
                }
            }
            // Shoulders arrive first; the head eases onto the pillow last.
            if (neck != null)
            {
                float anticipation = Range(path, SeatFraction, ReclineFraction) * (1 - Range(path, HeadSettleStart, 1));
                neck.rotation = Quaternion.AngleAxis(-NeckAnticipation * anticipation, Vector3.forward) * neck.rotation;
            }
        }

        private static float Range(float value, float from, float to) => Mathf.Clamp01((value - from) / (to - from));

        private void Blend(Pose a, Pose b, float t)
        {
            float eased = Mathf.SmoothStep(0, 1, t);
            model.SetPositionAndRotation(Vector3.Lerp(a.Position, b.Position, eased), Quaternion.Slerp(a.Rotation, b.Rotation, eased));
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localRotation = Quaternion.Slerp(a.Bones[i], b.Bones[i], eased);
                bones[i].localScale = Vector3.Lerp(a.Scales[i], b.Scales[i], eased);
            }
            model.position += Vector3.Lerp(a.Hip, b.Hip, eased) - hips.position;
        }

        private void Reach(int i, Pose a, Pose b, float t, float lift)
        {
            float eased = Mathf.SmoothStep(0, 1, t);
            Vector3 target = Vector3.Lerp(a.Ends[i], b.Ends[i], eased) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * lift);
            Vector3 pole = limbs[i].Lower.position + (i < 2 ? Vector3.up * .35f : model.right * (i % 2 == 0 ? -.15f : .15f));
            limbs[i].Reach(target, pole, Quaternion.Slerp(a.EndRotations[i], b.EndRotations[i], eased));
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
