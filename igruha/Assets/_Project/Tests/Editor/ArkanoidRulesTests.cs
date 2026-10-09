using Igruha.Core.Hub.Activities;
using NUnit.Framework;
using UnityEngine;

public sealed class ArkanoidRulesTests
{
    private static ArkanoidState Playing()
    {
        var s = ArkanoidRules.NewGame(7, true);
        ArkanoidRules.Launch(ref s);
        return s;
    }

    [Test] public void MissConsumesExactlyOneLifeAndWaitsForClick()
    {
        var s = Playing();
        s.Ball = new Vector2(.8f, -.6f); s.Velocity = Vector2.down;
        ArkanoidRules.Step(ref s, 0, .1f);
        Assert.AreEqual(2, s.Lives); Assert.AreEqual(ArkanoidPhase.Ready, s.Phase);
        for (int i = 0; i < 100; i++) ArkanoidRules.Step(ref s, 0, .02f);
        Assert.AreEqual(2, s.Lives);
    }

    [Test] public void LastLifeEndsAndClickRestarts()
    {
        var s = Playing(); s.Lives = 1; s.Score = 90;
        s.Ball = new Vector2(.8f, -.65f); s.Velocity = Vector2.down;
        ArkanoidRules.Step(ref s, 0, .02f);
        Assert.AreEqual(ArkanoidPhase.GameOver, s.Phase);
        ArkanoidRules.Launch(ref s);
        Assert.AreEqual(ArkanoidPhase.Playing, s.Phase); Assert.AreEqual(3, s.Lives);
        Assert.AreEqual(0, s.Score); Assert.AreEqual(7, s.Session);
    }

    [TestCase(-.12f, -1)] [TestCase(.12f, 1)]
    public void PaddleContactSteersBall(float offset, int sign)
    {
        var s = Playing(); s.Ball = new Vector2(offset, -.43f); s.Velocity = Vector2.down * 2.4f;
        ArkanoidRules.Step(ref s, 0, .03f);
        Assert.Greater(s.Velocity.y, 0); Assert.AreEqual(sign, Mathf.Sign(s.Velocity.x));
    }

    [Test] public void FastBallCannotTunnelThroughBrick()
    {
        var s = Playing(); s.Ball = ArkanoidRules.BrickCenter(2) + Vector2.down * .08f;
        s.Velocity = Vector2.up * ArkanoidRules.MaxSpeed;
        ArkanoidRules.Step(ref s, 0, .04f);
        Assert.AreEqual(0UL, s.Bricks & (1UL << 2)); Assert.AreEqual(10, s.Score);
        Assert.Less(s.Velocity.y, 0);
    }

    [Test] public void SideCollisionReflectsHorizontally()
    {
        var s = Playing(); s.Bricks = 1UL;
        s.Ball = ArkanoidRules.BrickCenter(0) + Vector2.right * .16f;
        s.Velocity = Vector2.left * ArkanoidRules.MaxSpeed;
        s.Bricks |= 1UL << 39;
        ArkanoidRules.Step(ref s, 0, .02f);
        Assert.Greater(s.Velocity.x, 0); Assert.AreEqual(1UL << 39, s.Bricks);
    }

    [Test] public void LastBrickStartsNextLevelPreservingLivesAndScore()
    {
        var s = Playing(); s.Bricks = 1UL; s.Lives = 2;
        s.Ball = ArkanoidRules.BrickCenter(0) + Vector2.down * .07f;
        s.Velocity = Vector2.up;
        ArkanoidRules.Step(ref s, 0, .03f);
        Assert.AreEqual(2, s.Level); Assert.AreEqual(2, s.Lives); Assert.AreEqual(10, s.Score);
        Assert.AreEqual(ArkanoidRules.AllBricks, s.Bricks); Assert.AreEqual(ArkanoidPhase.Ready, s.Phase);
    }

    [Test] public void InputIsBoundedAndInvalidNumbersCannotPoisonSimulation()
    {
        var s = Playing(); var before = s;
        ArkanoidRules.Step(ref s, float.NaN, .02f);
        Assert.IsTrue(s.Equals(before));
        ArkanoidRules.Step(ref s, float.PositiveInfinity, .02f);
        Assert.IsTrue(s.Equals(before));
        ArkanoidRules.Step(ref s, 100, .02f);
        Assert.LessOrEqual(s.Paddle, ArkanoidRules.PaddleSpeed * .02f + .0001f);
        for (int i = 0; i < 20; i++) ArkanoidRules.Step(ref s, 100, .02f);
        Assert.LessOrEqual(s.Paddle, ArkanoidRules.PaddleLimit);
    }

    [Test] public void SpeedRampsButIsCapped()
    {
        var s = Playing(); float initial = ArkanoidRules.Speed(s);
        s.Hits = 10; Assert.Greater(ArkanoidRules.Speed(s), initial);
        s.Hits = 10000; s.Level = 10000;
        Assert.AreEqual(ArkanoidRules.MaxSpeed, ArkanoidRules.Speed(s));
    }

    [Test] public void WallAndRoofReflectInwards()
    {
        var s = Playing(); s.Bricks = 0; s.Ball = new Vector2(.975f, .605f);
        s.Velocity = Vector2.one.normalized * ArkanoidRules.MaxSpeed;
        ArkanoidRules.Step(ref s, 0, .01f);
        Assert.Less(s.Velocity.x, 0); Assert.Less(s.Velocity.y, 0);
    }
}
