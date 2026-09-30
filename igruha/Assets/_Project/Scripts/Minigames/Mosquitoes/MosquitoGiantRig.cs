using System.Collections.Generic;
using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    // Temporary, minigame-local pose and collision shell. Never edits the roster assets.
    public sealed class MosquitoGiantRig : MonoBehaviour
    {
        private Animator animator;
        private Transform model;
        private Vector3 originalPosition, sleepingPosition;
        private Quaternion originalRotation, sleepingRotation;
        private bool originalEnabled, sleeping;
        private AnimatorCullingMode originalCulling;
        private GameObject shell;
        private readonly List<Part> parts = new List<Part>();
        private readonly List<Vector3> points = new List<Vector3>(1024);
        private SkinnedMeshRenderer[] skins;
        private readonly List<int> groups = new List<int>();
        public float BackContactHeight { get; private set; }
        private float reaction;
        public Bounds SkinBounds { get; private set; }
        public Bounds MeasureSkinBounds() { ReadSkin(); return SkinBounds; }
        public int ContactCount => parts.Count;
        public Vector3 BiteSurfacePoint { get; private set; }
        private sealed class Part { public Transform A, B; public CapsuleCollider Collider; public float Radius; }

        public void Initialize(Animator value)
        {
            animator = value; model = animator.transform;
            originalPosition = model.localPosition; originalRotation = model.localRotation;
            originalEnabled = animator.enabled; originalCulling = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            shell = new GameObject("MosquitoGiantContacts");
            var rb = shell.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            ReadSkin();
            AddPart(HumanBodyBones.Hips, HumanBodyBones.Chest, .22f, .52f);
            AddPart(HumanBodyBones.Head, HumanBodyBones.Head, .17f, .34f);
            AddPart(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, .07f, .20f);
            AddPart(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, .06f, .18f);
            AddPart(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, .07f, .20f);
            AddPart(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, .06f, .18f);
            AddPart(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, .09f, .26f);
            AddPart(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, .07f, .22f);
            AddPart(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, .09f, .26f);
            AddPart(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, .07f, .22f);
            RefreshContacts();
        }
        private void AddPart(HumanBodyBones a, HumanBodyBones b, float minimum, float maximum)
        {
            var start = animator.GetBoneTransform(a); var end = animator.GetBoneTransform(b);
            if (start == null || end == null) return;
            // Measure nearby skin in the upright pose, rather than using one character's width.
            float radius = minimum; Vector3 axis = end.position - start.position;
            foreach (var p in points)
            {
                float t = axis.sqrMagnitude < .0001f ? 0 : Vector3.Dot(p - start.position, axis) / axis.sqrMagnitude;
                if (t < -.05f || t > 1.05f) continue;
                float d = Vector3.Distance(p, start.position + axis * Mathf.Clamp01(t));
                if (d <= maximum) radius = Mathf.Max(radius, d);
            }
            var go = new GameObject(a + "Contact"); go.transform.SetParent(shell.transform); go.layer = 12;
            var c = go.AddComponent<CapsuleCollider>(); c.direction = 1;
            // Per-collider overrides: only flying bodies collide; player/environment masks stay intact.
            c.includeLayers = 1 << 12; c.excludeLayers = ~(1 << 12); c.layerOverridePriority = 100;
            parts.Add(new Part { A = start, B = end, Radius = radius, Collider = c });
        }
        public void SetSleeping(bool value)
        {
            if (sleeping == value) return;
            sleeping = value; model.localPosition = originalPosition; model.localRotation = originalRotation;
            animator.enabled = true; animator.speed = 1;
            animator.Play("Base Layer.Idle", 0, 0); animator.Update(0);
            if (sleeping)
            {
                animator.enabled = false;
                RelaxArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, -1);
                RelaxArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 1);
                // Rigidbody teleport rotation reaches Transform later; pose directly in bed world axes.
                model.rotation = Quaternion.Euler(0, 90, 0) * Quaternion.Euler(-90, 0, 0) * originalRotation;
                ReadSkin();
                // The headward end of the actual skin stays inside the headboard for every roster size.
                Vector3 shift = new Vector3(-2.97f - SkinBounds.min.x, .665f - SkinBounds.min.y, -1.1f - SkinBounds.center.z);
                model.position += shift;
                // Support the back/pelvis, not a finger or heel far below the torso.
                ReadSkin();
                model.position += Vector3.up * (.674f - BackHeight());
                RestLeg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
                RestLeg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
                SupportFoot(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0);
                SupportFoot(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 1);
                ReadSkin();
                RestArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 2);
                RestArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 3);
                RestHead();
                ReadSkin(); BackContactHeight = BackHeight();
                sleepingPosition = model.position; sleepingRotation = model.rotation;
                ReadSkin();
                Vector3 chest = animator.GetBoneTransform(HumanBodyBones.Chest).position;
                float top = chest.y;
                foreach (var p in points) if (Mathf.Abs(p.x - chest.x) < .22f && Mathf.Abs(p.z - chest.z) < .23f) top = Mathf.Max(top, p.y);
                BiteSurfacePoint = new Vector3(chest.x, top + .025f, chest.z);
            }
            RefreshContacts();
        }
        private void RestLeg(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot, float ankleHeight = .78f)
        {
            var a = animator.GetBoneTransform(upper); var b = animator.GetBoneTransform(lower); var c = animator.GetBoneTransform(foot);
            if (a == null || b == null || c == null) return;
            Quaternion footRotation = c.rotation;
            float l1 = Vector3.Distance(a.position, b.position), l2 = Vector3.Distance(b.position, c.position);
            float reach = (l1 + l2) * .97f;
            float drop = Mathf.Min(Mathf.Max(0, a.position.y - ankleHeight), reach * .8f);
            Vector3 target = new Vector3(a.position.x + Mathf.Sqrt(reach * reach - drop * drop), a.position.y - drop, c.position.z);
            Vector3 direction = (target - a.position).normalized;
            float distance = Mathf.Min(Vector3.Distance(target, a.position), (l1 + l2) * .995f);
            target = a.position + direction * distance;
            float along = (l1 * l1 + distance * distance - l2 * l2) / (2 * distance);
            Vector3 bend = Vector3.ProjectOnPlane(Vector3.up, direction).normalized;
            Vector3 knee = a.position + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, l1 * l1 - along * along));
            a.rotation = Quaternion.FromToRotation(b.position - a.position, knee - a.position) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, target - b.position) * b.rotation;
            c.rotation = footRotation;
        }
        private void RelaxArm(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, float side)
        {
            var a = animator.GetBoneTransform(upper); var b = animator.GetBoneTransform(lower); var c = animator.GetBoneTransform(hand);
            if (a == null || b == null || c == null) return;
            var right = transform.right; var forward = transform.forward;
            a.rotation = Quaternion.FromToRotation(b.position - a.position, right * side * .12f - Vector3.up + forward * .08f) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, right * side * .05f - Vector3.up + forward * .16f) * b.rotation;
        }
        private void ReadSkin()
        {
            points.Clear(); groups.Clear(); var library = CharacterSkinProbes.Shared;
            foreach (var skin in skins)
            {
                if (library == null || !library.TryGet(skin.sharedMesh, out var entry)) continue;
                var bones = skin.bones; var matrices = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++) matrices[i] = bones[i].localToWorldMatrix * entry.BindPoses[i];
                foreach (var p in entry.Probes)
                {
                    Vector3 world = Vector3.zero;
                    for (int j = 0; j < 4; j++) if (p.Weights[j] > 0) world += matrices[p.Bone(j)].MultiplyPoint3x4(p.Position) * p.Weights[j];
                    points.Add(world); groups.Add(p.Group);
                }
            }
            if (points.Count == 0) foreach (var skin in skins) { points.Add(skin.bounds.min); points.Add(skin.bounds.max); }
            var bounds = new Bounds(points[0], Vector3.zero); foreach (var p in points) bounds.Encapsulate(p); SkinBounds = bounds;
        }
        private void SupportFoot(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot, int group)
        {
            float height=.78f;
            for(int pass=0;pass<6;pass++)
            {
                ReadSkin(); float lowest=float.PositiveInfinity;
                for(int i=0;i<points.Count;i++) if(groups[i]==group) lowest=Mathf.Min(lowest,points[i].y);
                if(float.IsInfinity(lowest)||Mathf.Abs(.683f-lowest)<.003f) break;
                height+=.683f-lowest; RestLeg(upper,lower,foot,height);
            }
        }
        private float BackHeight()
        {
            var hip = animator.GetBoneTransform(HumanBodyBones.Hips).position;
            var chest = animator.GetBoneTransform(HumanBodyBones.Chest).position;
            float lowest = float.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
                if (groups[i] == 4 && points[i].x >= chest.x - .30f && points[i].x <= hip.x + .12f)
                    lowest = Mathf.Min(lowest, points[i].y);
            return float.IsInfinity(lowest) ? SkinBounds.min.y : lowest;
        }
        private void RestArm(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, int group)
        {
            var a = animator.GetBoneTransform(upper); var b = animator.GetBoneTransform(lower); var c = animator.GetBoneTransform(hand);
            if (a == null || b == null || c == null) return;
            // Raise only the arm until its skin rests on the bedding; the torso stays supported.
            for (int pass = 0; pass < 20; pass++)
            {
                ReadSkin(); float lowest = float.PositiveInfinity;
                for (int i = 0; i < points.Count; i++) if (groups[i] == group) lowest = Mathf.Min(lowest, points[i].y);
                float delta = .683f - lowest;
                if (float.IsInfinity(lowest) || Mathf.Abs(delta) < .003f) break;
                Vector3 target = c.position + Vector3.up * delta;
                a.rotation = Quaternion.FromToRotation(c.position - a.position, target - a.position) * a.rotation;
            }
        }
        private void RestHead()
        {
            var neck = animator.GetBoneTransform(HumanBodyBones.Neck); var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (neck == null || head == null) return;
            for (int pass = 0; pass < 6; pass++)
            {
                ReadSkin(); float lowest = float.PositiveInfinity;
                for (int i = 0; i < points.Count; i++) if (groups[i] == 4 && points[i].x < neck.position.x) lowest = Mathf.Min(lowest, points[i].y);
                float delta = .77f - lowest;
                if (float.IsInfinity(lowest) || Mathf.Abs(delta) < .004f) break;
                neck.rotation = Quaternion.FromToRotation(head.position - neck.position, head.position + Vector3.up * delta - neck.position) * neck.rotation;
            }
        }
        public Vector3 BitePoint
        {
            get
            {
                if (parts.Count == 0) return transform.position + Vector3.up;
                var torso = parts[0];
                if (!sleeping) return torso.B.position + transform.forward * (torso.Radius + .14f);
                // Find the exposed upper surface of the entire shell at the chest, including
                // neighbouring head/shoulder capsules. A point inside another capsule ejects the flyer.
                Vector3 chest = torso.B.position; float top = chest.y;
                foreach (var part in parts)
                    for (int sample = 0; sample <= 16; sample++)
                    {
                        Vector3 center = Vector3.Lerp(part.A.position, part.B.position, sample / 16f);
                        float dx = center.x - chest.x, dz = center.z - chest.z;
                        float square = part.Radius * part.Radius - dx * dx - dz * dz;
                        if (square >= 0) top = Mathf.Max(top, center.y + Mathf.Sqrt(square));
                    }
                return new Vector3(chest.x, top + .145f, chest.z);
            }
        }
        public void React() => reaction = .32f;
        private void LateUpdate()
        {
            if (model == null) return;
            reaction = Mathf.Max(0, reaction - Time.deltaTime);
            if (sleeping)
            {
                model.SetPositionAndRotation(sleepingPosition, sleepingRotation);
            }
            RefreshContacts();
        }
        public void RefreshContacts()
        {
            foreach (var p in parts)
            {
                Vector3 axis = p.B.position - p.A.position;
                p.Collider.transform.SetPositionAndRotation((p.A.position + p.B.position) * .5f,
                    axis.sqrMagnitude > .0001f ? Quaternion.FromToRotation(Vector3.up, axis) : Quaternion.identity);
                p.Collider.radius = p.Radius; p.Collider.height = axis.magnitude + p.Radius * 2;
            }
        }
        public void Restore()
        {
            if (model != null)
            {
                model.localPosition = originalPosition; model.localRotation = originalRotation;
                animator.enabled = originalEnabled; animator.cullingMode = originalCulling;
            }
            if (shell != null) { shell.SetActive(false); if (Application.isPlaying) Destroy(shell); else DestroyImmediate(shell); }
            parts.Clear(); model = null;
        }
        private void OnDestroy() => Restore();
    }
}
