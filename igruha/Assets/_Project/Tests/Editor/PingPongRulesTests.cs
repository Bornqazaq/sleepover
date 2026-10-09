using System.Reflection;
using Igruha.Core.Hub.Activities;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class PingPongRulesTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject root;
        private PingPongTable table;
        private PingPongFlight incoming;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("PingPongRulesFixture");
            table = root.AddComponent<PingPongTable>();
            for (byte side = 0; side < 2; side++)
            {
                var go = new GameObject("Seat" + side); go.transform.SetParent(root.transform);
                var seat = go.AddComponent<PingPongSeat>();
                typeof(HubActivityStation).GetField("offlineOccupant", Private).SetValue(seat, side == 0 ? 11ul : 22ul);
                typeof(HubActivityStation).GetField("offlinePhase", Private).SetValue(seat, HubActivityPhase.Occupied);
                Set(table, side == 0 ? "left" : "right", seat);
            }
            incoming = PingPongRules.Serve(1, 0, 10);
            Set(table, "offlineState", new PingPongState { Phase = PingPongPhase.Playing, Flight = incoming });
            Set(table, "nextFlight", 1u);
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        [TestCase(0, 22ul, 1u)]
        [TestCase(1, 22ul, 1u)]
        [TestCase(0, 11ul, 7u)]
        [TestCase(2, 11ul, 1u)]
        public void WrongSenderSideOrFlightCannotReturn(int side, ulong sender, uint flight)
        {
            Assert.That(Hit((byte)side, sender, flight, incoming.ContactAt, incoming.ContactAt, 0), Is.False);
            Assert.That(table.State.Flight.Id, Is.EqualTo(1));
        }

        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(double.NegativeInfinity)]
        public void NonFiniteTimestampCannotChangeRally(double stamp)
        {
            Assert.That(Hit(0, 11, 1, stamp, incoming.ContactAt, .15f), Is.False);
            Assert.That(table.State.Flight.Id, Is.EqualTo(1));
        }

        [Test]
        public void DelayedHonestInputUsesTheTimeOfTheDisplayedBall()
        {
            double stamp = incoming.ContactAt - .1;
            Assert.That(Hit(0, 11, 1, stamp, stamp + .22, .15f), Is.True);
            Assert.That(table.State.Flight.Target, Is.EqualTo(1));
            Assert.That(table.State.Flight.StartsAt, Is.EqualTo(incoming.ContactAt));
            Assert.That(table.State.Previous.Id, Is.EqualTo(1));
            Assert.That(Hit(0, 11, 1, stamp, stamp + .23, .15f), Is.False, "replay must not create another return");
        }

        [Test]
        public void EarlyAttemptCannotBeRepairedBySpammingAtContact()
        {
            Assert.That(Hit(0, 11, 1, incoming.StartsAt + .1, incoming.StartsAt + .1, 0), Is.False);
            Assert.That(Hit(0, 11, 1, incoming.ContactAt, incoming.ContactAt, 0), Is.False);
            Assert.That(table.State.Flight.Id, Is.EqualTo(1));
        }

        [Test]
        public void ForgedOldOrFutureTimesAreRejectedBeforeTheyConsumeAnAttempt()
        {
            Assert.That(Hit(0, 11, 1, incoming.ContactAt, incoming.ContactAt + 1, .15f), Is.False);
            Assert.That(Hit(0, 11, 1, incoming.ContactAt, incoming.ContactAt - 1, .15f), Is.False);
            Assert.That(Hit(0, 11, 1, incoming.ContactAt, incoming.ContactAt, 0), Is.True);
        }

        [Test]
        public void LateClickOutsideWindowMissesEvenWithALargePingBudget()
        {
            double stamp = incoming.ContactAt + PingPongRules.LateWindow + .03;
            Assert.That(Hit(0, 11, 1, stamp, stamp + .2, .3f), Is.False);
        }

        [TestCase(0)] [TestCase(1)]
        public void FlightClearsNetBouncesOnReceivingHalfAndJoinsNextFlight(int side)
        {
            var f = PingPongRules.Serve(1, (byte)side, 10);
            Vector3 start = PingPongRules.Position(f, f.StartsAt);
            Vector3 bounce = PingPongRules.Position(f, f.StartsAt + f.Duration * PingPongRules.BounceFraction);
            Assert.That(Vector3.Distance(start, f.From), Is.LessThan(.001f));
            Assert.That(bounce.y, Is.EqualTo(PingPongRules.TableHeight + PingPongRules.BallRadius).Within(.001));
            Assert.That(Mathf.Sign(bounce.x), Is.EqualTo(side == 0 ? -1 : 1));
            for (int i = 0; i < 100; i++)
            {
                Vector3 p = PingPongRules.Position(f, f.StartsAt + f.Duration * i / 100d);
                Assert.That(p.y, Is.GreaterThanOrEqualTo(PingPongRules.TableHeight + PingPongRules.BallRadius - .001f));
                if (Mathf.Abs(p.x) < .07f) Assert.That(p.y - PingPongRules.BallRadius, Is.GreaterThan(1.05f));
            }
            var next = PingPongRules.Return(f, 2);
            Assert.That(Vector3.Distance(PingPongRules.Position(f, f.ContactAt), PingPongRules.Position(next, next.StartsAt)), Is.LessThan(.001f));
            var state = new PingPongState { Flight = next, Previous = f };
            Assert.That(PingPongRules.VisibleFlight(state, f.ContactAt - .01).Id, Is.EqualTo(1));
            Assert.That(PingPongRules.VisibleFlight(state, f.ContactAt + .01).Id, Is.EqualTo(2));
        }

        [Test]
        public void PaceHasAComfortableLimitAndNetworkGraceIsBounded()
        {
            Assert.That(PingPongRules.Duration(3), Is.EqualTo(PingPongRules.StartDuration));
            Assert.That(PingPongRules.Duration(4), Is.LessThan(PingPongRules.StartDuration));
            Assert.That(PingPongRules.Duration(1000000), Is.EqualTo(PingPongRules.MinimumDuration));
            Assert.That(PingPongRules.RewindBudget(60), Is.EqualTo(PingPongRules.MaximumRewind));
        }

        private bool Hit(byte side, ulong sender, uint id, double stamp, double now, float rtt) =>
            (bool)typeof(PingPongTable).GetMethod("TryHit", Private).Invoke(table, new object[] { side, sender, id, stamp, now, rtt });
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    }
}
