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
        private Vector3 originalPosition;
        private Quaternion originalRotation;
        private bool originalEnabled, sleeping;
        private AnimatorCullingMode originalCulling;
        private GameObject shell;
        private readonly List<SleepContact> sleepContacts = new List<SleepContact>(10);
        private Transform cameraTarget;
        public const float SurfaceOffset = .14f;
        private const float LandingClearance = .02f;
        private readonly Vector3[] landingPoints = new Vector3[7];
        private SphereCollider landingProbe;
        private sealed class SleepContact { public MeshCollider Collider; public Mesh Mesh; public Vector3 LocalCenter; public Quaternion LocalRotation; }
        private readonly List<Part> parts = new List<Part>();
        private readonly List<Vector3> points = new List<Vector3>(1024);
        private SkinnedMeshRenderer[] skins;
        private readonly List<int> groups = new List<int>();
        public float BackContactHeight { get; private set; }
        private float reaction;
        private float transitionElapsed, transitionDuration;
        private MosquitoSleepMotion motion;
        private MosquitoSleepMotion.Pose originalPose, capturedStart;
        private float reactionSide = 1;
        private Transform chestBone;
        public Bounds SkinBounds { get; private set; }
        public Bounds MeasureSkinBounds() { ReadSkin(); return SkinBounds; }
        public int ContactCount => parts.Count;
        public Vector3 BiteSurfacePoint { get; private set; }
        public Transform CameraTarget => cameraTarget;
        public bool OwnsContact(Collider collider) => shell != null && collider.transform.IsChildOf(shell.transform);
        private sealed class Part { public Transform A, B; public CapsuleCollider Collider; public float Radius; }

        public void Initialize(Animator value)
        {
            animator = value;
            if (animator == null) return;
            model = animator.transform;
            chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);
            motion = new MosquitoSleepMotion(animator);
            originalPose = motion.Capture();
            originalPosition = model.localPosition; originalRotation = model.localRotation;
            originalEnabled = animator.enabled; originalCulling = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            shell = new GameObject("MosquitoGiantContacts");
            shell.hideFlags = HideFlags.DontSave;
            if (Application.isPlaying) shell.hideFlags = HideFlags.None;
            shell.transform.SetParent(transform, true);
            var rb = shell.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            landingProbe = shell.AddComponent<SphereCollider>(); landingProbe.radius = .12f; landingProbe.enabled = false;
            cameraTarget = new GameObject("MosquitoSleepCameraTarget").transform;
            cameraTarget.gameObject.hideFlags = HideFlags.DontSave;
            cameraTarget.SetParent(transform, false);
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
        public void SetSleeping(bool value, float duration = 0, Vector3? standingPosition = null, Quaternion? standingRotation = null)
        {
            if (model == null) return;
            if (sleeping == value)
            {
                capturedStart = null;
                if (duration <= 0 && transitionElapsed < transitionDuration)
                {
                    motion.Sample(1); transitionElapsed = transitionDuration;
                    if (!sleeping) FinishWake();
                }
                return;
            }
            var from = capturedStart ?? motion.Capture(); capturedStart = null;
            motion.Apply(originalPose);
            transitionDuration = duration; transitionElapsed = 0;
            sleeping = value; model.localPosition = originalPosition; model.localRotation = originalRotation;
            animator.enabled = true; animator.speed = 1;
            animator.Play("Base Layer.Idle", 0, 0); animator.Update(0);
            if (!sleeping && standingPosition.HasValue && standingRotation.HasValue)
                model.SetPositionAndRotation(standingPosition.Value + standingRotation.Value * Vector3.Scale(originalPosition, transform.lossyScale),
                    standingRotation.Value * originalRotation);
            if (sleeping)
            {
                animator.enabled = false;
                RelaxArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, -1);
                RelaxArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 1);
                // Rigidbody teleport rotation reaches Transform later; pose directly in bed world axes.
                model.rotation = Quaternion.Euler(0, 90, 0) * Quaternion.Euler(-90, 0, 0) * originalRotation;
                ReadSkin();
                // The headward end of the actual skin stays inside the headboard for every roster size.
                Vector3 shift = new Vector3(MosquitoRoomLayout.HeadSkinX - SkinBounds.min.x,
                    MosquitoRoomLayout.MattressHeight - SkinBounds.min.y, MosquitoRoomLayout.BedZ - SkinBounds.center.z);
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
                ReadSkin();
                model.position += Vector3.right * (MosquitoRoomLayout.HeadSkinX - SkinBounds.min.x);
                ReadSkin(); BackContactHeight = BackHeight();
                ReadSkin();
                // The abdomen is clear of the head and shoulders on every roster shape.
                Vector3 chest = Vector3.Lerp(animator.GetBoneTransform(HumanBodyBones.Chest).position,
                    animator.GetBoneTransform(HumanBodyBones.Hips).position, .55f);
                FitSleepingContacts();
                transitionElapsed = transitionDuration;
                RefreshContacts();
                // ComputePenetration requires an enabled query shape. Disable it again
                // synchronously, before the next physics step can simulate this helper.
                landingProbe.enabled = true;
                try
                {
                    for (int slot = 0; slot < landingPoints.Length; slot++)
                        landingPoints[slot] = FindLanding(chest + ContactOffset(slot));
                }
                finally { landingProbe.enabled = false; }
                BiteSurfacePoint = landingPoints[4] - Vector3.up * SurfaceOffset;
                motion.SetRest();
            }
            if (duration > 0)
            {
                motion.Begin(from, motion.Capture(), sleeping);
                transitionElapsed = 0;
                animator.enabled = false;
            }
            else if (!sleeping) FinishWake();
            RefreshContacts();
        }
        public void CaptureTransitionStart() { if (motion != null) capturedStart = motion.Capture(); }
        private void FitSleepingContacts()
        {
            foreach (var contact in sleepContacts)
            {
                contact.Collider.enabled = false;
                if (Application.isPlaying) { Destroy(contact.Collider.gameObject); Destroy(contact.Mesh); }
                else { DestroyImmediate(contact.Collider.gameObject); DestroyImmediate(contact.Mesh); }
            }
            sleepContacts.Clear();
            // Cook small convex hulls from the existing skin probes once per sleep.
            // A box around a curved torso makes an invisible roof beside the neck.
            for (int group = 0; group < CharacterSkinProbes.GroupCount; group++)
            {
                int slices = group == 4 ? 2 : 1;
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                for (int i = 0; i < points.Count; i++) if (groups[i] == group)
                { min = Mathf.Min(min, points[i].x); max = Mathf.Max(max, points[i].x); }
                if (float.IsInfinity(min)) continue;
                for (int slice = 0; slice < slices; slice++)
                {
                    float neck = animator.GetBoneTransform(HumanBodyBones.Neck).position.x;
                    float left = slices == 1 || slice == 0 ? min : neck - .12f;
                    float right = slices == 1 || slice == 1 ? max : neck + .12f;
                    Bounds bounds = default; bool any = false;
                    for (int i = 0; i < points.Count; i++)
                    {
                        if (groups[i] != group || points[i].x < left || points[i].x > right) continue;
                        if (!any) { bounds = new Bounds(points[i], Vector3.zero); any = true; }
                        else bounds.Encapsulate(points[i]);
                    }
                    if (!any) continue;
                    var go = new GameObject("SleepingSkinContact"); go.hideFlags = HideFlags.DontSave;
                    if (Application.isPlaying) go.hideFlags = HideFlags.None;
                    go.layer = 12; go.transform.SetParent(shell.transform, true);
                    var vertices = new List<Vector3>();
                    for (int i = 0; i < points.Count; i++)
                        if (groups[i] == group && points[i].x >= left && points[i].x <= right)
                            vertices.Add(points[i] - bounds.center);
                    if (vertices.Count < 4) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); continue; }
                    var mesh = new Mesh { name = "MosquitoSleepingSkin", hideFlags = HideFlags.DontSave };
                    mesh.SetVertices(vertices);
                    // Convex cooking builds the hull from these vertices; the source fan
                    // merely references the point cloud without reading the million-vertex skin.
                    var triangles = new int[(vertices.Count - 2) * 3];
                    for (int i = 1; i < vertices.Count - 1; i++)
                    { triangles[(i - 1) * 3] = 0; triangles[(i - 1) * 3 + 1] = i; triangles[(i - 1) * 3 + 2] = i + 1; }
                    mesh.triangles = triangles;
                    var collider = go.AddComponent<MeshCollider>(); collider.convex = true; collider.sharedMesh = mesh;
                    collider.includeLayers = 1 << 12; collider.excludeLayers = ~(1 << 12); collider.layerOverridePriority = 100;
                    sleepContacts.Add(new SleepContact { Collider = collider, Mesh = mesh,
                        LocalCenter = model.InverseTransformPoint(bounds.center), LocalRotation = Quaternion.Inverse(model.rotation) });
                }
            }
        }
        public static Vector3 ContactOffset(int slot) => new Vector3((slot / 3 - 1) * .10f, 0, (slot % 3 - 1) * .12f);
        public Vector3 LandingPoint(int slot) => landingPoints[Mathf.Clamp(slot, 0, landingPoints.Length - 1)];
        private Vector3 FindLanding(Vector3 point)
        {
            float clear = SkinBounds.max.y + .3f;
            for (float height = clear - .02f; height >= SkinBounds.min.y; height -= .02f)
            {
                point.y = height;
                if (!OverlapsSkin(point)) { clear = height; continue; }
                float blocked = height;
                for (int pass = 0; pass < 8; pass++)
                {
                    point.y = (clear + blocked) * .5f;
                    if (OverlapsSkin(point)) blocked = point.y; else clear = point.y;
                }
                point.y = clear + LandingClearance;
                return point;
            }
            Debug.LogError("Mosquitoes: no sleeping skin below the landing point", this);
            point.y = SkinBounds.max.y + SurfaceOffset;
            return point;
        }
        private bool OverlapsSkin(Vector3 point)
        {
            foreach (var contact in sleepContacts)
                if (Physics.ComputePenetration(landingProbe, point, Quaternion.identity, contact.Collider,
                    contact.Collider.transform.position, contact.Collider.transform.rotation, out _, out _)) return true;
            return false;
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
                return landingPoints[4];
            }
        }
        public void React(Vector3 contact)
        {
            reaction = .42f;
            reactionSide = contact.z < MosquitoRoomLayout.BedZ ? -1 : 1;
        }
        private void LateUpdate()
        {
            if (model == null) return;
            reaction = Mathf.Max(0, reaction - Time.deltaTime);
            if (transitionElapsed < transitionDuration)
            {
                transitionElapsed = Mathf.Min(transitionDuration, transitionElapsed + Time.deltaTime);
                motion.Sample(transitionElapsed / transitionDuration);
                if (transitionElapsed >= transitionDuration && !sleeping) FinishWake();
            }
            else if (sleeping)
            {
                float flinch = Mathf.Sin(Mathf.Clamp01(reaction / .42f) * Mathf.PI);
                motion.Breathe(Time.time, flinch, reactionSide);
            }
            RefreshContacts();
            if (cameraTarget != null && chestBone != null) cameraTarget.position = chestBone.position;
        }
        private void FinishWake()
        {
            model.localPosition = originalPosition; model.localRotation = originalRotation;
            animator.enabled = originalEnabled;
        }
        public void RefreshContacts()
        {
            bool resting = sleeping && transitionElapsed >= transitionDuration;
            foreach (var p in parts)
            {
                p.Collider.enabled = !resting;
                Vector3 axis = p.B.position - p.A.position;
                p.Collider.transform.SetPositionAndRotation((p.A.position + p.B.position) * .5f,
                    axis.sqrMagnitude > .0001f ? Quaternion.FromToRotation(Vector3.up, axis) : Quaternion.identity);
                p.Collider.radius = p.Radius; p.Collider.height = axis.magnitude + p.Radius * 2;
            }
            foreach (var p in sleepContacts)
            {
                p.Collider.enabled = resting;
                p.Collider.transform.SetPositionAndRotation(model.TransformPoint(p.LocalCenter), model.rotation * p.LocalRotation);
            }
        }
        public void Restore()
        {
            if (model != null)
            {
                motion.Apply(originalPose);
                model.localPosition = originalPosition; model.localRotation = originalRotation;
                animator.enabled = originalEnabled; animator.cullingMode = originalCulling;
            }
            if (shell != null) { shell.SetActive(false); if (Application.isPlaying) Destroy(shell); else DestroyImmediate(shell); }
            if (cameraTarget != null) { if (Application.isPlaying) Destroy(cameraTarget.gameObject); else DestroyImmediate(cameraTarget.gameObject); }
            foreach (var contact in sleepContacts)
                if (Application.isPlaying) Destroy(contact.Mesh); else DestroyImmediate(contact.Mesh);
            sleepContacts.Clear(); parts.Clear(); model = null;
        }
        private void OnDestroy() => Restore();
    }
}
