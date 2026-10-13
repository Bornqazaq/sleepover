using System.Collections.Generic;
using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    /// <summary>Pose only the miniature copy; imported wing axes never define the flap plane.</summary>
    public sealed class MosquitoFlightVisual
    {
        private const float FollowRate = 14f;
        private const float ContactPitch = 32f;
        private const float VelocityFollow = 9f, AccelerationFollow = 6f;
        private const float FlightPitch = 30f, BrakePitch = 10f, MaxBank = 24f;
        public const float HitDuration = .32f;
        private const float HitTravel = .24f;
        private readonly Transform root;
        private readonly Vector3 scale;
        private readonly List<Transform> wings = new List<Transform>(4);
        private readonly List<Quaternion> rest = new List<Quaternion>(4);
        private readonly List<float> side = new List<float>(4);
        private readonly Renderer[] renderers;
        private float phase, contact, recoil;
        private Vector3 previousVelocity, smoothVelocity, smoothAcceleration, hitDirection;
        private float airTime, hitRemaining;
        private bool velocityInitialized;
        private Quaternion flightRotation = Quaternion.identity;
        private readonly MosquitoAirPose airPose;
        private readonly List<Transform> bones = new List<Transform>();
        private readonly List<Quaternion> flightBones = new List<Quaternion>();
        private readonly List<Quaternion> landingBones = new List<Quaternion>();
        private Transform neck;
        private Vector3 landingOffset;

        public MosquitoFlightVisual(Transform root, int seed)
        {
            this.root = root; scale = root.localScale; phase = seed * 1.7f; airTime = seed * .73f;
            renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.StartsWith("WingPivot_")) continue;
                wings.Add(child); rest.Add(child.localRotation);
                // All four imported hinges share the same origin; their positions cannot
                // identify left/right. The exported names carry the wing side.
                side.Add(child.name.StartsWith("WingPivot_-1_") ? -1 : 1);
            }
            BuildLandingPose();
            airPose = new MosquitoAirPose(root, root.GetComponentInChildren<Animator>(true));
        }
        private void BuildLandingPose()
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman) return;
            FitWingMount(animator);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone == null) continue;
                bones.Add(bone); flightBones.Add(bone.localRotation);
            }
            Quaternion original = root.localRotation;
            Vector3 originalPosition = root.position;
            root.localRotation = Quaternion.Euler(ContactPitch, 0, 0);
            // Fold at the waist; keep the legs under the pelvis rather than inverting
            // the entire character. Imported local bone axes differ across the roster.
            Bend(animator.GetBoneTransform(HumanBodyBones.Spine), 40);
            Bend(animator.GetBoneTransform(HumanBodyBones.Chest), 30);
            Bend(neck, 12);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            landingOffset = Vector3.up * (-.07f - root.parent.InverseTransformPoint(hips.position).y);
            root.position += root.parent.TransformVector(landingOffset);
            PoseLimb(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, -1, true);
            PoseLimb(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 1, true);
            PoseLimb(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, -1, false);
            PoseLimb(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 1, false);
            foreach (var bone in bones) landingBones.Add(bone.localRotation);
            for (int i = 0; i < bones.Count; i++) bones[i].localRotation = flightBones[i];
            root.localRotation = original;
            root.position = originalPosition;
        }
        private void FitWingMount(Animator animator)
        {
            Transform mount = null;
            foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.name == "WingMount") { mount = t; break; }
            var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (mount == null || chest == null) return;
            float back = 0;
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!CharacterSkinProbes.Shared.TryGet(skin.sharedMesh, out var entry)) continue;
                var matrices = new Matrix4x4[skin.bones.Length];
                for (int i = 0; i < matrices.Length; i++) matrices[i] = skin.bones[i].localToWorldMatrix * entry.BindPoses[i];
                foreach (var probe in entry.Probes)
                {
                    Vector3 point = Vector3.zero;
                    for (int i = 0; i < 4; i++) if (probe.Weights[i] > 0)
                        point += matrices[probe.Bone(i)].MultiplyPoint3x4(probe.Position) * probe.Weights[i];
                    Vector3 delta = point - chest.position;
                    if (Mathf.Abs(Vector3.Dot(delta, root.up)) < .015f && Mathf.Abs(Vector3.Dot(delta, root.right)) < .025f)
                        back = Mathf.Min(back, Vector3.Dot(delta, root.forward));
                }
            }
            // Put the hinge into this character's back, rather than at a fixed distance
            // that leaves a visible gap once the spine bends.
            if (back < 0) mount.position = chest.position + root.forward * (back + .002f);
        }
        private void Bend(Transform bone, float angle)
        { if (bone != null) bone.rotation = Quaternion.AngleAxis(angle, root.parent.right) * bone.rotation; }
        private void PoseLimb(Animator animator, HumanBodyBones a, HumanBodyBones b, HumanBodyBones c, float sign, bool hand)
        {
            var limb = new MosquitoPoseLimb(animator, a, b, c);
            if (!limb.Valid) return;
            Vector3 target = root.parent.TransformPoint(new Vector3(sign * (hand ? .04f : .028f), -.115f, hand ? .065f : -.03f));
            Vector3 pole = root.parent.TransformPoint(new Vector3(sign * .09f, -.02f, hand ? .10f : .07f));
            limb.Reach(target, pole, hand ? limb.SupportRotation(root.parent.forward) : limb.End.rotation);
        }
        public int VisibleCount
        {
            get { int count = 0; foreach (var r in renderers) if (r != null && r.enabled) count++; return count; }
        }
        public void Bite() => recoil = 1;
        public bool ShowingHit => hitRemaining > 0;
        public void Swat(Vector3 direction)
        {
            hitRemaining = HitDuration;
            hitDirection = direction.sqrMagnitude > .001f ? direction.normalized : Vector3.forward;
        }
        public void Tick(Vector3 velocity, float maxSpeed, bool attached, bool quiet, bool visible, float dt, float biteFraction = 0)
        {
            foreach (var r in renderers) if (r != null) r.enabled = visible;
            float blend = 1 - Mathf.Exp(-FollowRate * dt);
            contact = Mathf.MoveTowards(contact, attached ? 1 : 0, dt * (attached ? 7 : 5));
            recoil = Mathf.MoveTowards(recoil, 0, dt * 3);
            maxSpeed = Mathf.Max(.01f, maxSpeed);
            if (!velocityInitialized) { previousVelocity = velocity; smoothVelocity = velocity; velocityInitialized = true; }
            smoothVelocity = Vector3.Lerp(smoothVelocity, velocity, 1 - Mathf.Exp(-VelocityFollow * dt));
            Vector3 rawAcceleration = (velocity - previousVelocity) / Mathf.Max(.001f, dt);
            smoothAcceleration = Vector3.Lerp(smoothAcceleration, Vector3.ClampMagnitude(rawAcceleration, 40), 1 - Mathf.Exp(-AccelerationFollow * dt));
            previousVelocity = velocity;
            float speed = Mathf.Clamp01(smoothVelocity.magnitude / maxSpeed);
            Vector3 localVelocity = root.parent.InverseTransformDirection(smoothVelocity) / maxSpeed;
            Vector3 acceleration = root.parent.InverseTransformDirection(smoothAcceleration) / 20;
            float pitch = Mathf.Clamp(localVelocity.z * FlightPitch + acceleration.z * BrakePitch + localVelocity.y * 5, -24, 40);
            float bank = Mathf.Clamp(-localVelocity.x * 15 - acceleration.x * 12, -MaxBank, MaxBank);
            Quaternion pose = Quaternion.Euler(Mathf.Lerp(pitch, ContactPitch, contact), 0, bank * (1 - contact));
            flightRotation = Quaternion.Slerp(flightRotation, pose, blend);
            root.localRotation = flightRotation;
            float settled = Mathf.SmoothStep(0, 1, contact);
            for (int i = 0; i < bones.Count; i++) bones[i].localRotation = flightBones[i];
            airTime += dt;
            airPose.Apply(localVelocity, acceleration, airTime, pitch);
            for (int i = 0; i < bones.Count; i++) bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, landingBones[i], settled);
            if (neck != null && attached) Bend(neck, Mathf.Sin(biteFraction * Mathf.PI) * 3 * settled);
            root.position = root.parent.position + root.parent.TransformVector(landingOffset * settled) + Vector3.up *
                ((1 - contact) * Mathf.Sin(phase * .09f) * .009f + Mathf.Sin(recoil * Mathf.PI) * .015f);
            root.localScale = scale;
            phase += dt * Mathf.PI * 2 * (quiet ? 9 : Mathf.Lerp(12, 17, speed));
            for (int i = 0; i < wings.Count; i++)
            {
                // Rotate about the character's forward axis, expressed in each pivot's parent.
                // FBX pivots are rotated 270 degrees: their local Z was a sideways sweep.
                Vector3 axis = wings[i].parent.InverseTransformDirection(root.forward);
                float flap = Mathf.Sin(phase + (i % 2) * .35f) * (quiet ? 22 : 36);
                float angle = Mathf.Lerp(flap, 58, settled) * side[i];
                wings[i].localRotation = Quaternion.AngleAxis(angle, axis) * rest[i];
            }
            if (hitRemaining <= 0) return;
            hitRemaining = Mathf.Max(0, hitRemaining - dt);
            float hit = 1 - hitRemaining / HitDuration;
            root.position += hitDirection * (HitTravel * hit) + Vector3.down * (.20f * hit * hit);
            root.localRotation *= Quaternion.Euler(65 * hit, 0, 140 * hit);
            float size = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, 1, hit));
            root.localScale = Vector3.Scale(scale, Vector3.Lerp(new Vector3(1.3f, .55f, 1.3f), Vector3.one, Mathf.Clamp01(hit * 5))) * size;
            if (hitRemaining <= 0) foreach (var r in renderers) if (r != null) r.enabled = false;
        }
    }
}
