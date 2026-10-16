using System.Reflection;
using Igruha.Core.Hub.Activities;
using NUnit.Framework;
using UnityEngine;

public sealed class ArcadePauseTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private ArcadeStation station;
    [TearDown] public void TearDown() { if (root != null) Object.DestroyImmediate(root); }
    private void Create(bool starships)
    {
        root = new GameObject("Arcade pause gate");
        station = starships ? (ArcadeStation)root.AddComponent<StarshipStation>() : root.AddComponent<ArkanoidStation>();
        typeof(HubActivityStation).GetField("offlineOccupant", Private).SetValue(station, 5UL);
        if (starships)
        {
            var s = StarshipRules.NewGame(41, true); StarshipRules.Start(ref s);
            station.GetType().GetField("simulation", Private).SetValue(station, s);
        }
        else
        {
            var s = ArkanoidRules.NewGame(41, true); ArkanoidRules.Launch(ref s);
            station.GetType().GetField("simulation", Private).SetValue(station, s);
        }
    }
    private void Pause(bool value, uint session, ulong sender) =>
        typeof(ArcadeStation).GetMethod("SetPaused", Private).Invoke(station, new object[] { value, session, sender });
    private void Step() => station.GetType().GetMethod("FixedUpdate", Private).Invoke(station, null);
    [TestCase(false), TestCase(true)] public void OnlyCurrentOccupantCanPauseOrResume(bool starships)
    {
        Create(starships); Pause(true, 41, 6); Pause(true, 40, 5); Assert.IsFalse(station.IsArcadePaused);
        Pause(true, 41, 5); Assert.IsTrue(station.IsArcadePaused);
        Pause(false, 41, 6); Pause(false, 40, 5); Assert.IsTrue(station.IsArcadePaused);
        Pause(false, 41, 5); Assert.IsFalse(station.IsArcadePaused);
    }
    [TestCase(false), TestCase(true)] public void PauseFreezesEntireSimulationAndResumeContinues(bool starships)
    {
        Create(starships); Pause(true, 41, 5);
        object before = station.GetType().GetProperty("State").GetValue(station);
        for (int i = 0; i < 150; i++) Step();
        Assert.AreEqual(before, station.GetType().GetProperty("State").GetValue(station));
        Pause(false, 41, 5); Step();
        Assert.AreNotEqual(before, station.GetType().GetProperty("State").GetValue(station));
    }
    [TestCase(false), TestCase(true)] public void ReleaseClearsPauseAndOldSessionCannotPauseFreeCabinet(bool starships)
    {
        Create(starships); Pause(true, 41, 5); station.Release();
        Assert.IsFalse(station.IsArcadePaused); Pause(true, 41, 5); Assert.IsFalse(station.IsArcadePaused);
    }
    [TestCase(false), TestCase(true)] public void PlayButtonCannotRestartWhilePaused(bool starships)
    {
        Create(starships);
        if (starships)
        {
            var s = ((StarshipStation)station).State; s.Phase = StarshipPhase.GameOver;
            station.GetType().GetField("simulation", Private).SetValue(station, s);
            Pause(true, 41, 5);
            station.GetType().GetMethod("AcceptStart", Private).Invoke(station, new object[] { 41U, 5UL });
            Assert.AreEqual(StarshipPhase.GameOver, ((StarshipStation)station).State.Phase);
        }
        else
        {
            var s = ((ArkanoidStation)station).State; s.Phase = ArkanoidPhase.GameOver;
            station.GetType().GetField("simulation", Private).SetValue(station, s);
            Pause(true, 41, 5);
            station.GetType().GetMethod("AcceptPlay", Private).Invoke(station, new object[] { 0f, 41U, 5UL });
            Assert.AreEqual(ArkanoidPhase.GameOver, ((ArkanoidStation)station).State.Phase);
        }
    }
}
