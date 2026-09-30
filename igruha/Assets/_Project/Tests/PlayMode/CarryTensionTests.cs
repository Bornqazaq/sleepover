using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Items;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.CarryItem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Igruha.Tests.PlayMode
{
    public sealed class CarryTensionTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private CarryItemConfig config;
        private WaterCart cart;
        private bool background;
        private static readonly PropertyInfo Intent = typeof(PlayerController).GetProperty("MoveIntent");

        [SetUp]
        public void SetUp()
        {
            background = Application.runInBackground; Application.runInBackground = true;
            config = ScriptableObject.CreateInstance<CarryItemConfig>(); spawned.Add(config);
            var root = new GameObject("Tension test cart"); spawned.Add(root);
            root.transform.position = Vector3.up * 1000f;
            root.AddComponent<BoxCollider>();
            var body = root.AddComponent<Rigidbody>(); body.useGravity = false;
            root.AddComponent<MultiCarryObject>();
            cart = root.AddComponent<WaterCart>();
            cart.Initialize(config, TeamSide.A, 4, -100f, root.transform.position, Quaternion.identity);
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            body.linearDamping = 0f;
            cart.ChangeWater(150, WaterLossReason.Filled);
        }

        [TearDown]
        public void TearDown()
        {
            cart.Carry.ReleaseAll(CarryReleaseReason.RoundEnded);
            for (int i = spawned.Count - 1; i >= 0; i--) if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            spawned.Clear(); Application.runInBackground = background;
        }

        private PlayerController Carrier(int slot, Vector3 input)
        {
            var root = new GameObject("Carrier " + slot); spawned.Add(root); root.SetActive(false);
            root.transform.position = cart.Carry.StationOf(slot);
            var character = ScriptableObject.CreateInstance<CharacterConfig>(); spawned.Add(character);
            var player = root.AddComponent<PlayerController>();
            typeof(PlayerController).GetField("config", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, character);
            player.enabled = false;
            root.SetActive(true);
            var body = root.GetComponent<Rigidbody>(); body.useGravity = false; body.interpolation = RigidbodyInterpolation.None;
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            body.linearDamping = 0f;
            Intent.SetValue(player, input);
            Assert.That(cart.Carry.TryGrab(player), Is.True);
            return player;
        }

        [TestCase(1, 2.2f, 1.6f, 2.4f, 1.2f)]
        [TestCase(2, 2.8f, 2f, 4f, 2f)]
        [TestCase(3, 2.9f, 2.2f, 5.2f, 2.6f)]
        [TestCase(4, 2.9f, 2.3f, 6.4f, 3.2f)]
        public void OccupiedHandsSelectBothLoadEndpoints(int hands, float emptySpeed, float fullSpeed, float emptyAccel, float fullAccel)
        {
            var settings = cart.Carry.Settings;
            Assert.That(settings.RollingSpeed(hands, 0), Is.EqualTo(emptySpeed));
            Assert.That(settings.RollingSpeed(hands, 1), Is.EqualTo(fullSpeed));
            Assert.That(settings.RollingAcceleration(hands, 0), Is.EqualTo(emptyAccel));
            Assert.That(settings.RollingAcceleration(hands, 1), Is.EqualTo(fullAccel));
        }

        [UnityTest]
        public IEnumerator SoloStraightDoesNotBreakOrSpill()
        {
            cart.SetHandleCount(1); Carrier(0, Vector3.forward);
            yield return new WaitForSeconds(5f);
            Assert.That(cart.Carry.CarrierCount, Is.EqualTo(1));
            Assert.That(cart.Carry.FlatVelocity.magnitude, Is.EqualTo(1.6f).Within(0.12f));
            Assert.That(cart.Carry.StretchOf(0), Is.LessThan(0.6f));
            Assert.That(cart.Water, Is.EqualTo(150));
            Assert.That(cart.Stability.State.Cause, Is.EqualTo(CartTiltCause.None));
        }

        [UnityTest]
        public IEnumerator FourCoordinatedHandsReachFullCapWithoutSpill()
        {
            for (int i = 0; i < 4; i++) Carrier(i, Vector3.forward);
            yield return new WaitForSeconds(4f);
            Assert.That(cart.Carry.CarrierCount, Is.EqualTo(4));
            Assert.That(cart.Carry.FlatVelocity.magnitude, Is.EqualTo(2.3f).Within(0.12f));
            Assert.That(cart.Water, Is.EqualTo(150));
        }

        [UnityTest]
        public IEnumerator PassiveHandBrakesAndCausesDisagreement()
        {
            cart.SetHandleCount(2);
            Carrier(0, Vector3.forward); Carrier(1, Vector3.zero);
            yield return new WaitForSeconds(1.5f);
            Assert.That(cart.Carry.CarrierCount, Is.EqualTo(2));
            Assert.That(cart.Carry.FlatVelocity.magnitude, Is.LessThan(1.5f));
            Assert.That(cart.Stability.State.Cause, Is.EqualTo(CartTiltCause.Disagreement));
            Assert.That(cart.Carry.TiltAngle, Is.GreaterThan(20f));
        }

        [UnityTest]
        public IEnumerator ReleasedCartCoastsAndDoesNotApplyDirectWaterPenalty()
        {
            cart.SetHandleCount(1); var player = Carrier(0, Vector3.forward);
            yield return new WaitForSeconds(3f);
            float before = cart.Carry.FlatVelocity.magnitude;
            cart.Carry.ReleaseHandle(0, CarryReleaseReason.LetGo);
            // Once released, the former carrier must not physically bump the cart in this coasting test.
            player.GetComponent<Collider>().enabled = false;
            Assert.That(cart.Carry.TiltAngle, Is.EqualTo(30f).Within(0.5f));
            Assert.That(cart.Water, Is.EqualTo(150));
            yield return new WaitForSeconds(0.2f);
            Assert.That(cart.Carry.FlatVelocity.magnitude, Is.EqualTo(before - 0.3f).Within(0.08f));
            Assert.That(cart.Carry.CarrierCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator KnockdownReleaseLowersCapWithoutDroppingCart()
        {
            cart.SetHandleCount(2); Carrier(0, Vector3.forward); Carrier(1, Vector3.forward);
            yield return new WaitForSeconds(2f);
            cart.Carry.ReleaseHandle(1, CarryReleaseReason.Knockdown);
            Assert.That(cart.Carry.RollingSpeed, Is.EqualTo(1.6f));
            Assert.That(cart.Carry.TiltAngle, Is.GreaterThan(29f));
            Assert.That(cart.Carry.CarrierCount, Is.EqualTo(1));
            Assert.That(cart.IsLost, Is.False);
        }

        [UnityTest]
        public IEnumerator DisconnectDoesNotAddReleaseKick()
        {
            cart.SetHandleCount(2); Carrier(0, Vector3.forward); Carrier(1, Vector3.forward);
            yield return new WaitForSeconds(2f);
            cart.Carry.ReleaseHandle(1, CarryReleaseReason.RoundEnded);
            cart.SetHandleCount(1);
            Assert.That(cart.Carry.TiltAngle, Is.LessThan(1f));
            Assert.That(cart.Carry.CarrierCount, Is.EqualTo(1));
        }

        [Test]
        public void TurnCalibrationAndEmptyDamping()
        {
            Assert.That(WaterCartStability.TurnDegrees(90, 2, 1, config), Is.EqualTo(45));
            Assert.That(WaterCartStability.TurnDegrees(90, 2, 0, config), Is.EqualTo(22.5f));
            Assert.That(WaterCartStability.DisagreementDegrees(0.15f, 1, config), Is.Zero);
            Assert.That(WaterCartStability.DisagreementDegrees(0.5f, 0, config), Is.EqualTo(24).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator FrontCarrierSurvivesLayoutShrink()
        {
            cart.SetHandleCount(3);
            var rear = Carrier(0, Vector3.forward);
            Carrier(1, Vector3.forward);
            var front = Carrier(2, Vector3.forward);
            yield return new WaitForSeconds(2f);
            cart.Carry.ReleaseFor(rear, CarryReleaseReason.RoundEnded);
            rear.GetComponent<Collider>().enabled = false;
            cart.SetHandleCount(2);
            yield return new WaitForSeconds(2f);
            Assert.That(cart.Carry.HandleCount, Is.EqualTo(2));
            Assert.That(cart.Carry.CarrierCount, Is.EqualTo(2));
            Assert.That(cart.Carry.IsCarriedBy(front), Is.True);
            Assert.That(cart.Water, Is.EqualTo(150));
        }

        [Test]
        public void WaterSurfaceStaysHorizontalInsideTiltedTub()
        {
            var pivot = new GameObject("Tilted water"); spawned.Add(pivot);
            pivot.transform.rotation = Quaternion.Euler(21f, 30f, 35f);
            var child = new GameObject("Water mesh"); child.transform.SetParent(pivot.transform, false);
            var filter = child.AddComponent<MeshFilter>();
            var water = pivot.AddComponent<HorizontalCartWater>();
            water.SetLevel(0.8f);
            var vertices = filter.sharedMesh.vertices;
            var indices = filter.sharedMesh.triangles;
            int horizontalFaces = 0;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = child.transform.TransformPoint(vertices[indices[i]]);
                Vector3 b = child.transform.TransformPoint(vertices[indices[i + 1]]);
                Vector3 c = child.transform.TransformPoint(vertices[indices[i + 2]]);
                Assert.That(a.y, Is.LessThanOrEqualTo(water.SurfacePoint.y + 0.0001f));
                if (Mathf.Abs(a.y - water.SurfacePoint.y) < 0.0001f &&
                    Mathf.Abs(b.y - a.y) < 0.0001f && Mathf.Abs(c.y - a.y) < 0.0001f)
                {
                    Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - a).normalized, Vector3.up), Is.GreaterThan(0.999f));
                    horizontalFaces++;
                }
            }
            Assert.That(horizontalFaces, Is.GreaterThan(0));
        }

        [Test]
        public void OnlyFirstSpillAnnouncesUntilNewLoad()
        {
            int heard = 0;
            cart.Stability.FirstSpill += (_, _) => heard++;
            cart.ChangeWater(-2, WaterLossReason.Tilt);
            cart.ChangeWater(-2, WaterLossReason.Tilt);
            Assert.That(heard, Is.EqualTo(1));
            cart.ChangeWater(-150, WaterLossReason.Poured);
            cart.ChangeWater(150, WaterLossReason.Filled);
            cart.ChangeWater(-2, WaterLossReason.Tilt);
            Assert.That(heard, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator SpillWaitsOneSecondAndUsesLoadRate()
        {
            cart.Stability.enabled = false;
            cart.Carry.AddTiltKick(Vector3.forward * 55f);
            cart.Carry.SetTiltTarget(Vector3.forward * 55f);
            yield return new WaitForSeconds(0.8f);
            Assert.That(cart.Water, Is.EqualTo(150));
            yield return new WaitForSeconds(1.2f);
            Assert.That(cart.Water, Is.InRange(142, 144));
        }
    }
}
