using System.Reflection;
using System.Collections;
using Igruha.Core.CameraSystems;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.Circus;
using Igruha.Minigames.Stopwatch;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class CircusDuelTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void OnlyFinalistsAndActualSpectatorsWatchTheDuel(bool cans)
        {
            var root = new GameObject("AudienceTest"); root.SetActive(false);
            var first = new GameObject("First").AddComponent<CageStation>();
            var second = new GameObject("Second").AddComponent<CageStation>();
            try
            {
                Component game = cans ? (Component)root.AddComponent<CansOrderMinigame>() : root.AddComponent<StopwatchMinigame>();
                var spectator = root.AddComponent<SpectatorCamera>(); Set(game, "spectator", spectator);
                var type = game.GetType().GetNestedType("Contestant", BindingFlags.NonPublic);
                var local = System.Activator.CreateInstance(type);
                type.GetField("LocallyControlled").SetValue(local, true);
                type.GetField("Cage").SetValue(local, first);
                if (cans) type.GetField("Entry").SetValue(local, new CansOrderEntry { Alive = true });
                else type.GetField("Alive").SetValue(local, true);
                ((IList)game.GetType().GetField("contestants", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game)).Add(local);
                var audience = (ICircusDuelAudience)game;
                Assert.That(audience.CanWatchDuel(first, second), Is.True);
                type.GetField("InPit").SetValue(local, true);
                Assert.That(audience.CanWatchDuel(first, second), Is.False, "The pit owns the camera even with a stale Alive flag.");
                type.GetField("InPit").SetValue(local, false);
                if (cans) type.GetField("Entry").SetValue(local, new CansOrderEntry { Alive = false });
                else type.GetField("Alive").SetValue(local, false);
                Assert.That(audience.CanWatchDuel(first, second), Is.False, "Elimination alone does not mean spectator.");
                typeof(SpectatorCamera).GetProperty("IsActive").SetValue(spectator, true);
                Assert.That(audience.CanWatchDuel(first, second), Is.True);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(first.gameObject); Object.DestroyImmediate(second.gameObject); }
        }

        private sealed class Audience : ICircusDuelAudience
        {
            public bool Allowed;
            public string DuelName(CageStation cage) => "Тестовый игрок";
            public bool CanWatchDuel(CageStation first, CageStation second) => Allowed;
        }

        [Test]
        public void PitKeepsNormalLightingAndJoiningSpectatorUsesTheCurrentCue()
        {
            var root = new GameObject("AudienceLighting");
            var duel = root.AddComponent<CircusDuelPresentation>();
            var first = new GameObject("First").AddComponent<CageStation>();
            var second = new GameObject("Second").AddComponent<CageStation>();
            var lamp = root.AddComponent<Light>(); lamp.intensity = 27;
            var audience = new Audience();
            float original = RenderSettings.ambientIntensity;
            try
            {
                Set(duel, "cages", new[] { first, second }); Set(duel, "houseLights", new[] { lamp }); Set(duel, "audience", audience);
                var state = new CircusDuelState { Active = true, FirstCage = 0, SecondCage = 1, StartedAt = 91 };
                duel.ApplyState(state); duel.RenderAt(92);
                Assert.That(duel.IsVisible, Is.False);
                Assert.That(lamp.intensity, Is.EqualTo(27));
                Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(original));
                audience.Allowed = true; duel.RenderAt(94);
                Assert.That(duel.IsVisible, Is.True);
                Assert.That(duel.State.StartedAt, Is.EqualTo(91));
                Assert.That(duel.State.Cue(94), Is.EqualTo(2));
                audience.Allowed = false; duel.RenderAt(94.1);
                Assert.That(duel.IsVisible, Is.False);
                Assert.That(lamp.intensity, Is.EqualTo(27));
                Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(original));
                Assert.That(duel.State.Active, Is.True, "Local visibility must never cancel the network duel.");
            }
            finally { duel.ResetShow(); Object.DestroyImmediate(root); Object.DestroyImmediate(first.gameObject); Object.DestroyImmediate(second.gameObject); }
        }

        [Test]
        public void StopwatchStageCarriesOriginalCueClockAndFinalistsThroughNgoSerialization()
        {
            var original = new StopwatchStageNetState { Stage = StopwatchMinigame.StageDuel, Subround = 6,
                Duration = CircusDuelState.IntroSeconds, EndTime = 91 + CircusDuelState.IntroSeconds, Duel = Snapshot() };
            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteNetworkSerializable(original);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out StopwatchStageNetState copy);
            Assert.That(copy.Equals(original), Is.True);
            Assert.That(copy.Duel.Cue(copy.Duel.StartedAt + CircusDuelState.SecondSpotAt + .4), Is.EqualTo(2),
                "A delayed client joins the second reveal, not a fresh blackout.");
            copy.Duel.FirstCage = 0;
            Assert.That(copy.Equals(original), Is.False, "Changing finalists must dirty the stage snapshot.");
        }

        [Test]
        public void CansSubsequentStageRetainsDuelWithoutReplayingIntroduction()
        {
            var original = new CansOrderStageNetState { Stage = 2, Circle = 8, Duration = 12,
                EndTime = 120, Duel = Snapshot() };
            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteNetworkSerializable(original);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out CansOrderStageNetState copy);
            Assert.That(copy.Equals(original), Is.True);
            Assert.That(copy.Duel.Cue(108), Is.EqualTo(4));
            Assert.That(copy.Duel.StartedAt, Is.EqualTo(91));
        }

        [Test]
        public void RepeatedSnapshotAndCancellationRestoreTheOriginalLightingExactly()
        {
            var root = new GameObject("DuelTest");
            var duel = root.AddComponent<CircusDuelPresentation>();
            var first = new GameObject("First"); var second = new GameObject("Second");
            var lamp = root.AddComponent<Light>(); lamp.intensity = 27;
            float ambient = RenderSettings.ambientIntensity;
            Color sky = RenderSettings.ambientSkyColor;
            try
            {
                Set(duel, "cages", new[] { first.AddComponent<CageStation>(), second.AddComponent<CageStation>() });
                Set(duel, "houseLights", new[] { lamp });
                var state = new CircusDuelState { Active = true, FirstCage = 0, SecondCage = 1, StartedAt = 91 };
                duel.ApplyState(state); duel.RenderAt(91.6);
                Assert.That(lamp.intensity, Is.LessThan(1));
                duel.ApplyState(state); duel.RenderAt(92.4);
                Assert.That(duel.TryBegin(first.GetComponent<CageStation>(), second.GetComponent<CageStation>()), Is.False);
                duel.ResetShow();
                Assert.That(lamp.intensity, Is.EqualTo(27));
                Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(ambient));
                Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(sky));
                Assert.That(duel.State.Active, Is.False);
                Assert.That(duel.TryBegin(first.GetComponent<CageStation>(), second.GetComponent<CageStation>()), Is.True,
                    "A new match can introduce its finalists again.");
            }
            finally
            {
                duel.ResetShow(); Object.DestroyImmediate(root); Object.DestroyImmediate(first); Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void CinematicPrivacyRestoresPreviouslyHiddenCanDetailsWithoutChangingItsSlot()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var symbol = GameObject.CreatePrimitive(PrimitiveType.Cube);
            symbol.transform.SetParent(root.transform, false);
            var body = root.GetComponent<Renderer>();
            var mark = symbol.GetComponent<Renderer>(); mark.forceRenderingOff = true;
            var can = root.AddComponent<Can>();
            typeof(Can).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(can, null);
            can.SlotIndex = 3;
            try
            {
                can.SetPresentationHidden(true); can.SetPresentationHidden(true);
                Assert.That(body.forceRenderingOff && mark.forceRenderingOff, Is.True);
                can.SetPresentationHidden(false);
                Assert.That(body.forceRenderingOff, Is.False);
                Assert.That(mark.forceRenderingOff, Is.True);
                Assert.That(can.SlotIndex, Is.EqualTo(3));
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static CircusDuelState Snapshot() => new CircusDuelState
            { Active = true, FirstCage = 2, SecondCage = 6, StartedAt = 91 };
        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
