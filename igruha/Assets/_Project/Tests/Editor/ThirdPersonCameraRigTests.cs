using System.Reflection;
using Igruha.Core.CameraSystems;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class ThirdPersonCameraRigTests
    {
        private const float Radius = 0.28f;
        private const float Tolerance = 0.002f;
        private static readonly Vector3 TestOrigin = new Vector3(1000f, 1000f, 1000f);
        private GameObject objects;
        private Transform player;
        private Transform chest;
        private CapsuleCollider capsule;
        private CinemachineCamera camera;
        private CinemachineOrbitalFollow orbit;
        private CinemachineDeoccluder deoccluder;
        private int mask;

        [SetUp]
        public void SetUp()
        {
            objects = new GameObject("Camera regression fixtures");
            player = new GameObject("Player root").transform;
            player.SetParent(objects.transform);
            player.position = TestOrigin;
            capsule = player.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = Vector3.up;
            capsule.height = 2f;
            capsule.radius = 0.36f;
            chest = new GameObject("CameraTarget").transform;
            chest.SetParent(player, false);
            chest.localPosition = Vector3.up * 1.7f;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Camera/PartyCameraRig.prefab");
            var instance = Object.Instantiate(prefab, objects.transform);
            camera = instance.GetComponent<CinemachineCamera>();
            orbit = instance.GetComponent<CinemachineOrbitalFollow>();
            deoccluder = instance.GetComponent<CinemachineDeoccluder>();
            var rig = instance.GetComponent<ThirdPersonCameraRig>();
            // MonoBehaviour.Awake не вызывается у обычного компонента в Edit Mode.
            typeof(ThirdPersonCameraRig).GetMethod("Awake",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rig, null);
            camera.Follow = chest;
            mask = deoccluder.CollideAgainst;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(objects);
            Physics.SyncTransforms();
        }

        [Test]
        public void RootAndChestTargetsResolveToSameWorldHead()
        {
            Tick(0f, 17.5f);
            Vector3 fromChest = chest.position + chest.up * deoccluder.AvoidObstacles.UseFollowTarget.YOffset;
            camera.Follow = player;
            Tick(0f, 17.5f);
            Vector3 fromRoot = player.position + player.up * deoccluder.AvoidObstacles.UseFollowTarget.YOffset;
            Assert.That(Vector3.Distance(fromChest, fromRoot), Is.LessThan(Tolerance));
            Assert.That(fromChest.y - TestOrigin.y, Is.EqualTo(2.25f).Within(Tolerance));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OpenSpacePreservesOrbitAndAim(bool useRoot)
        {
            camera.Follow = useRoot ? player : chest;
            Tick(65f, 17.5f);
            Assert.That(camera.State.PositionCorrection.sqrMagnitude, Is.LessThan(Tolerance * Tolerance));
            Assert.That(camera.GetComponent<CinemachineRotationComposer>().TargetOffset.y,
                Is.EqualTo(0.3f).Within(Tolerance));
            Assert.That(orbit.Radius, Is.EqualTo(4.2f));
            Assert.That(camera.Lens.FieldOfView, Is.EqualTo(55f));
        }

        [TestCase(-1f, -1f)]
        [TestCase(-1f, 1f)]
        [TestCase(1f, -1f)]
        [TestCase(1f, 1f)]
        public void LandingCornersStayInsideAtEveryOrbitAngle(float x, float z)
        {
            BuildRoom();
            player.position = TestOrigin + new Vector3(x * 2.6f, 1.5f, z * 2.6f);
            foreach (float pitch in new[] { -12f, 17.5f, 55f })
                for (int yaw = -180; yaw < 180; yaw += 15)
                {
                    Tick(yaw, pitch);
                    AssertClear($"corner={x},{z}, orbit={yaw},{pitch}");
                }
        }

        [Test]
        public void JumpAndCrouchUnderCeilingRemainClear()
        {
            BuildRoom();
            for (int frame = 0; frame <= 40; frame++)
            {
                player.position = TestOrigin + Vector3.up * (1.85f * Mathf.Sin(frame / 40f * Mathf.PI));
                Tick(40f, 55f, 1f / 60f);
                AssertClear("jump frame=" + frame);
            }
            capsule.height = 1f;
            capsule.center = Vector3.up * 0.5f;
            Tick(40f, 55f);
            AssertClear("crouch");
            Assert.That(deoccluder.AvoidObstacles.UseFollowTarget.YOffset, Is.LessThan(0f));
        }

        [Test]
        public void DampingCannotLeaveCameraBehindNewWall()
        {
            Tick(0f, 17.5f);
            Box("wall", new Vector3(0f, 2f, -1f), new Vector3(6f, 4f, 0.2f));
            Tick(0f, 17.5f, 1f / 120f);
            AssertClear("first frame of obstruction");
        }

        [Test]
        public void CloseObstructionLiftsAimAboveHead()
        {
            Box("close wall", new Vector3(0f, 2f, -0.6f), new Vector3(6f, 4f, 0.2f));
            Tick(0f, 17.5f);
            Tick(0f, 17.5f, 1f / 60f);
            AssertClear("close view");
            Assert.That(camera.GetComponent<CinemachineRotationComposer>().TargetOffset.y,
                Is.GreaterThan(0.5f));
        }

        [Test]
        public void DefaultLayerAndTriggersDoNotObstructCamera()
        {
            var wall = Box("Default", new Vector3(0f, 2f, -1f), new Vector3(6f, 4f, 0.2f));
            wall.gameObject.layer = 0;
            var trigger = Box("Trigger", new Vector3(0f, 2f, -2f), new Vector3(6f, 4f, 0.2f));
            trigger.isTrigger = true;
            Tick(0f, 17.5f);
            Assert.That(camera.State.PositionCorrection.sqrMagnitude, Is.LessThan(Tolerance * Tolerance));
        }

        [Test]
        public void CameraOnlyBeamBlocksCameraWithBodyContactsExcluded()
        {
            int layer = LayerMask.NameToLayer("CameraOnly");
            Assert.That(layer, Is.GreaterThanOrEqualTo(0));
            var beam = Box("beam", new Vector3(0f, 2f, -1f), new Vector3(6f, 4f, 0.2f));
            beam.gameObject.layer = layer;
            beam.excludeLayers = ~0;
            Tick(0f, 17.5f);
            AssertClear("camera-only beam");
            Assert.That(camera.State.PositionCorrection.magnitude, Is.GreaterThan(0.5f));
        }

        private void BuildRoom()
        {
            Box("ceiling", new Vector3(0f, 4f, 0f), new Vector3(8f, 0.2f, 8f));
            Box("floor", new Vector3(0f, -0.1f, 0f), new Vector3(8f, 0.2f, 8f));
            Box("west", new Vector3(-3.1f, 2f, 0f), new Vector3(0.2f, 4f, 8f));
            Box("east", new Vector3(3.1f, 2f, 0f), new Vector3(0.2f, 4f, 8f));
            Box("south", new Vector3(0f, 2f, -3.1f), new Vector3(8f, 4f, 0.2f));
            Box("north", new Vector3(0f, 2f, 3.1f), new Vector3(8f, 4f, 0.2f));
        }

        private BoxCollider Box(string name, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(objects.transform);
            go.transform.position = TestOrigin + position;
            go.layer = LayerMask.NameToLayer("Ground");
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        private void Tick(float yaw, float pitch, float deltaTime = -1f)
        {
            orbit.HorizontalAxis.Value = yaw;
            orbit.VerticalAxis.Value = pitch;
            Physics.SyncTransforms();
            camera.InternalUpdateCameraState(Vector3.up, deltaTime);
        }

        private void AssertClear(string context)
        {
            Vector3 position = camera.State.GetFinalPosition();
            Assert.That(Physics.CheckSphere(position, Radius - Tolerance, mask,
                QueryTriggerInteraction.Ignore), Is.False, context + ": camera overlaps geometry");
            Assert.That(Physics.Linecast(capsule.bounds.center, position, mask,
                QueryTriggerInteraction.Ignore), Is.False, context + ": wall hides player");
        }
    }
}
