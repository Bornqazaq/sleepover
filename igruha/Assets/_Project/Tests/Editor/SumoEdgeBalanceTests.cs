using Igruha.Minigames.SumoRing;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class SumoEdgeBalanceTests
    {
        private SumoConfig config;
        [SetUp] public void SetUp() => config = ScriptableObject.CreateInstance<SumoConfig>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(config);
        private Vector3 Feet(float radius, float angle = 0) => new Vector3(Mathf.Cos(angle) * radius, config.Height, Mathf.Sin(angle) * radius);

        [Test]
        public void QuietInCentreAndGraduallyBalancesTowardExposedLip()
        {
            Assert.That(SumoEdgeBalance.Evaluate(config, Feet(0), 0, out _), Is.Zero);
            float far = SumoEdgeBalance.Evaluate(config, Feet(config.Radius - .6f), 0, out var farDirection);
            float near = SumoEdgeBalance.Evaluate(config, Feet(config.Radius - .1f), 0, out var direction);
            Assert.That(far, Is.InRange(.01f, .25f));
            Assert.That(near, Is.GreaterThan(.9f));
            Assert.That(direction.x, Is.GreaterThan(.98f));
            Assert.That(Vector3.Dot(direction, farDirection), Is.GreaterThan(.98f));
        }
        [Test]
        public void CollapseMovesTheDangerToNewEdgeAndPermanentCentre()
        {
            var feet = Feet(config.OuterRadius(1) - .1f);
            Assert.That(SumoEdgeBalance.Evaluate(config, feet, 9, out _), Is.Zero);
            Assert.That(SumoEdgeBalance.Evaluate(config, feet, 11, out _), Is.GreaterThan(.9f));
            Assert.That(SumoEdgeBalance.Evaluate(config, Feet(config.CentreRadius - .1f), 100, out _), Is.GreaterThan(.9f));
            Assert.That(SumoEdgeBalance.Evaluate(config, Feet(0), 100, out _), Is.Zero);
        }
        [Test]
        public void PartialSweepAlsoDetectsSideCliffBetweenStandingAndMissingSectors()
        {
            // Sector zero has fallen, sector one is still standing. Its radial outer
            // edge is distant, but the missing neighbour is immediately to our right.
            float seam = Mathf.PI * 2 / config.Sectors;
            var feet = Feet(config.Radius - .6f, seam + .01f);
            double elapsed = (config.SegmentFallsAt(0, 0) + config.SegmentFallsAt(0, 1)) * .5;
            float danger = SumoEdgeBalance.Evaluate(config, feet, elapsed, out var direction);
            var towardMissing = new Vector3(Mathf.Sin(seam), 0, -Mathf.Cos(seam));
            Assert.That(danger, Is.GreaterThan(.9f));
            Assert.That(Vector3.Dot(direction, towardMissing), Is.GreaterThan(.75f));
        }
        [Test]
        public void CountdownAirborneAndAlreadyFallingDoNotPretendToHaveFooting()
        {
            var feet = Feet(config.Radius - .1f);
            Assert.That(SumoEdgeBalance.Evaluate(config, feet, -1, out _), Is.Zero);
            Assert.That(SumoEdgeBalance.Evaluate(config, feet + Vector3.up, 1, out _), Is.Zero);
            Assert.That(SumoEdgeBalance.Evaluate(config, feet - Vector3.up, 1, out _), Is.Zero);
            Assert.That(SumoEdgeBalance.Evaluate(config, Feet(config.Radius + .2f), 1, out _), Is.Zero);
        }
    }
}
