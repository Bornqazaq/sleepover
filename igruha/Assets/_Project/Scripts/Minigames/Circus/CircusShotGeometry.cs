using System.Collections.Generic;
using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>Local framing geometry. Open hatch renderers still obstruct a shot
    /// after their physics colliders have been disabled to release the passenger.</summary>
    internal sealed class CircusShotGeometry
    {
        private const float SkinPadding = .13f;
        private const float VisibilityPadding = .025f;
        private readonly List<Renderer> cageVisuals = new List<Renderer>();
        private readonly List<Transform> bearBones = new List<Transform>();
        private readonly List<Vector3> bearRadii = new List<Vector3>();
        private struct Obstacle
        {
            public bool Active;
            public string Name;
            public Bounds World, Local;
            public Matrix4x4 WorldToLocal;
            public Vector3 PaddingScale;
            public Surface Surface;
        }
        private struct Triangle
        {
            public Vector3 A, Edge1, Edge2;
            public Bounds Bounds;
        }
        private sealed class Surface
        {
            public readonly Triangle[] Triangles;
            private struct Node
            {
                public Bounds Bounds;
                public int Start, Count, Left, Right;
            }
            private sealed class AxisComparer : IComparer<Triangle>
            {
                private readonly int axis;
                public AxisComparer(int value) => axis = value;
                public int Compare(Triangle a, Triangle b) => a.Bounds.center[axis].CompareTo(b.Bounds.center[axis]);
            }
            private static readonly AxisComparer[] Comparers = { new AxisComparer(0), new AxisComparer(1), new AxisComparer(2) };
            private readonly Node[] nodes;
            public Surface(Mesh mesh)
            {
                var vertices = mesh.vertices;
                var indices = mesh.triangles;
                Triangles = new Triangle[indices.Length / 3];
                for (int i = 0; i < Triangles.Length; i++)
                {
                    Vector3 a = vertices[indices[i * 3]], b = vertices[indices[i * 3 + 1]], c = vertices[indices[i * 3 + 2]];
                    var bounds = new Bounds(a, Vector3.zero); bounds.Encapsulate(b); bounds.Encapsulate(c);
                    Triangles[i] = new Triangle { A = a, Edge1 = b - a, Edge2 = c - a, Bounds = bounds };
                }
                var tree = new List<Node>();
                if (Triangles.Length != 0) BuildNode(tree, 0, Triangles.Length);
                nodes = tree.ToArray();
            }

            private int BuildNode(List<Node> tree, int start, int count)
            {
                Bounds bounds = Triangles[start].Bounds;
                for (int i = start + 1; i < start + count; i++) bounds.Encapsulate(Triangles[i].Bounds);
                int index = tree.Count;
                var node = new Node { Bounds = bounds, Start = start, Count = count };
                tree.Add(node);
                if (count > 8)
                {
                    Vector3 size = bounds.size;
                    int axis = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);
                    System.Array.Sort(Triangles, start, count, Comparers[axis]);
                    int half = count / 2;
                    node.Left = BuildNode(tree, start, half); node.Right = BuildNode(tree, start + half, count - half);
                    node.Count = 0; tree[index] = node;
                }
                return index;
            }

            public bool Intersects(Vector3 from, Vector3 to)
            {
                Vector3 segment = to - from; float length = segment.magnitude;
                if (length < .0001f || nodes.Length == 0) return false;
                return Intersects(0, new Ray(from, segment / length), length);
            }

            private bool Intersects(int index, Ray ray, float length)
            {
                Node node = nodes[index];
                if (!node.Bounds.IntersectRay(ray, out float broad) || broad > length) return false;
                if (node.Count == 0) return Intersects(node.Left, ray, length) || Intersects(node.Right, ray, length);
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    Triangle triangle = Triangles[i];
                    // Double-sided: an open door is opaque from either side. The
                    // actual triangles preserve holes in the combined cage mesh.
                    Vector3 cross = Vector3.Cross(ray.direction, triangle.Edge2);
                    float determinant = Vector3.Dot(triangle.Edge1, cross);
                    if (Mathf.Abs(determinant) < .0000001f) continue;
                    float inverse = 1 / determinant;
                    Vector3 relative = ray.origin - triangle.A;
                    float u = Vector3.Dot(relative, cross) * inverse;
                    if (u < 0 || u > 1) continue;
                    Vector3 q = Vector3.Cross(relative, triangle.Edge1);
                    float v = Vector3.Dot(ray.direction, q) * inverse;
                    if (v < 0 || u + v > 1) continue;
                    float hit = Vector3.Dot(triangle.Edge2, q) * inverse;
                    if (hit >= 0 && hit < length - .002f) return true;
                }
                return false;
            }
        }
        private Obstacle[] obstacles = System.Array.Empty<Obstacle>();
        private PlayerController player;
        private PitBear bear;
        private Transform bearHead;
        private Transform[] skinBones;
        private CharacterSkinProbes.Probe[] skinProbes;
        private Matrix4x4[] bindPoses, skinMatrices;
        private int[] usedBones;
        private CapsuleCollider capsule;
        public Bounds PlayerBounds { get; private set; }
        public Bounds BearBounds { get; private set; }
        public Bounds CombinedBounds { get; private set; }
        public Bounds ContactBounds { get; private set; }
        public string LastObstruction { get; private set; }
        private Bounds contactPlayer, contactBear;
        private Vector3 anticipatedTravel;

        public void CaptureContactEnvelope(Vector3 travel, float floor, int solidMask)
        {
            float distance = travel.magnitude;
            if (distance > .001f && Physics.SphereCast(PlayerBounds.center, .35f, travel / distance,
                out RaycastHit hit, distance, solidMask, QueryTriggerInteraction.Ignore))
                travel *= Mathf.Clamp01((hit.distance - .12f) / distance);
            anticipatedTravel = travel;
            contactPlayer = PlayerBounds;
            var future = PlayerBounds; future.center += travel;
            contactPlayer.Encapsulate(future);
            // Reserve room for the lifted foot and the standing-to-prone pose.
            contactPlayer.Expand(new Vector3(.65f, .45f, .65f));
            var lower = contactPlayer.min; lower.y = Mathf.Max(floor + .02f, lower.y);
            var upper = contactPlayer.max; upper.y += .35f;
            contactPlayer.SetMinMax(lower, upper);
            contactBear = BearBounds; contactBear.Expand(.24f);
            var combined = contactBear; combined.Encapsulate(contactPlayer); ContactBounds = combined;
        }

        public bool ClearAnticipatedView(Vector3 camera, float floor, int solidMask)
        {
            var future = PlayerBounds;
            future.center += anticipatedTravel * .5f + Vector3.up * .25f;
            if (!ClearActorView(camera, future, floor, solidMask)) return false;
            future.center = PlayerBounds.center + anticipatedTravel;
            return ClearActorView(camera, future, floor, solidMask);
        }

        public bool ClearActorSegment(Vector3 from, Vector3 to, float padding)
        {
            Vector3 delta = to - from; float length = delta.magnitude;
            if (length < .001f) return true;
            var ray = new Ray(from, delta / length);
            var human = PlayerBounds; var animal = BearBounds;
            human.Expand(padding * 2); animal.Expand(padding * 2);
            return !human.Contains(from) && !animal.Contains(from) &&
                (!human.IntersectRay(ray, out float personHit) || personHit > length) &&
                (!animal.IntersectRay(ray, out float bearHit) || bearHit > length);
        }

        public float ContactDistance(Vector3 focus, Vector3 outward, float fieldOfView, float aspect)
        {
            Quaternion orientation = Quaternion.LookRotation(-outward);
            Vector3 right = orientation * Vector3.right, up = orientation * Vector3.up;
            float halfHeight = Mathf.Tan(fieldOfView * .5f * Mathf.Deg2Rad) * .82f;
            float halfWidth = halfHeight * Mathf.Max(.8f, aspect);
            return Mathf.Max(RequiredDistance(contactPlayer, focus, outward, right, up, halfWidth, halfHeight),
                RequiredDistance(contactBear, focus, outward, right, up, halfWidth, halfHeight));
        }

        public float RequiredFieldOfView(Vector3 position, Vector3 focus, float aspect, bool anticipated)
        {
            Quaternion orientation = Quaternion.LookRotation(focus - position);
            Quaternion inverse = Quaternion.Inverse(orientation);
            float tangent = Mathf.Max(RequiredTangent(anticipated ? contactPlayer : PlayerBounds, position, inverse, aspect),
                RequiredTangent(anticipated ? contactBear : BearBounds, position, inverse, aspect));
            return 2 * Mathf.Atan(tangent / .82f) * Mathf.Rad2Deg;
        }

        private static float RequiredTangent(Bounds bounds, Vector3 camera, Quaternion inverse, float aspect)
        {
            float tangent = 0;
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = inverse * (point - camera);
                if (local.z <= .05f) return float.PositiveInfinity;
                tangent = Mathf.Max(tangent, Mathf.Max(Mathf.Abs(local.y), Mathf.Abs(local.x) / Mathf.Max(.8f, aspect)) / local.z);
            }
            return tangent;
        }

        public void Bind(PitBear source, PlayerController victim)
        {
            player = victim; bear = source;
            cageVisuals.Clear(); bearBones.Clear(); bearRadii.Clear(); bearHead = null;
            skinBones = null; skinProbes = null; bindPoses = null; skinMatrices = null; usedBones = null;
            capsule = player != null ? player.GetComponent<CapsuleCollider>() : null;
            if (bear != null)
            {
                foreach (var cage in bear.transform.root.GetComponentsInChildren<CageStation>(true))
                    foreach (var visual in cage.GetComponentsInChildren<Renderer>(true))
                        if (visual is MeshRenderer && visual.GetComponentInParent<PlayerController>() == null)
                            cageVisuals.Add(visual);
                if (bear.VisualRoot != null)
                    foreach (var bone in bear.VisualRoot.GetComponentsInChildren<Transform>(true))
                    {
                        Vector3 radius;
                        switch (bone.name)
                        {
                            case "Pelvis": radius = new Vector3(.65f, .58f, .55f); break;
                            case "Lumbar": case "Spine": case "Chest": radius = new Vector3(.66f, .60f, .55f); break;
                            case "Head": bearHead = bone; continue;
                            case "ForePaw.L": case "ForePaw.R": case "HindPaw.L": case "HindPaw.R":
                                radius = new Vector3(.36f, .28f, .56f); break;
                            default: continue;
                        }
                        bearBones.Add(bone); bearRadii.Add(radius);
                    }
            }
            obstacles = new Obstacle[cageVisuals.Count];
            // Several cages share the same imported frame/shelf. Read vertices
            // once per mesh at binding, never once per camera ray or per frame.
            var surfaces = new Dictionary<Mesh, Surface>();
            for (int i = 0; i < cageVisuals.Count; i++)
            {
                var visual = cageVisuals[i];
                obstacles[i].Name = visual.transform.parent.name + "/" + visual.name;
                var filter = visual.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                if (!surfaces.TryGetValue(mesh, out var surface)) surfaces.Add(mesh, surface = new Surface(mesh));
                obstacles[i].Surface = surface;
            }
            if (player == null) return;
            var database = CharacterSkinProbes.Shared;
            if (database == null) return;
            foreach (var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!database.TryGet(renderer.sharedMesh, out var entry) || entry.Probes == null || entry.Probes.Length == 0) continue;
                var bones = renderer.bones;
                if (entry.BindPoses == null || entry.BindPoses.Length != bones.Length) continue;
                var used = new bool[bones.Length];
                bool valid = true;
                foreach (var probe in entry.Probes)
                    for (int i = 0; i < CharacterSkinProbes.BonesPerProbe; i++)
                    {
                        if (probe.Weights[i] <= 0) continue;
                        int index = probe.Bone(i);
                        if (index >= bones.Length || bones[index] == null) { valid = false; break; }
                        used[index] = true;
                    }
                if (!valid) continue;
                var indices = new List<int>();
                for (int i = 0; i < used.Length; i++) if (used[i]) indices.Add(i);
                skinBones = bones; skinProbes = entry.Probes; bindPoses = entry.BindPoses;
                skinMatrices = new Matrix4x4[bones.Length]; usedBones = indices.ToArray();
                break;
            }
        }

        public void RefreshBounds()
        {
            for (int i = 0; i < cageVisuals.Count; i++)
            {
                Renderer renderer = cageVisuals[i];
                ref Obstacle obstacle = ref obstacles[i];
                obstacle.Active = renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy;
                if (!obstacle.Active) continue;
                obstacle.World = renderer.bounds; obstacle.Local = renderer.localBounds;
                obstacle.WorldToLocal = renderer.transform.worldToLocalMatrix;
                Vector3 scale = renderer.transform.lossyScale;
                obstacle.PaddingScale = new Vector3(1 / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                    1 / Mathf.Max(.0001f, Mathf.Abs(scale.y)), 1 / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
            }
            Bounds actor = capsule != null ? capsule.bounds : new Bounds(player.Position + Vector3.up, new Vector3(1, 2, 1));
            if (skinProbes != null)
            {
                foreach (int index in usedBones) skinMatrices[index] = skinBones[index].localToWorldMatrix * bindPoses[index];
                for (int i = 0; i < skinProbes.Length; i++)
                {
                    var probe = skinProbes[i]; Vector3 point = Vector3.zero;
                    for (int slot = 0; slot < CharacterSkinProbes.BonesPerProbe; slot++)
                        if (probe.Weights[slot] > 0) point += skinMatrices[probe.Bone(slot)].MultiplyPoint3x4(probe.Position) * probe.Weights[slot];
                    if (i == 0) actor = new Bounds(point, Vector3.zero); else actor.Encapsulate(point);
                }
            }
            actor.Expand(SkinPadding * 2);
            PlayerBounds = actor;
            var animal = new Bounds(bear.PresentationRoot.position + Vector3.up, WorldRadii(new Vector3(.75f, 1.1f, 1.85f)) * 2);
            for (int i = 0; i < bearBones.Count; i++)
            {
                // Radii enclose the actual body masses about the posed skeleton.
                // Renderer.bounds cannot be used: the importer reserves a 10 m
                // box for the rear-up clip, even while the animal is on all fours.
                var box = new Bounds(bearBones[i].position, WorldRadii(bearRadii[i]) * 2);
                if (i == 0) animal = box; else animal.Encapsulate(box);
            }
            if (bearHead != null)
                animal.Encapsulate(new Bounds(bearHead.position + bear.PresentationRoot.forward * .5f, WorldRadii(new Vector3(.5f, .425f, .6f)) * 2));
            BearBounds = animal;
            animal.Encapsulate(actor); CombinedBounds = animal;
        }

        private Vector3 WorldRadii(Vector3 radius)
        {
            Vector3 right = bear.PresentationRoot.right, forward = bear.PresentationRoot.forward;
            return new Vector3(Mathf.Abs(right.x) * radius.x + Mathf.Abs(forward.x) * radius.z, radius.y,
                Mathf.Abs(right.z) * radius.x + Mathf.Abs(forward.z) * radius.z);
        }

        public bool ClearVisualSegment(Vector3 from, Vector3 to, float padding = VisibilityPadding)
        {
            LastObstruction = null;
            Vector3 segment = to - from; float distance = segment.magnitude;
            if (distance < .001f) return true;
            var ray = new Ray(from, segment / distance);
            foreach (var obstacle in obstacles)
            {
                if (!obstacle.Active) continue;
                Bounds broad = obstacle.World; broad.Expand(padding * 2);
                if (!broad.Contains(from) && (!broad.IntersectRay(ray, out float broadDistance) || broadDistance > distance)) continue;
                Vector3 localFrom = obstacle.WorldToLocal.MultiplyPoint3x4(from);
                Vector3 localTo = obstacle.WorldToLocal.MultiplyPoint3x4(to);
                Vector3 localDelta = localTo - localFrom;
                float length = localDelta.magnitude;
                Bounds bounds = obstacle.Local;
                bounds.Expand(obstacle.PaddingScale * (padding * 2));
                if (!bounds.Contains(localFrom) && (length < .001f ||
                    !bounds.IntersectRay(new Ray(localFrom, localDelta / length), out float hit) || hit >= length)) continue;
                if (obstacle.Surface != null)
                {
                    bool blocked = obstacle.Surface.Intersects(localFrom, localTo);
                    // Lens clearance also samples its rim. Subject visibility uses
                    // independent screen samples below, so a thin rail is not
                    // inflated into an opaque wall across the whole animal.
                    if (!blocked && padding > .03f)
                    {
                        Vector3 right = Vector3.Cross(segment, Vector3.up).normalized;
                        if (right.sqrMagnitude < .1f) right = Vector3.right;
                        Vector3 up = Vector3.Cross(right, segment).normalized;
                        Vector3 x = obstacle.WorldToLocal.MultiplyVector(right * padding);
                        Vector3 y = obstacle.WorldToLocal.MultiplyVector(up * padding);
                        blocked = obstacle.Surface.Intersects(localFrom + x, localTo + x) ||
                            obstacle.Surface.Intersects(localFrom - x, localTo - x) ||
                            obstacle.Surface.Intersects(localFrom + y, localTo + y) ||
                            obstacle.Surface.Intersects(localFrom - y, localTo - y);
                    }
                    if (!blocked) continue;
                }
                LastObstruction = obstacle.Name + (obstacle.Surface != null ? " (mesh)" : " (solid bounds)");
                return false;
            }
            return true;
        }

        public bool ClearActorViews(Vector3 camera, float floor, int solidMask = 0)
        {
            return ClearActorView(camera, PlayerBounds, floor, solidMask) && ClearActorView(camera, BearBounds, floor, solidMask);
        }

        private bool ClearActorView(Vector3 camera, Bounds bounds, float floor, int solidMask)
        {
            Vector3 middle = bounds.center;
            Vector3 side = Vector3.Cross(Vector3.up, camera - middle).normalized;
            float width = (Mathf.Abs(side.x) * bounds.extents.x + Mathf.Abs(side.z) * bounds.extents.z) * .72f;
            float lower = Mathf.Max(floor + .15f, bounds.min.y + .15f), upper = bounds.max.y - SkinPadding;
            int blocked = 0;
            for (int row = 0; row < 3; row++)
                for (int column = -2; column <= 2; column++)
                {
                    Vector3 point = middle + side * (column * .5f * width);
                    point.y = Mathf.Lerp(lower, upper, row * .5f);
                    bool occluded;
                    if (solidMask != 0 && Physics.Linecast(camera, point, out RaycastHit hit, solidMask, QueryTriggerInteraction.Ignore))
                    { LastObstruction = hit.collider.name + " (physics)"; occluded = true; }
                    else occluded = !ClearVisualSegment(camera, point);
                    if (occluded && ++blocked > 3) return false;
                }
            return true;
        }

        public float RequiredDistance(Vector3 focus, Vector3 outward, float fieldOfView, float aspect)
        {
            Quaternion orientation = Quaternion.LookRotation(-outward);
            Vector3 right = orientation * Vector3.right, up = orientation * Vector3.up;
            float halfHeight = Mathf.Tan(fieldOfView * .5f * Mathf.Deg2Rad) * .82f;
            float halfWidth = halfHeight * Mathf.Max(.8f, aspect);
            return Mathf.Max(RequiredDistance(PlayerBounds, focus, outward, right, up, halfWidth, halfHeight),
                RequiredDistance(BearBounds, focus, outward, right, up, halfWidth, halfHeight));
        }

        private static float RequiredDistance(Bounds bounds, Vector3 focus, Vector3 outward,
            Vector3 right, Vector3 up, float halfWidth, float halfHeight)
        {
            float required = 0;
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 delta = point - focus;
                float depth = Vector3.Dot(delta, outward);
                required = Mathf.Max(required, Mathf.Max(depth + Mathf.Abs(Vector3.Dot(delta, right)) / halfWidth,
                    depth + Mathf.Abs(Vector3.Dot(delta, up)) / halfHeight));
            }
            return required;
        }
    }
}
