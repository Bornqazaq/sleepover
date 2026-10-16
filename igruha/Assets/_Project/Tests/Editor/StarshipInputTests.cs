using System.Reflection;
using Igruha.Core.Hub.Activities;
using NUnit.Framework;
using UnityEngine;

public sealed class StarshipInputTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private StarshipStation station;
    [SetUp] public void SetUp()
    {
        root = new GameObject("Starship input gate"); station = root.AddComponent<StarshipStation>();
        typeof(HubActivityStation).GetField("offlineOccupant", Private).SetValue(station, 5UL);
        typeof(StarshipStation).GetField("simulation", Private).SetValue(station, StarshipRules.NewGame(41, true));
    }
    [TearDown] public void TearDown() => Object.DestroyImmediate(root);
    private void Input(float ship, bool fire, uint session, uint sequence, ulong sender) =>
        typeof(StarshipStation).GetMethod("AcceptInput", Private).Invoke(station, new object[] { ship, fire, session, sequence, sender });
    private T Field<T>(string name) => (T)typeof(StarshipStation).GetField(name, Private).GetValue(station);
    [Test] public void OnlyCurrentOccupantCanMoveOrFire()
    {
        Input(.5f, true, 41, 1, 6); Input(.5f, true, 40, 2, 5);
        Assert.AreEqual(0, Field<float>("target")); Assert.IsFalse(Field<bool>("fireHeld"));
        Input(.5f, true, 41, 3, 5); Assert.AreEqual(.5f, Field<float>("target")); Assert.IsTrue(Field<bool>("fireHeld"));
    }
    [Test] public void InvalidAndReorderedInputCannotOverwriteLatestIntent()
    {
        Input(.4f, true, 41, 5, 5);
        Input(float.NaN, false, 41, 6, 5); Input(float.PositiveInfinity, false, 41, 7, 5);
        Input(-.6f, false, 41, 4, 5); Input(-.6f, false, 41, 5, 5);
        Assert.AreEqual(.4f, Field<float>("target")); Assert.IsTrue(Field<bool>("fireHeld"));
        Input(-2, false, 41, 6, 5); Assert.AreEqual(-StarshipRules.ShipLimit, Field<float>("target"));
        Assert.IsFalse(Field<bool>("fireHeld"));
    }
    [Test] public void UnauthorizedOrStaleStartIsRejected()
    {
        var start = typeof(StarshipStation).GetMethod("AcceptStart", Private);
        start.Invoke(station, new object[] { 41U, 6UL }); start.Invoke(station, new object[] { 40U, 5UL });
        Assert.AreEqual(StarshipPhase.Ready, station.State.Phase);
        start.Invoke(station, new object[] { 41U, 5UL }); Assert.AreEqual(StarshipPhase.Playing, station.State.Phase);
    }
    [Test] public void FireStopsWhenInputPacketsStopArriving()
    {
        var s = StarshipRules.NewGame(41, true); StarshipRules.Start(ref s);
        typeof(StarshipStation).GetField("simulation", Private).SetValue(station, s);
        Input(0, true, 41, 1, 5);
        typeof(StarshipStation).GetField("lastInputAt", Private).SetValue(station, Time.unscaledTime - 1);
        typeof(StarshipStation).GetMethod("FixedUpdate", Private).Invoke(station, null);
        Assert.AreEqual(0, station.State.ShotsFired);
    }
}
