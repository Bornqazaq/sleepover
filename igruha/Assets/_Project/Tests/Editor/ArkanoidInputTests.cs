using System.Reflection;
using Igruha.Core.Hub.Activities;
using NUnit.Framework;
using UnityEngine;

public sealed class ArkanoidInputTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private ArkanoidStation station;

    [SetUp] public void SetUp()
    {
        root = new GameObject("Arcade gate test");
        station = root.AddComponent<ArkanoidStation>();
        typeof(HubActivityStation).GetField("offlineOccupant", Private).SetValue(station, 5UL);
        typeof(ArkanoidStation).GetField("simulation", Private).SetValue(station, ArkanoidRules.NewGame(41, true));
    }
    [TearDown] public void TearDown() => Object.DestroyImmediate(root);
    private void Move(float paddle, uint session, uint sequence, ulong sender) =>
        typeof(ArkanoidStation).GetMethod("AcceptInput", Private).Invoke(station, new object[] { paddle, session, sequence, sender });
    private float Target => (float)typeof(ArkanoidStation).GetField("target", Private).GetValue(station);

    [Test] public void NonOwnerAndOldSessionCannotControlPaddle()
    {
        Move(.5f, 41, 1, 6); Assert.AreEqual(0, Target);
        Move(.5f, 40, 2, 5); Assert.AreEqual(0, Target);
        Move(.5f, 41, 3, 5); Assert.AreEqual(.5f, Target);
    }
    [Test] public void InvalidAndReorderedPacketsAreIgnored()
    {
        Move(.4f, 41, 5, 5);
        Move(float.NaN, 41, 6, 5); Assert.AreEqual(.4f, Target);
        Move(float.PositiveInfinity, 41, 7, 5); Assert.AreEqual(.4f, Target);
        Move(-.6f, 41, 4, 5); Assert.AreEqual(.4f, Target);
        Move(-.6f, 41, 5, 5); Assert.AreEqual(.4f, Target);
        Move(-2, 41, 6, 5); Assert.AreEqual(-ArkanoidRules.PaddleLimit, Target);
    }
    [Test] public void OnlyCurrentOwnerCanLaunch()
    {
        var play = typeof(ArkanoidStation).GetMethod("AcceptPlay", Private);
        play.Invoke(station, new object[] { 0f, 41U, 6UL });
        Assert.AreEqual(ArkanoidPhase.Ready, station.State.Phase);
        play.Invoke(station, new object[] { 0f, 40U, 5UL });
        Assert.AreEqual(ArkanoidPhase.Ready, station.State.Phase);
        play.Invoke(station, new object[] { float.NaN, 41U, 5UL });
        Assert.AreEqual(ArkanoidPhase.Ready, station.State.Phase);
        play.Invoke(station, new object[] { 0f, 41U, 5UL });
        Assert.AreEqual(ArkanoidPhase.Playing, station.State.Phase);
    }
}
