using Igruha.Core.Hub.Activities;
using NUnit.Framework;
using UnityEngine;

public sealed class StarshipRulesTests
{
    private static StarshipState Playing()
    {
        var s = StarshipRules.NewGame(1, true); StarshipRules.Start(ref s); s.Shield = 0; return s;
    }
    [Test] public void RestartResetsRunAndAdvancesSession()
    {
        var s = Playing(); s.Score = 600; s.Wave = 8; s.Lives = 0; s.Phase = StarshipPhase.GameOver;
        StarshipRules.Start(ref s);
        Assert.AreEqual(2, s.Session); Assert.AreEqual(0, s.Score); Assert.AreEqual(3, s.Lives);
        Assert.AreEqual(1, s.Wave); Assert.AreEqual(StarshipRules.FullFormation, s.Enemies);
    }
    [Test] public void InvalidInputDoesNotPoisonState()
    {
        var s = Playing(); var old = s;
        StarshipRules.Step(ref s, float.NaN, true, .02f); Assert.IsTrue(s.Equals(old));
        StarshipRules.Step(ref s, 0, true, float.PositiveInfinity); Assert.IsTrue(s.Equals(old));
    }
    [Test] public void ShipMovementIsServerSpeedLimited()
    {
        var s = Playing(); StarshipRules.Step(ref s, 100, false, .02f);
        Assert.That(s.Ship, Is.EqualTo(StarshipRules.ShipSpeed * .02f).Within(.0001f));
        for (int i = 0; i < 100; i++) StarshipRules.Step(ref s, 100, false, .02f);
        Assert.That(s.Ship, Is.LessThanOrEqualTo(StarshipRules.ShipLimit));
    }
    [Test] public void HoldingFireCannotExceedCooldownOrPool()
    {
        var s = Playing();
        for (int i = 0; i < 50; i++) StarshipRules.Step(ref s, .9f, true, .02f);
        Assert.That(s.ShotsFired, Is.InRange(3U, 5U)); Assert.That(s.Shots, Is.LessThanOrEqualTo(7));
    }
    [Test] public void SweptShotHitsEnemyOnce()
    {
        var s = Playing(); var p = StarshipRules.EnemyPosition(s, 12);
        s.Shots = 1; s.Shot0 = p - Vector2.up * .06f;
        StarshipRules.Step(ref s, 0, false, .05f);
        Assert.AreEqual(0U, s.Enemies & (1U << 12)); Assert.AreEqual(10, s.Score); Assert.AreEqual(0, s.Shots);
    }
    [Test] public void LastEnemyAdvancesWaveAndClearsProjectiles()
    {
        var s = Playing(); s.Enemies = 1U << 12; s.Shots = 1;
        s.Shot0 = StarshipRules.EnemyPosition(s, 12) - Vector2.up * .04f;
        s.Bombs = 1; s.Bomb0 = new Vector2(0, 0);
        StarshipRules.Step(ref s, 0, false, .02f);
        Assert.AreEqual(2, s.Wave); Assert.AreEqual(StarshipPhase.Ready, s.Phase);
        Assert.AreEqual(StarshipRules.FullFormation, s.Enemies); Assert.AreEqual(0, s.Shots | s.Bombs);
    }
    [Test] public void ThreeHitsEndGameAndPreserveScore()
    {
        var s = Playing(); s.Score = 90;
        for (int i = 0; i < 3; i++)
        {
            s.Shield = 0; s.Bombs = 1; s.Bomb0 = new Vector2(0, StarshipRules.ShipY + .04f);
            StarshipRules.Step(ref s, 0, false, .02f);
            if (i < 2) { Assert.AreEqual(StarshipPhase.Ready, s.Phase); StarshipRules.Start(ref s); }
        }
        Assert.AreEqual(StarshipPhase.GameOver, s.Phase); Assert.AreEqual(0, s.Lives); Assert.AreEqual(90, s.Score);
    }
    [Test] public void RespawnShieldAndSidewaysDodgePreventDamage()
    {
        var s = Playing(); s.Shield = 1; s.Bombs = 1; s.Bomb0 = new Vector2(0, StarshipRules.ShipY + .04f);
        StarshipRules.Step(ref s, 0, false, .02f); Assert.AreEqual(3, s.Lives);
        s.Shield = 0; s.Bombs = 1; s.Bomb0 = new Vector2(.5f, StarshipRules.ShipY + .04f);
        StarshipRules.Step(ref s, 0, false, .02f); Assert.AreEqual(3, s.Lives);
    }
    [Test] public void InvadersReachingShipCostOneLifeAndResetFormation()
    {
        var s = Playing(); s.FormationY = -.8f;
        StarshipRules.Step(ref s, 0, false, .02f);
        Assert.AreEqual(2, s.Lives); Assert.AreEqual(0, s.FormationY); Assert.AreEqual(StarshipPhase.Ready, s.Phase);
    }
    [Test] public void WaveDifficultyHasUpperLimits()
    {
        Assert.That(StarshipRules.FormationSpeed(10000), Is.LessThanOrEqualTo(.32f));
        Assert.That(StarshipRules.BombSpeed(10000), Is.LessThanOrEqualTo(.95f));
    }
}
