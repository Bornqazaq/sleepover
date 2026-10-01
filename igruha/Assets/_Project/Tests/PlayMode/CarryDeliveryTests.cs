using System.Collections;
using System.Collections.Generic;
using Igruha.Core.Items;
using Igruha.Core.Session;
using Igruha.Minigames.CarryItem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Igruha.Tests.PlayMode
{
    /// <summary>
    /// Зона бака и зона крана «Переноски» v2 на настоящей физике: серверный
    /// запрос формы, а не триггеры. Регрессии IGR-579/592 — потерянные
    /// Enter/Exit — здесь и ловятся.
    /// </summary>
    public sealed class CarryDeliveryTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private CarryItemConfig config;
        private WaterTank tank;
        private bool previousBackground;

        [SetUp]
        public void SetUp()
        {
            previousBackground = Application.runInBackground;
            Application.runInBackground = true;
            config = ScriptableObject.CreateInstance<CarryItemConfig>();
            spawned.Add(config);
            var root = new GameObject("DeliveryTank");
            spawned.Add(root);
            root.transform.position = Vector3.up * 1000f;
            var zone = root.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.size = Vector3.one * 4f;
            tank = root.AddComponent<WaterTank>();
            tank.Configure(config, TeamSide.A);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            spawned.Clear();
            Application.runInBackground = previousBackground;
        }

        /// <summary>Полная неподвижная тележка команды в заданной точке. Компоненты выключены: проверяется зона, а не ход.</summary>
        private WaterCart Cart(Vector3 position, TeamSide team = TeamSide.A, bool full = true)
        {
            var root = new GameObject("DeliveryCart");
            spawned.Add(root);
            root.transform.position = position;
            root.AddComponent<BoxCollider>();
            var body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
            var carry = root.AddComponent<MultiCarryObject>();
            var cart = root.AddComponent<WaterCart>();
            cart.Initialize(config, team, 1, -100f, position, Quaternion.identity);
            if (full) cart.ChangeWater(config.CartCapacity, WaterLossReason.Filled);
            carry.enabled = false;
            cart.enabled = false;
            Physics.SyncTransforms();
            return cart;
        }

        private WaterCart CartAtTank(TeamSide team = TeamSide.A) => Cart(tank.transform.position, team);

        private WaterTap Tap(TeamSide team = TeamSide.A)
        {
            var root = new GameObject("FillTap");
            spawned.Add(root);
            root.transform.position = Vector3.up * 1200f;
            var zone = root.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.size = Vector3.one * 3f;
            var tap = root.AddComponent<WaterTap>();
            tap.Configure(config, team);
            return tap;
        }

        [UnityTest]
        public IEnumerator ResetWhileCartOverlapsStillDelivers()
        {
            CartAtTank();
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.GreaterThan(0), "baseline delivery");
            tank.Configure(config, TeamSide.A);
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.GreaterThan(0), "overlap must survive reset without another Enter");
        }

        [UnityTest]
        public IEnumerator DisabledCartColliderStopsDelivery()
        {
            var cart = CartAtTank();
            yield return new WaitForSeconds(0.2f);
            cart.GetComponent<Collider>().enabled = false;
            yield return new WaitForFixedUpdate();
            yield return null;
            int before = tank.Water;
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator LeavingAndReturningPreservesRemainingWater()
        {
            var cart = CartAtTank();
            yield return new WaitForSeconds(0.2f);
            cart.transform.position += Vector3.right * 10f;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
            int before = tank.Water;
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.EqualTo(before));
            cart.transform.position = tank.transform.position;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.GreaterThan(before));
            Assert.That(tank.Water + cart.Water, Is.EqualTo(config.CartCapacity));
        }

        [UnityTest]
        public IEnumerator OneHoseDrainsOneVesselAndDoesNotWasteWaterAtCapacity()
        {
            var first=CartAtTank();var second=CartAtTank();
            yield return new WaitForSeconds(.3f);
            Assert.That(first.IsPouring ^ second.IsPouring,Is.True,"one visible hose, one source");
            Assert.That(tank.Water,Is.InRange(1,12),"single pump rate rather than two simultaneous pumps");
            Assert.That(first.Water+second.Water+tank.Water,Is.EqualTo(300));
            typeof(WaterTank).GetField("water",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(tank,config.TankCapacity-2);
            int before=first.Water+second.Water;
            yield return new WaitForSeconds(.3f);
            Assert.That(tank.Water,Is.EqualTo(config.TankCapacity));
            Assert.That(first.Water+second.Water,Is.EqualTo(before-2));
            Assert.That(first.IsPouring||second.IsPouring,Is.False);
        }

        [UnityTest]
        public IEnumerator OpponentCartDoesNotScore()
        {
            var cart = CartAtTank(TeamSide.B);
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.Zero);
            Assert.That(cart.Water, Is.EqualTo(config.CartCapacity));
        }

        [UnityTest]
        public IEnumerator CrowdedZoneDoesNotLoseCart()
        {
            for (int i = 0; i < 80; i++)
            {
                var clutter = new GameObject("ZoneClutter");
                spawned.Add(clutter);
                clutter.transform.position = tank.transform.position;
                clutter.AddComponent<BoxCollider>();
            }
            CartAtTank();
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator RotatedZoneDoesNotAcceptEmptyBoundsCorner()
        {
            tank.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            var cart = CartAtTank();
            cart.transform.position += new Vector3(2.5f, 0f, 2.5f);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MultipleCollidersDoNotMultiplyPourRate()
        {
            var cart = CartAtTank();
            var extra = new GameObject("ExtraCartCollider");
            extra.transform.SetParent(cart.transform, false);
            extra.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            float fullPour = config.CartCapacity / config.PourRate;
            yield return new WaitForSeconds(fullPour * 0.25f);
            Assert.That(tank.Water, Is.InRange(config.CartCapacity / 8, config.CartCapacity / 2));
            Assert.That(tank.Water + cart.Water, Is.EqualTo(config.CartCapacity));
        }

        [UnityTest]
        public IEnumerator FourTripsWithOneCartFinishExactlyOnceEach()
        {
            int finished = 0;
            int delivered = 0;
            tank.TripFinished += () => finished++;
            tank.Delivered += (amount, time) => delivered += amount;
            var cart = CartAtTank();
            for (int i = 1; i <= 4; i++)
            {
                if (i > 1) cart.ChangeWater(config.CartCapacity, WaterLossReason.Filled);
                float deadline = Time.realtimeSinceStartup + config.CartCapacity / config.PourRate + 2f;
                while (cart.Water > 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(cart.Water, Is.Zero, "delivery timeout");
                yield return new WaitForFixedUpdate();
                Assert.That(finished, Is.EqualTo(i), "one trip per emptying");
                Assert.That(tank.Water, Is.EqualTo(Mathf.Min(config.TankCapacity, i * config.CartCapacity)));
                Assert.That(tank.Water, Is.EqualTo(delivered));
            }
        }

        [UnityTest]
        public IEnumerator EmptyCartInZoneDoesNotFinishTrip()
        {
            int finished = 0;
            tank.TripFinished += () => finished++;
            Cart(tank.transform.position, TeamSide.A, full: false);
            yield return new WaitForSeconds(0.3f);
            Assert.That(finished, Is.Zero);
            Assert.That(tank.Water, Is.Zero);
        }

        [UnityTest]
        public IEnumerator TapFillsOnlyWhileCartIsInsideZone()
        {
            var tap = Tap();
            var cart = Cart(tap.transform.position, TeamSide.A, full: false);
            tap.AttachCart(cart);
            yield return new WaitForSeconds(0.4f);
            int inside = cart.Water;
            Assert.That(inside, Is.GreaterThan(0), "fills inside the zone");
            Assert.That(cart.IsFilling, Is.True);
            cart.transform.position += Vector3.right * 10f;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
            int outside = cart.Water;
            yield return new WaitForSeconds(0.3f);
            Assert.That(cart.Water, Is.EqualTo(outside), "keeps what it took, gains nothing outside");
            Assert.That(cart.IsFilling, Is.False);
        }

        [UnityTest]
        public IEnumerator TapFillsCapturedVesselsAndStopsAtCapacity()
        {
            var tap = Tap(TeamSide.A);
            var foreign = Cart(tap.transform.position, TeamSide.B, full: false);
            tap.AttachCart(foreign);
            yield return new WaitForSeconds(0.3f);
            Assert.That(foreign.Water, Is.GreaterThan(0), "physical stream fills any captured vessel");
            tap.DetachCart();
            var own = Cart(tap.transform.position, TeamSide.A, full: true);
            tap.AttachCart(own);
            yield return new WaitForSeconds(0.3f);
            Assert.That(own.Water, Is.EqualTo(config.CartCapacity), "no overflow");
            Assert.That(own.IsFilling, Is.False, "full cart is not 'filling'");
        }
    }
}
