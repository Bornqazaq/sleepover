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

        private WaterBottle Bottle(TeamSide team = TeamSide.A)
        {
            var root = new GameObject("DeliveryBottle");
            spawned.Add(root);
            root.transform.position = tank.transform.position;
            root.AddComponent<CapsuleCollider>();
            var body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
            var carry = root.AddComponent<MultiCarryObject>();
            var bottle = root.AddComponent<WaterBottle>();
            bottle.Initialize(config, team, 1, -100f);
            carry.enabled = false;
            bottle.enabled = false;
            Physics.SyncTransforms();
            return bottle;
        }

        [UnityTest]
        public IEnumerator ResetWhileBottleOverlapsStillDelivers()
        {
            Bottle();
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.GreaterThan(0), "baseline delivery");
            tank.Configure(config, TeamSide.A);
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.GreaterThan(0), "overlap must survive reset without another Enter");
        }

        [UnityTest]
        public IEnumerator DisabledBottleColliderStopsDelivery()
        {
            var bottle = Bottle();
            yield return new WaitForSeconds(0.2f);
            bottle.GetComponent<Collider>().enabled = false;
            yield return new WaitForFixedUpdate();
            yield return null;
            int before = tank.Water;
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator LeavingAndReturningPreservesRemainingWater()
        {
            var bottle = Bottle();
            yield return new WaitForSeconds(0.2f);
            bottle.transform.position += Vector3.right * 10f;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
            int before = tank.Water;
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.EqualTo(before));
            bottle.transform.position = tank.transform.position;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.GreaterThan(before));
            Assert.That(tank.Water + bottle.Water, Is.EqualTo(config.BottleCapacity));
        }

        [UnityTest]
        public IEnumerator OpponentBottleDoesNotScore()
        {
            var bottle = Bottle(TeamSide.B);
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.Zero);
            Assert.That(bottle.Water, Is.EqualTo(config.BottleCapacity));
        }

        [UnityTest]
        public IEnumerator CrowdedZoneDoesNotLoseBottle()
        {
            for (int i = 0; i < 80; i++)
            {
                var clutter = new GameObject("ZoneClutter");
                spawned.Add(clutter);
                clutter.transform.position = tank.transform.position;
                clutter.AddComponent<BoxCollider>();
            }
            Bottle();
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator RotatedZoneDoesNotAcceptEmptyBoundsCorner()
        {
            tank.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            var bottle = Bottle();
            bottle.transform.position += new Vector3(2.5f, 0f, 2.5f);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.2f);
            Assert.That(tank.Water, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MultipleCollidersDoNotMultiplyPourRate()
        {
            var bottle = Bottle();
            var extra = new GameObject("ExtraBottleCollider");
            extra.transform.SetParent(bottle.transform, false);
            extra.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            yield return new WaitForSeconds(config.PourSeconds * 0.25f);
            Assert.That(tank.Water, Is.InRange(config.BottleCapacity / 8, config.BottleCapacity / 2));
            Assert.That(tank.Water + bottle.Water, Is.EqualTo(config.BottleCapacity));
        }

        [UnityTest]
        public IEnumerator FiveDeliveriesCreditBeforeDespawnExactlyOnce()
        {
            int finished = 0;
            int delivered = 0;
            tank.BottleFinished += () => finished++;
            tank.Delivered += (amount, time) => delivered += amount;
            for (int i = 1; i <= 5; i++)
            {
                var bottle = Bottle();
                bottle.Gone += gone => Assert.That(tank.Water, Is.EqualTo(delivered));
                float deadline = Time.realtimeSinceStartup + config.PourSeconds + 2f;
                while (bottle != null && !bottle.IsGone && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(bottle == null || bottle.IsGone, Is.True, "delivery timeout");
                Assert.That(finished, Is.EqualTo(i));
                Assert.That(tank.Water, Is.EqualTo(Mathf.Min(config.TankCapacity, i * config.BottleCapacity)));
            }
        }
    }
}
