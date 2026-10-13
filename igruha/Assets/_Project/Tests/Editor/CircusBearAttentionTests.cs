using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class CircusBearAttentionTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private Random.State random;
        [SetUp] public void Setup() { random = Random.state; Random.InitState(1310); }
        [TearDown] public void Clean()
        {
            foreach (var go in objects) Object.DestroyImmediate(go);
            objects.Clear(); Random.state = random;
        }
        private T Create<T>(string name, Vector3 position) where T : Component
        {
            var go = new GameObject(name); objects.Add(go); go.transform.position = position;
            return go.AddComponent<T>();
        }
        private PitBear Bear(Vector3 position)
        {
            var bear = Create<PitBear>("Bear", position);
            bear.Configure(8.2f, .85f, 2.35f, 3, 8, 8.64f);
            return bear;
        }

        [TestCase(180, 60, false)] [TestCase(-179, 30, false)]
        [TestCase(180, 60, true)]
        public void HalfTurnTravelsAnArcAndNeverRotatesAgainstTheWall(float degrees, int fps, bool atWall)
        {
            var bear = Bear(atWall ? Vector3.forward * 6.25f : Vector3.zero);
            var turn = typeof(PitBear).GetMethod("TurnToward", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector3 direction = Quaternion.Euler(0, degrees, 0) * Vector3.forward;
            float travel = 0, maxSide = 0;
            for (int frame = 0; frame < fps * 12; frame++)
            {
                Vector3 before = bear.transform.position; Quaternion rotation = bear.transform.rotation;
                turn.Invoke(bear, new object[] { direction, 1f / fps });
                float distance = Vector3.Distance(before, bear.transform.position);
                travel += distance; maxSide = Mathf.Max(maxSide, Mathf.Abs(bear.transform.position.x));
                float yaw = Quaternion.Angle(rotation, bear.transform.rotation);
                if (Vector3.Angle(rotation * Vector3.forward, direction) > 40)
                    Assert.That(distance, Is.GreaterThanOrEqualTo(yaw * Mathf.Deg2Rad * .8f - .0001f), "Large turns must move the root.");
                Assert.That(bear.transform.position.magnitude, Is.LessThanOrEqualTo(6.361f));
            }
            Assert.That(Vector3.Angle(bear.transform.forward, direction), Is.LessThan(.1f));
            Assert.That(travel, Is.GreaterThan(2.2f));
            Assert.That(maxSide, Is.GreaterThan(atWall ? .8f : 1.2f), "The turn must have a visible circular footprint, including a tighter backstep at the wall.");
        }

        [TestCase(3.6f)] [TestCase(8.64f)]
        public void OccupiedCageGetsRepeatedQuadrupedAttentionAtEveryHeight(float height)
        {
            var bear = Bear(Vector3.zero);
            var player = Create<PlayerController>("Cage occupant", new Vector3(0, height, -7.2f));
            var occupants = new[] { player };
            int looks = 0, watchingFrames = 0; bool wasWatching = false;
            Vector3 planted = Vector3.zero;
            for (int frame = 0; frame < 60 * 60; frame++)
            {
                bear.Tick(1f / 60, null, false, occupants);
                Assert.That(bear.State, Is.Not.EqualTo(PitBear.BearState.Taunt), "The removed rear must never start.");
                Vector3 toward = player.Position - bear.transform.position; toward.y = 0;
                bool watching = bear.State == PitBear.BearState.CageApproach &&
                    bear.AnimatorSpeed < .03f && Vector3.Angle(bear.transform.forward, toward) < 4.1f;
                if (watching && !wasWatching) { looks++; planted = bear.transform.position; }
                if (watching)
                {
                    watchingFrames++;
                    Assert.That(bear.PresentationVictim, Is.SameAs(player));
                    Assert.That(Vector3.Distance(planted, bear.transform.position), Is.LessThan(.01f));
                }
                wasWatching = watching;
            }
            TestContext.Out.WriteLine("looks=" + looks + " watching seconds=" + watchingFrames / 60f);
            Assert.That(looks, Is.InRange(3, 9));
            Assert.That(watchingFrames / 60f, Is.GreaterThan(6));
            bear.Tick(.1f, null, false, System.Array.Empty<PlayerController>());
            Assert.That(bear.State, Is.EqualTo(PitBear.BearState.Patrol));
            Assert.That(bear.CageTarget, Is.Null, "Empty/disconnected cages cannot retain attention.");
        }

        [Test] public void TurnEscapesWhenCompanionBlocksTheBackwardStepAtTheRim()
        {
            var bear=Bear(Vector3.forward*6.35f);
            var companion=Bear(Vector3.forward*1.55f);
            bear.ConfigureCompanion(companion,0);
            var turn=typeof(PitBear).GetMethod("TurnToward",BindingFlags.Instance|BindingFlags.NonPublic);
            Vector3 start=bear.transform.position;
            for(int frame=0;frame<1200;frame++)
            {
                turn.Invoke(bear,new object[]{Vector3.back,1f/60});
                Assert.That(Vector3.Distance(bear.transform.position,companion.transform.position),Is.GreaterThanOrEqualTo(4.799f));
                Assert.That(bear.transform.position.magnitude,Is.LessThanOrEqualTo(6.361f));
            }
            Assert.That(Vector3.Angle(bear.transform.forward,Vector3.back),Is.LessThan(.1f));
            Assert.That(Vector3.Distance(start,bear.transform.position),Is.GreaterThan(1));
        }

        [Test] public void PairWatchesDifferentOccupantsAndGroundRunnerTakesPriority()
        {
            var a = Bear(Vector3.left * 4); var b = Bear(Vector3.right * 4);
            a.ConfigureCompanion(b, 0); b.ConfigureCompanion(a, 1);
            var left = Create<PlayerController>("Left occupant", new Vector3(-7.2f, 8.64f, 0));
            var right = Create<PlayerController>("Right occupant", new Vector3(7.2f, 8.64f, 0));
            var occupants = new[] { left, right };
            int firstLooks = 0, secondLooks = 0, independentFrames = 0;
            bool first = false, second = false;
            for (int frame = 0; frame < 3600; frame++)
            {
                a.Tick(1f / 60, null, false, occupants); b.Tick(1f / 60, null, false, occupants);
                bool nextA = a.State == PitBear.BearState.CageApproach, nextB = b.State == PitBear.BearState.CageApproach;
                if (nextA && !first) firstLooks++;
                if (nextB && !second) secondLooks++;
                if (nextA != nextB) independentFrames++;
                Assert.That(a.State, Is.Not.EqualTo(PitBear.BearState.Taunt));
                Assert.That(b.State, Is.Not.EqualTo(PitBear.BearState.Taunt));
                if (nextA && nextB) Assert.That(a.CageTarget, Is.Not.SameAs(b.CageTarget));
                first = nextA; second = nextB;
                Assert.That(Vector3.Distance(a.transform.position, b.transform.position), Is.GreaterThanOrEqualTo(4.799f));
            }
            Assert.That(firstLooks, Is.GreaterThanOrEqualTo(3)); Assert.That(secondLooks, Is.GreaterThanOrEqualTo(3));
            Assert.That(independentFrames, Is.GreaterThan(300));
            left.transform.position = Vector3.zero;
            a.Tick(1f / 60, left, false, new[] { right });
            Assert.That(a.Target, Is.SameAs(left)); Assert.That(a.CageTarget, Is.Null);
            Assert.That(a.State, Is.EqualTo(PitBear.BearState.WindUp));
        }

        [Test] public void PursuitPassesInsideACompanionInsteadOfBrakingIntoTheRim()
        {
            var bear=Bear(new Vector3(5.30f,0,3.49f));
            var other=Bear(bear.transform.position+new Vector3(-.9f,0,.435f).normalized*4.81f);
            bear.Configure(8.2f,.85f,2.35f,0,8,8.64f);bear.ConfigureCompanion(other,0);
            bear.transform.rotation=Quaternion.Euler(0,51.2f,0);
            var player=Create<PlayerController>("Runner at rim",new Vector3(1.75f,0,8.1f));
            Vector3 start=bear.transform.position;float travel=0;
            for(int frame=0;frame<360;frame++)
            {
                Vector3 before=bear.transform.position;bear.Tick(1f/60,player,false);
                travel+=Vector3.Distance(before,bear.transform.position);
                Assert.That(Vector3.Distance(bear.transform.position,other.transform.position),Is.GreaterThanOrEqualTo(4.799f));
                Assert.That(bear.transform.position.magnitude,Is.LessThanOrEqualTo(6.361f));
            }
            Assert.That(travel,Is.GreaterThan(1),"Avoidance must choose a route that fits inside the arena.");
            Assert.That(Vector3.Distance(start,bear.transform.position),Is.GreaterThan(.5f));
        }
    }
}
