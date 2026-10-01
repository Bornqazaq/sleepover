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
        public IEnumerator PassiveHandSlowsWithoutInventedSpill()
        {
            cart.SetHandleCount(2);
            Carrier(0, Vector3.forward); Carrier(1, Vector3.zero);
            yield return new WaitForSeconds(1.5f);
            Assert.That(cart.Carry.CarrierCount, Is.EqualTo(2));
            Assert.That(cart.Carry.FlatVelocity.magnitude, Is.LessThan(1.5f));
            Assert.That(cart.Stability.State.Cause, Is.EqualTo(CartTiltCause.None));
            Assert.That(cart.Water, Is.EqualTo(150));
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
            Assert.That(cart.Carry.TiltAngle, Is.LessThan(1f));
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
            Assert.That(cart.Carry.TiltAngle, Is.LessThan(1f));
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
        public void OnlyWaterAbovePhysicalRimCanOverflow()
        {
            Assert.That(CartWaterSurface.Overflow(1f, new Vector2(0f, 0.1f), 300f,
                out _, out _, out _, out _), Is.Zero);
            float rate = CartWaterSurface.Overflow(1f, new Vector2(0f, 0.3f), 300f,
                out byte edge, out float along, out float width, out float risk);
            Assert.That(rate, Is.GreaterThan(0f)); Assert.That(edge, Is.EqualTo(2));
            Assert.That(CartWaterSurface.Height(1f, new Vector2(0f, 0.3f), along, CartWaterSurface.Length / 2f),
                Is.GreaterThan(CartWaterSurface.Depth));
            Assert.That(width, Is.GreaterThan(0.5f)); Assert.That(risk, Is.GreaterThan(1f));
            Assert.That(CartWaterSurface.Overflow(0.4f, new Vector2(0f, 0.3f), 300f,
                out _, out _, out _, out _), Is.Zero);
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
        public void CalmMeshHasFreeboardAndMatchesAuthoritativeSurface()
        {
            var pivot = new GameObject("Water"); spawned.Add(pivot);
            var child = new GameObject("Water mesh"); child.transform.SetParent(pivot.transform, false);
            var filter = child.AddComponent<MeshFilter>(); child.AddComponent<MeshRenderer>();
            var water = pivot.AddComponent<HorizontalCartWater>(); water.SetLevel(1f);
            float highest = float.NegativeInfinity;
            foreach (var vertex in filter.sharedMesh.vertices)
                highest = Mathf.Max(highest, child.transform.TransformPoint(vertex).y);
            Assert.That(highest, Is.EqualTo(CartWaterSurface.FullHeight).Within(0.0001f));
            Assert.That(highest, Is.LessThan(CartWaterSurface.Depth));
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
        public IEnumerator SurfaceMovesBeforeAnyWaterIsLost()
        {
            var pivot = new GameObject("Water"); pivot.transform.SetParent(cart.transform, false);
            var child = new GameObject("Water mesh"); child.transform.SetParent(pivot.transform, false);
            var filter = child.AddComponent<MeshFilter>(); child.AddComponent<MeshRenderer>();
            pivot.AddComponent<HorizontalCartWater>();
            yield return null;
            float before = filter.sharedMesh.vertices[16].y;
            cart.Stability.Impact(Vector3.right);
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(cart.Water, Is.EqualTo(150));
            Assert.That(filter.sharedMesh.vertices[16].y, Is.GreaterThan(before + 0.002f),
                "The warning wave must render without waiting for a water-count change");
        }

        [UnityTest]
        public IEnumerator ImpactLosesOnlyIntegratedVisibleFlowAndSettles()
        {
            cart.Stability.Impact(Vector3.right);
            Assert.That(cart.Water, Is.EqualTo(150), "No invisible instant penalty");
            float integral = 0f; bool sawWaveBeforeFlow = false, sawFlow = false;
            for (int i = 0; i < 250; i++)
            {
                yield return new WaitForFixedUpdate();
                var state = cart.Stability.State;
                integral += state.Outflow * Time.fixedDeltaTime;
                if (state.Wave.magnitude > 0.01f && state.Outflow == 0f && !sawFlow) sawWaveBeforeFlow = true;
                sawFlow |= state.Outflow > 0f;
            }
            Assert.That(sawWaveBeforeFlow && sawFlow, Is.True);
            Assert.That(150 - cart.Water, Is.EqualTo(Mathf.FloorToInt(integral)).Within(1));
            Assert.That(cart.Water, Is.InRange(115, 149));
            Assert.That(cart.Stability.State.Outflow, Is.Zero);
        }

        [UnityTest]
        public IEnumerator OppositeInputRocksThenSpillsThroughTheVisibleLowRim()
        {
            cart.SetHandleCount(2); Carrier(0, Vector3.forward); Carrier(1, Vector3.back);
            float integral = 0f, positive = 0f, negative = 0f;
            bool warningBeforeFlow = false, flow = false;
            for (int i = 0; i < 220; i++)
            {
                yield return new WaitForFixedUpdate();
                var s = cart.Stability.State;
                positive = Mathf.Max(positive, s.BodySlope.y); negative = Mathf.Min(negative, s.BodySlope.y);
                if (!flow && s.Risk > 0.45f && s.Outflow == 0) warningBeforeFlow = true;
                flow |= s.Outflow > 0; integral += s.Outflow * Time.fixedDeltaTime;
                if (i < 10) Assert.That(cart.Water, Is.EqualTo(150), "No instant input penalty");
                if (s.Outflow > 0)
                {
                    Vector2 wave = CartWaterSurface.InHeading(s.Wave, cart.transform.rotation);
                    Vector2 lean = CartWaterSurface.InHeading(s.BodySlope, cart.transform.rotation);
                    Vector3 p = CartWaterSurface.RimPoint(s.SpillSide, s.SpillAlong);
                    // The count may have dropped one unit after sampling this surface.
                    Assert.That(CartWaterSurface.Height((cart.Water + 1f) / 150f, wave, p.x, p.z, lean),
                        Is.GreaterThan(CartWaterSurface.Depth));
                }
            }
            Assert.That(cart.Carry.CarrierCount, Is.EqualTo(2));
            Assert.That(cart.Carry.FlatVelocity.magnitude, Is.LessThan(0.05f));
            Assert.That(positive, Is.GreaterThan(0.04f)); Assert.That(negative, Is.LessThan(-0.04f));
            Assert.IsTrue(warningBeforeFlow && flow);
            Assert.That(150 - cart.Water, Is.EqualTo(Mathf.FloorToInt(integral)).Within(1));
            Assert.That(cart.Water, Is.InRange(75, 149));
        }

        [UnityTest]
        public IEnumerator PerpendicularInputRocksWhileTheCartKeepsMoving()
        {
            cart.SetHandleCount(2);
            Carrier(0, Vector3.forward); Carrier(1, Vector3.right);
            Vector3 start = cart.transform.position;
            bool sawRock = false, sawFlow = false;
            for (int i = 0; i < 250; i++)
            {
                yield return new WaitForFixedUpdate();
                var state = cart.Stability.State;
                sawRock |= state.BodySlope.magnitude > 0.04f;
                sawFlow |= state.Outflow > 0f && state.Cause == CartTiltCause.Disagreement;
            }
            Assert.That(sawRock && sawFlow, Is.True, "Forward versus sideways must also punish disagreement.");
            Assert.That(Vector3.Distance(cart.transform.position, start), Is.GreaterThan(2f));
            Assert.That(cart.Carry.CarrierCount, Is.EqualTo(2));
            Assert.That(cart.Water, Is.InRange(110, 149), "Partial disagreement spills gradually.");
        }

        [UnityTest]
        public IEnumerator BriefWrongDirectionIsForgiven()
        {
            cart.SetHandleCount(2); Carrier(0, Vector3.forward); var other = Carrier(1, Vector3.back);
            yield return new WaitForSeconds(0.12f);
            Intent.SetValue(other, Vector3.forward);
            yield return new WaitForSeconds(2.5f);
            Assert.That(cart.Water, Is.EqualTo(150));
        }

        [UnityTest]
        public IEnumerator MatchingDirectionsLetsTheSuspensionSettleWithoutSnapping()
        {
            cart.SetHandleCount(2); Carrier(0, Vector3.forward); var other = Carrier(1, Vector3.back);
            yield return new WaitForSeconds(3.3f);
            Intent.SetValue(other, Vector3.forward);
            Vector2 before = cart.Stability.State.BodySlope;
            yield return new WaitForFixedUpdate();
            Assert.That(Vector2.Distance(before, cart.Stability.State.BodySlope), Is.LessThan(0.04f));
            yield return new WaitForSeconds(4f);
            Assert.That(cart.Stability.State.BodySlope.magnitude, Is.LessThan(0.005f));
            Assert.That(cart.Stability.State.Outflow, Is.Zero);
            int water = cart.Water;
            yield return new WaitForSeconds(1f);
            Assert.That(cart.Water, Is.EqualTo(water));
        }

        [Test]
        public void LeanedTubKeepsCalmWaterHorizontalAndOverflowsFromTheLowSide()
        {
            var lean = new Vector2(0.28f, 0f);
            Quaternion rotation = CartWaterSurface.BodyRotation(lean, Quaternion.identity);
            float left = CartWaterSurface.Height(0.5f, Vector2.zero, -0.3f, 0, lean);
            float right = CartWaterSurface.Height(0.5f, Vector2.zero, 0.3f, 0, lean);
            Assert.That((rotation * new Vector3(-0.3f, left, 0)).y,
                Is.EqualTo((rotation * new Vector3(0.3f, right, 0)).y).Within(0.0001f));
            float rate = CartWaterSurface.Overflow(1, Vector2.zero, config.OverflowRate,
                out byte side, out _, out _, out _, lean);
            Assert.That(rate, Is.GreaterThan(0)); Assert.That(side, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FastTurnMakesWaveAndSlowTurnPreservesWater()
        {
            cart.SetHandleCount(1); var player = Carrier(0, Vector3.forward);
            yield return new WaitForSeconds(3f);
            Intent.SetValue(player, Vector3.right);
            float peak = 0f;
            for (int i = 0; i < 75; i++) { yield return new WaitForFixedUpdate(); peak = Mathf.Max(peak, cart.Stability.State.Risk); }
            Assert.That(peak, Is.GreaterThan(1f)); Assert.That(cart.Water, Is.LessThan(150));
            Intent.SetValue(player, Vector3.zero); yield return new WaitForSeconds(3f);
            cart.ChangeWater(150, WaterLossReason.Filled); cart.Stability.ResetTrip();
            Intent.SetValue(player, Vector3.right * 0.2f); yield return new WaitForSeconds(2f);
            Intent.SetValue(player, Vector3.forward * 0.2f); yield return new WaitForSeconds(2f);
            Assert.That(cart.Water, Is.EqualTo(150));
        }

        [UnityTest]
        public IEnumerator ReverseInputBrakesBeforeBackingUpAndRaisesWave()
        {
            cart.SetHandleCount(1); var player = Carrier(0, Vector3.forward);
            yield return new WaitForSeconds(3f); Intent.SetValue(player, Vector3.back);
            yield return new WaitForFixedUpdate();
            Assert.That(cart.Carry.FlatVelocity.z, Is.GreaterThan(0f));
            yield return new WaitForSeconds(1.2f);
            Assert.That(cart.Carry.FlatVelocity.z, Is.LessThan(0f));
            Assert.That(cart.Water, Is.LessThan(150));
        }
    }
}
