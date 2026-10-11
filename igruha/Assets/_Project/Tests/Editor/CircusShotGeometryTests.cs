using System;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Arena;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.Tests
{
    public sealed class CircusShotGeometryTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type GeometryType = typeof(CircusAttackPresentation).Assembly.GetType("Igruha.Minigames.Circus.CircusShotGeometry", true);
        private static readonly FieldInfo SharedProbes = typeof(CharacterSkinProbes).GetField("shared", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly List<MeshFilter> bearParts = new List<MeshFilter>();
        private readonly List<Mesh> obstacleMeshes = new List<Mesh>();
        private GameObject root;
        private PitBear bear;
        private PlayerController player;
        private CharacterConfig config;
        private CharacterSkinProbes originalProbes, testProbes;
        private Transform baseBone, armBone, pawBone, cage;
        private SkinnedMeshRenderer skin;
        private Mesh fixtureSkin, baked;
        private Camera camera;
        private object geometry;

        [SetUp]
        public void BuildFixtures()
        {
            originalProbes = CharacterSkinProbes.Shared;
            root = new GameObject("Circus camera regression fixtures");
            root.SetActive(false);
            camera = Child("Output camera").gameObject.AddComponent<Camera>();
            camera.enabled = false; camera.fieldOfView = 55; camera.nearClipPlane = .01f; camera.farClipPlane = 100;
            var avatar = Child("Victim"); avatar.localPosition = new Vector3(2.2f, 0, .4f);
            player = avatar.gameObject.AddComponent<PlayerController>(); player.enabled = false;
            config = ScriptableObject.CreateInstance<CharacterConfig>();
            typeof(PlayerController).GetField("config", Private).SetValue(player, config);
            var capsule = avatar.GetComponent<CapsuleCollider>(); capsule.height = 2; capsule.radius = .3f; capsule.center = Vector3.up;
            BuildSkin(avatar);

            bear = Child("Bear").gameObject.AddComponent<PitBear>(); bear.enabled = false;
            var visual = Child("Visual", bear.transform);
            typeof(PitBear).GetField("visualRoot", Private).SetValue(bear, visual);
            Child("Pelvis", visual).localPosition = new Vector3(0, 1.1f, -.9f);
            Child("Lumbar", visual).localPosition = new Vector3(0, 1.3f, -.5f);
            Child("Spine", visual).localPosition = new Vector3(0, 1.5f, 0);
            Child("Chest", visual).localPosition = new Vector3(0, 1.65f, .4f);
            var head = Child("Head", visual); head.localPosition = new Vector3(0, 1.6f, 1.1f);
            BearPart("Body surface", visual, new Vector3(0, 1.1f, -.35f), new Vector3(1.2f, 1.6f, 2.2f));
            BearPart("Head surface", head, new Vector3(0, 0, .4f), new Vector3(.75f, .55f, 1.2f));
            pawBone = Child("ForePaw.R", visual); pawBone.localPosition = new Vector3(-.51f, .23f, .66f);
            BearPart("Paw surface", pawBone, new Vector3(0, 0, .12f), new Vector3(.55f, .4f, .7f));
            Child("ForePaw.L", visual).localPosition = new Vector3(.51f, .23f, .66f);
            Child("HindPaw.L", visual).localPosition = new Vector3(.51f, .20f, -1.03f);
            Child("HindPaw.R", visual).localPosition = new Vector3(-.51f, .20f, -1.03f);

            cage = Child("Cage");
            var hatch = Child("Floor", cage).gameObject.AddComponent<HingedFloorHatch>();
            var station = cage.gameObject.AddComponent<CageStation>(); station.enabled = false;
            typeof(CageStation).GetField("hatch", Private).SetValue(station, hatch);
            // EditMode does not run RidePlatform.Awake. Match its actual
            // kinematic body so non-convex cage meshes participate in raycasts.
            cage.GetComponent<Rigidbody>().isKinematic = true;
            root.SetActive(true);
            geometry = Activator.CreateInstance(GeometryType, true);
            baked = new Mesh();
        }

        [TearDown]
        public void Clean()
        {
            SharedProbes.SetValue(null, originalProbes);
            if (root != null) Object.DestroyImmediate(root);
            if (testProbes != null) Object.DestroyImmediate(testProbes);
            if (fixtureSkin != null) Object.DestroyImmediate(fixtureSkin);
            if (baked != null) Object.DestroyImmediate(baked);
            if (config != null) Object.DestroyImmediate(config);
            foreach (var mesh in obstacleMeshes) Object.DestroyImmediate(mesh);
            obstacleMeshes.Clear();
            bearParts.Clear();
        }

        [Test]
        public void VisibleOpenHatchBlocksTheShotAfterItsPhysicsColliderIsDisabled()
        {
            var panel = Panel(new Vector3(20, 1, 0), new Vector3(2, 2, .08f), Quaternion.identity);
            var collider = panel.GetComponent<BoxCollider>(); collider.enabled = false;
            Bind();
            Vector3 from = new Vector3(20, 1, -3), to = new Vector3(20, 1, 3);
            var ray = new Ray(from, (to - from).normalized);
            Assert.That(collider.Raycast(ray, out _, 6), Is.False, "The regression must not be caught by the disabled physics shape.");
            Assert.That(Clear(from, to), Is.False, "The visible plywood still fills the camera ray.");
            Assert.That(Clear(from + Vector3.right * 2, to + Vector3.right * 2), Is.True, "An adjacent open sightline remains usable.");
        }

        [Test]
        public void RotatedThinPanelDoesNotTurnItsEmptyWorldBoundsIntoAWall()
        {
            var panel = Panel(new Vector3(20, 1, 0), new Vector3(3, 2, .08f), Quaternion.Euler(0, 45, 0));
            Bind();
            Vector3 from = panel.transform.position + new Vector3(.8f, 0, .8f);
            Vector3 to = from + Vector3.right * 1.5f;
            var ray = new Ray(from, (to - from).normalized);
            Assert.That(panel.bounds.Contains(from), Is.True, "Fixture must enter the broad-phase world box.");
            Assert.That(panel.GetComponent<BoxCollider>().Raycast(ray, out _, 1.5f), Is.False, "Real oriented plywood is outside this ray.");
            Assert.That(Clear(from, to), Is.True, "Use the oriented surface volume, not the whole world AABB.");
            from = panel.transform.position - panel.transform.forward * 3;
            to = panel.transform.position + panel.transform.forward * 3;
            Assert.That(panel.GetComponent<BoxCollider>().Raycast(new Ray(from, (to - from).normalized), out _, 6), Is.True);
            Assert.That(Clear(from, to), Is.False);
        }

        [Test]
        public void CombinedHollowCageUsesItsActualSurfacesInsteadOfFillingItsOpening()
        {
            var frame = Child("Combined imported cage window", cage);
            frame.position = new Vector3(20, 1, 0);
            frame.rotation = Quaternion.Euler(0, 31, 0);
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var weights = new List<BoneWeight>();
            SkinBox(vertices, triangles, weights, Vector3.left, new Vector3(.15f, 2.15f, .1f), 0);
            SkinBox(vertices, triangles, weights, Vector3.right, new Vector3(.15f, 2.15f, .1f), 0);
            SkinBox(vertices, triangles, weights, Vector3.up, new Vector3(2.15f, .15f, .1f), 0);
            SkinBox(vertices, triangles, weights, Vector3.down, new Vector3(2.15f, .15f, .1f), 0);
            var mesh = new Mesh { name = "One mesh, four solid rails, empty centre" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); obstacleMeshes.Add(mesh);
            frame.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = frame.gameObject.AddComponent<MeshRenderer>();
            var physics = frame.gameObject.AddComponent<MeshCollider>(); physics.sharedMesh = mesh;
            Physics.SyncTransforms();
            Vector3 from = frame.position - frame.forward * 3, to = frame.position + frame.forward * 3;
            Assert.That(renderer.bounds.Contains(frame.position), Is.True, "The hollow centre is inside both renderer bounding boxes.");
            Assert.That(physics.Raycast(new Ray(from, frame.forward), out _, 6), Is.False, "Real triangles leave the opening empty.");
            Vector3 railFrom = from + frame.right, railTo = to + frame.right;
            Assert.That(physics.Raycast(new Ray(railFrom, frame.forward), out _, 6), Is.True, "The rail is a real opaque surface.");
            physics.enabled = false;
            Bind();
            Assert.That(Clear(from, to), Is.True, "A combined frame must not seal its whole renderer volume.");
            Assert.That(Clear(from, frame.position), Is.True, "The actor may stand inside a hollow renderer's box.");
            Assert.That(Clear(railFrom, railTo), Is.False, "Disabling the physics collider must not make visible rails transparent.");
            Assert.That(Clear(railTo, railFrom), Is.False, "Visual geometry blocks from both sides, regardless of triangle winding.");
        }

        [Test]
        public void CachedDoorGeometryFollowsMovementVisibilityAndParentScale()
        {
            cage.localScale = new Vector3(1.7f, .85f, .6f);
            cage.localRotation = Quaternion.Euler(0, 24, 0);
            var panel = Panel(new Vector3(20, 1, 0), new Vector3(2, 2, .10f), Quaternion.Euler(0, 17, 0));
            panel.GetComponent<BoxCollider>().enabled = false;
            Bind();
            Vector3 from = panel.transform.position - panel.transform.forward * 4;
            Vector3 to = panel.transform.position + panel.transform.forward * 4;
            Assert.That(Clear(from, to), Is.False);
            panel.enabled = false; Refresh(); Assert.That(Clear(from, to), Is.True);
            panel.enabled = true; panel.gameObject.SetActive(false); Refresh(); Assert.That(Clear(from, to), Is.True);
            panel.gameObject.SetActive(true); panel.transform.position += Vector3.right * 6; Refresh();
            Assert.That(Clear(from, to), Is.True);
            panel.transform.position -= Vector3.right * 6; Refresh(); Assert.That(Clear(from, to), Is.False);
        }

        [Test]
        public void ARendererOnlyPanelCanOccludeAnActorWithoutTouchingTheCamera()
        {
            Bind();
            Vector3 viewpoint = new Vector3(2.2f, 1.4f, -5);
            var panel = Panel(new Vector3(2.2f, 1, -2), new Vector3(.8f, 2, .08f), Quaternion.identity);
            panel.GetComponent<BoxCollider>().enabled = false;
            Bind();
            Assert.That(panel.bounds.Contains(viewpoint), Is.False);
            Assert.That(ActorViews(viewpoint), Is.False, "The subject is hidden although the lens itself is in free space.");
            panel.transform.position += Vector3.right * 10; Refresh();
            Assert.That(ActorViews(viewpoint), Is.True);
        }

        [Test]
        public void OneThinCageRailDoesNotCancelAnOtherwiseReadableContactShot()
        {
            // The raised/rightward arm shifts the visible silhouette away from
            // the root capsule. Place the rail on the independently baked centre.
            skin.BakeMesh(baked);
            Vector3[] vertices = baked.vertices;
            var silhouette = new Bounds(skin.transform.TransformPoint(vertices[0]), Vector3.zero);
            foreach (Vector3 vertex in vertices) silhouette.Encapsulate(skin.transform.TransformPoint(vertex));
            Vector3 viewpoint = silhouette.center + new Vector3(0, .4f, -5.4f);
            var rail = Panel(silhouette.center + Vector3.back * 2.4f, new Vector3(.05f, 3, .06f), Quaternion.identity);
            rail.GetComponent<BoxCollider>().enabled = false;
            Bind();
            Assert.That(Clear(viewpoint, BoundsProperty("PlayerBounds").center), Is.False, "This rail crosses the centre ray.");
            Assert.That(ActorViews(viewpoint), Is.True, "The other silhouette samples still expose most of the player and animal.");
            rail.transform.localScale = new Vector3(2, 3, .06f); Refresh();
            Assert.That(ActorViews(viewpoint), Is.False, "A broad opaque hatch is not equivalent to a narrow rail.");
        }

        [Test]
        public void EnteringFromInsideTheActualBearMuzzleRequiresAnImpactCut()
        {
            Bind();
            // A point inside the authored head mesh, far from the pelvis/actor
            // centre, was missed by the old centre-distance blend test.
            Vector3 nose = bearParts[1].transform.TransformPoint(new Vector3(0, 0, .35f));
            Assert.That(bearParts[1].GetComponent<Renderer>().bounds.Contains(nose), Is.True);
            Vector3 sideShot = new Vector3(-5, 1.4f, -3);
            Assert.That((bool)Call("ClearActorSegment", nose, sideShot, .18f), Is.False);
            Assert.That((bool)Call("ClearActorSegment", sideShot, nose, .18f), Is.False);
            Assert.That((bool)Call("ClearActorSegment", new Vector3(-5, 3, -3), new Vector3(-4, 3, -2), .18f), Is.True);
        }

        [Test]
        public void StationaryContactCameraFramesTheAnticipatedMovingSilhouette([Values(4f / 3f, 16f / 9f)] float aspect)
        {
            Bind();
            Vector3 travel = new Vector3(1.3f, 0, .4f);
            Call("CaptureContactEnvelope", travel, 0f, 0);
            Vector3 focus = BoundsProperty("ContactBounds").center;
            Vector3 outward = new Vector3(-.8f, .1f, -.6f).normalized;
            float distance = (float)Call("ContactDistance", focus, outward, 55f, aspect);
            camera.aspect = aspect; camera.fieldOfView = 55;
            camera.transform.SetPositionAndRotation(focus + outward * distance, Quaternion.LookRotation(-outward));
            skin.BakeMesh(baked);
            foreach (float progress in new[] { 0f, .5f, 1f })
                foreach (Vector3 vertex in baked.vertices)
                {
                    Vector3 point = skin.transform.TransformPoint(vertex) + travel * progress + Vector3.up * (.25f * progress);
                    Vector3 viewport = camera.WorldToViewportPoint(point);
                    Assert.That(viewport.z, Is.GreaterThan(camera.nearClipPlane));
                    Assert.That(viewport.x, Is.InRange(.01f, .99f), "The fixed camera must reserve travel room.");
                    Assert.That(viewport.y, Is.InRange(.01f, .99f), "The lifted silhouette must fit without moving the lens.");
                }
            float lens = (float)Call("RequiredFieldOfView", camera.transform.position, focus, aspect, true);
            Assert.That(lens, Is.InRange(54.8f, 55.2f), "The independent projection and lens fit should describe the same perspective.");
        }

        [Test]
        public void AnimatedSilhouettesFitRealCameraProjection(
            [Values(4f / 3f, 16f / 9f, 21f / 9f)] float aspect,
            [Values(0, 1, 2)] int pose,
            [Values(0f, 70f)] float heading)
        {
            bear.transform.rotation = Quaternion.Euler(0, heading, 0);
            if (pose == 1)
            {
                baseBone.localPosition = new Vector3(.3f, .65f, .4f);
                baseBone.localRotation = Quaternion.Euler(-30, 0, 10);
                armBone.localRotation = Quaternion.Euler(0, 0, -70);
                pawBone.localPosition = new Vector3(-.65f, 1.3f, 1.1f);
            }
            else if (pose == 2)
            {
                baseBone.localPosition = new Vector3(.4f, .65f, -.3f);
                baseBone.localRotation = Quaternion.Euler(10, 0, 78);
                armBone.localRotation = Quaternion.Euler(30, 0, 25);
                pawBone.localPosition = new Vector3(-.7f, .9f, 1.4f);
            }
            Bind();
            camera.aspect = aspect;
            Vector3 focus = BoundsProperty("CombinedBounds").center;
            Vector3 outward = new Vector3(.8f, .23f, -.6f).normalized;
            float distance = (float)Call("RequiredDistance", focus, outward, camera.fieldOfView, aspect);
            camera.transform.SetPositionAndRotation(focus + outward * (distance + .001f), Quaternion.LookRotation(-outward));
            skin.BakeMesh(baked);
            var points = new List<Vector3>();
            foreach (var vertex in baked.vertices) points.Add(skin.transform.TransformPoint(vertex));
            foreach (var part in bearParts)
                foreach (var vertex in part.sharedMesh.vertices) points.Add(part.transform.TransformPoint(vertex));
            float left = 1, right = 0, bottom = 1, top = 0;
            foreach (Vector3 point in points)
            {
                Vector3 viewport = camera.WorldToViewportPoint(point);
                Assert.That(viewport.z, Is.GreaterThan(camera.nearClipPlane));
                Assert.That(viewport.x, Is.InRange(.015f, .985f), "Pose " + pose + " horizontal silhouette crop at " + point);
                Assert.That(viewport.y, Is.InRange(.015f, .985f), "Pose " + pose + " vertical silhouette crop at " + point);
                left = Mathf.Min(left, viewport.x); right = Mathf.Max(right, viewport.x);
                bottom = Mathf.Min(bottom, viewport.y); top = Mathf.Max(top, viewport.y);
            }
            Assert.That(Mathf.Max(right - left, top - bottom), Is.GreaterThan(.35f), "The actors must still occupy a readable part of the shot.");
        }

        private void BuildSkin(Transform avatar)
        {
            baseBone = Child("Body bone", avatar);
            armBone = Child("Arm bone", baseBone); armBone.localPosition = Vector3.up;
            skin = Child("Actual skinned fixture", avatar).gameObject.AddComponent<SkinnedMeshRenderer>();
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var weights = new List<BoneWeight>();
            SkinBox(vertices, triangles, weights, new Vector3(0, 1, 0), new Vector3(.65f, 1.9f, .45f), 0);
            SkinBox(vertices, triangles, weights, new Vector3(.65f, 1.05f, 0), new Vector3(.7f, .25f, .25f), 1);
            fixtureSkin = new Mesh { name = "Two independently skinned camera subjects" };
            fixtureSkin.SetVertices(vertices); fixtureSkin.SetTriangles(triangles, 0); fixtureSkin.boneWeights = weights.ToArray();
            var bones = new[] { baseBone, armBone };
            var poses = new[] { baseBone.worldToLocalMatrix * skin.transform.localToWorldMatrix, armBone.worldToLocalMatrix * skin.transform.localToWorldMatrix };
            fixtureSkin.bindposes = poses; fixtureSkin.RecalculateBounds();
            skin.sharedMesh = fixtureSkin; skin.bones = bones; skin.rootBone = baseBone; skin.updateWhenOffscreen = true;
            var probes = new CharacterSkinProbes.Probe[vertices.Count];
            for (int i = 0; i < probes.Length; i++)
                probes[i] = new CharacterSkinProbes.Probe { Position = vertices[i], Weights = new Vector4(1, 0, 0, 0),
                    Bones = CharacterSkinProbes.Probe.PackBones(weights[i].boneIndex0, 0, 0, 0), Group = i < 8 ? 4 : 2 };
            testProbes = ScriptableObject.CreateInstance<CharacterSkinProbes>();
            testProbes.Replace(new[] { new CharacterSkinProbes.Entry { Mesh = fixtureSkin, BindPoses = poses, Probes = probes } });
            SharedProbes.SetValue(null, testProbes);
        }

        private static void SkinBox(List<Vector3> vertices, List<int> triangles, List<BoneWeight> weights, Vector3 center, Vector3 size, int bone)
        {
            int first = vertices.Count;
            for (int i = 0; i < 8; i++)
            {
                vertices.Add(center + Vector3.Scale(size * .5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1 });
            }
            foreach (int index in new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 })
                triangles.Add(first + index);
        }

        private Transform Child(string name, Transform parent = null)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent != null ? parent : root.transform, false);
            return child;
        }

        private void BearPart(string name, Transform parent, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>()); bearParts.Add(go.GetComponent<MeshFilter>());
        }

        private Renderer Panel(Vector3 position, Vector3 scale, Quaternion rotation)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Visible plywood, physics optional";
            go.transform.SetParent(cage, false); go.transform.position = position; go.transform.rotation = rotation; go.transform.localScale = scale;
            return go.GetComponent<Renderer>();
        }

        private void Bind() { Call("Bind", bear, player); Refresh(); }
        private void Refresh() { Physics.SyncTransforms(); Call("RefreshBounds"); }
        private object Call(string method, params object[] args) => GeometryType.GetMethod(method).Invoke(geometry, args);
        private bool Clear(Vector3 from, Vector3 to) => (bool)Call("ClearVisualSegment", from, to, .025f);
        private bool ActorViews(Vector3 from) => (bool)Call("ClearActorViews", from, 0f, 0);
        private Bounds BoundsProperty(string name) => (Bounds)GeometryType.GetProperty(name).GetValue(geometry);
    }
}
