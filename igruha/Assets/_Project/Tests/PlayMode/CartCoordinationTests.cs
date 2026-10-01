using Igruha.Core.Items;
using Igruha.Minigames.CarryItem;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests.PlayMode
{
    public sealed class CartCoordinationTests
    {
        [Test]
        public void VectorHudCreatesItsRenderer()
        {
            var root = new GameObject("HUD renderer test", typeof(RectTransform));
            try
            {
                root.AddComponent<CartCoordinationGraphic>();
                Assert.IsNotNull(root.GetComponent<CanvasRenderer>());
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void DifferentKeysCanMeanTheSameWorldDirection()
        {
            var first = new CarryInputSample(Vector2.up, Vector2.up);
            var turned = new CarryInputSample(Vector2.up, Vector2.left);
            Assert.AreEqual(CartInputRelation.Together, CartCoordinationMath.Relation(first.World, turned.World));
            Assert.IsTrue(CartCoordinationHud.KeyActive(first.Move, 0));
            Assert.IsTrue(CartCoordinationHud.KeyActive(turned.Move, 1));
        }

        [Test]
        public void SameKeysCanOpposeWhenCamerasFaceEachOther()
        {
            var first = new CarryInputSample(Vector2.up, Vector2.up);
            var opposite = new CarryInputSample(Vector2.down, Vector2.up);
            Assert.AreEqual(CartInputRelation.Opposing, CartCoordinationMath.Relation(first.World, opposite.World));
            Assert.That((first.World + opposite.World).magnitude, Is.LessThan(0.001f));
        }

        [Test]
        public void IdleIsDistinctFromDivergenceAndDoesNotAccuseActiveCarrier()
        {
            Assert.AreEqual(CartInputRelation.Idle, CartCoordinationMath.Relation(Vector3.zero, Vector3.forward));
            Assert.AreEqual(CartInputRelation.Together, CartCoordinationMath.Relation(Vector3.forward, Vector3.zero));
            Assert.AreEqual(CartInputRelation.Diverging, CartCoordinationMath.Relation(Vector3.right, Vector3.forward));
        }

        [TestCase(0, 0, 1)]
        [TestCase(90, -1, 0)]
        [TestCase(180, 0, -1)]
        public void ArrowsFollowTheViewersCamera(float yaw, float x, float y)
        {
            var camera = Quaternion.Euler(20, yaw, 0);
            var screen = CartCoordinationMath.OnScreen(Vector3.forward, camera * Vector3.forward, camera * Vector3.right);
            Assert.That(Vector2.Distance(screen, new Vector2(x, y)), Is.LessThan(0.001f));
        }

        [Test]
        public void VerticalCameraKeepsCompassReadable()
        {
            Assert.That(CartCoordinationMath.OnScreen(Vector3.forward, Vector3.down, Vector3.right), Is.EqualTo(Vector2.up));
        }

        [Test]
        public void MalformedInputCannotPoisonSnapshotOrPhysics()
        {
            Assert.That(CarryInputSample.Sanitize(new Vector2(float.NaN, 1)), Is.EqualTo(Vector2.zero));
            Assert.That(CarryInputSample.Sanitize(new Vector2(1, float.PositiveInfinity)), Is.EqualTo(Vector2.zero));
            var sample = new CarryInputSample(new Vector2(100, 100), new Vector2(-100, -100));
            Assert.That(sample.World.magnitude, Is.InRange(0.99f, 1.01f));
            Assert.That(sample.Move.magnitude, Is.InRange(0.99f, 1.01f));
        }

        [Test]
        public void ReleaseAndReplacementClearThePreviousOccupantsInput()
        {
            MultiCarryNetState state = default;
            for (int i = 0; i < 4; i++)
            {
                state.Set(i, (ulong)(i + 1));
                state.SetInput(i, new CarryInputSample(Vector2.one, Vector2.up));
                state.Set(i, 0);
                Assert.That(state.InputOf(i).World, Is.EqualTo(Vector3.zero));
                state.Set(i, 10);
                Assert.That(state.InputOf(i).Move, Is.EqualTo(Vector2.zero));
            }
        }
    }
}
