using NUnit.Framework;
using UnityEngine;
using Igruha.Minigames.Mosquitoes;
using Igruha.Core.Player;
using Igruha.Core.UI;

namespace Igruha.Tests
{
    public sealed class MosquitoesRulesTests
    {
        private MosquitoesConfig config;
        [SetUp] public void Setup() => config = ScriptableObject.CreateInstance<MosquitoesConfig>();
        [TearDown] public void Cleanup() => Object.DestroyImmediate(config);
        [Test] public void AwakeAndTransitionsDoNotSpendAccumulatedSleep()
        {
            var s = new GiantSleepState(config); s.Tick(12); s.ToggleBed();
            s.Tick(.25f); Assert.That(s.Phase, Is.EqualTo(GiantPhase.Waking)); Assert.That(s.Sleep, Is.EqualTo(12));
            s.Tick(.25f); Assert.That(s.CanSwat); s.Tick(10); Assert.That(s.Sleep, Is.EqualTo(12));
            s.ToggleBed(); s.Tick(.99f); Assert.That(s.Sleep, Is.EqualTo(12));
            s.Tick(.51f); Assert.That(s.Sleep, Is.EqualTo(12.5f).Within(.001f));
        }
        [Test] public void TwoTimelyBitesWakeAndGrantImmunity()
        {
            var s = new GiantSleepState(config); Assert.That(s.RegisterBite()); s.Tick(3.99f);
            Assert.That(s.RegisterBite()); Assert.That(s.Phase, Is.EqualTo(GiantPhase.Waking));
            Assert.That(s.ImmunityRemaining, Is.EqualTo(2)); Assert.That(s.RegisterBite(), Is.False);
            s.Tick(.5f); Assert.That(s.CanSwat); Assert.That(s.Sleep, Is.EqualTo(3.99f).Within(.001f));
        }
        [Test] public void OneMosquitoCannotWakeWithFiveSecondCooldown()
        {
            var s = new GiantSleepState(config); s.RegisterBite(); s.Tick(config.BiteCooldown); s.RegisterBite();
            Assert.That(s.Phase, Is.EqualTo(GiantPhase.Sleeping));
        }
        [Test] public void BitesDuringLyingDownDoNotPreloadWakePair()
        {
            var s = new GiantSleepState(config); s.ToggleBed(); s.Tick(.5f); s.ToggleBed();
            Assert.That(s.RegisterBite()); Assert.That(s.RegisterBite()); Assert.That(s.Phase, Is.EqualTo(GiantPhase.LyingDown));
            s.Tick(1); s.RegisterBite(); Assert.That(s.Phase, Is.EqualTo(GiantPhase.Sleeping));
        }
        [TestCase(3)] [TestCase(4)] [TestCase(8)] public void TeamResultsPreserveOriginalRosterAndDisconnectedMosquitoTeamPlace(int n)
        {
            for (int i = 0; i < n; i++)
            {
                Assert.That(MosquitoesMinigame.PlaceFor(i, 1, true, n, false, false, false), Is.EqualTo(i == 1 ? 1 : n));
                Assert.That(MosquitoesMinigame.PlaceFor(i, 1, false, n, false, false, false), Is.EqualTo(i == 1 ? n : 1));
                Assert.That(MosquitoesMinigame.PlaceFor(i, 1, false, n, true, false, true), Is.EqualTo(i == 1 ? n : 1));
            }
            Assert.That(MosquitoesMinigame.PlaceFor(0, 1, false, n, false, true, false), Is.EqualTo(n));
            Assert.That(MosquitoesMinigame.PlaceFor(0, 1, false, n, false, true, true), Is.EqualTo(1));
        }
        [Test] public void SleepClampsAtTarget()
        {
            var s = new GiantSleepState(config); s.Tick(100); Assert.That(s.Sleep, Is.EqualTo(60)); Assert.That(s.ReachedTarget);
        }
        [TestCase(true, false)] [TestCase(false, true)] public void ParkingRestoresOriginalStateAndIsIdempotent(bool enabled, bool locked)
        {
            var go = new GameObject("park test"); go.SetActive(false);
            var reader = go.AddComponent<PlayerInputReader>(); var avatar = go.AddComponent<PlayerController>();
            reader.enabled = enabled; reader.SetSuspended(true); avatar.MovementLocked = locked;
            var park = new PlayerAvatarPark(); park.Park(avatar); park.Park(avatar);
            Assert.That(park.Count, Is.EqualTo(1)); Assert.That(go.activeSelf, Is.False);
            park.ReleaseAll(); park.ReleaseAll();
            Assert.That(reader.enabled, Is.EqualTo(enabled)); Assert.That(reader.Suspended); Assert.That(avatar.MovementLocked, Is.EqualTo(locked));
            Assert.That(go.activeSelf, Is.False); Object.DestroyImmediate(go);
        }
        [Test] public void VignetteFadesBackToExactlyZero()
        {
            var go = new GameObject("vignette test", typeof(RectTransform)); var v = go.AddComponent<ScreenVignette>();
            v.SetImmediate(.9f); v.SetAmount(0); v.Advance(.25f); Assert.That(v.Amount, Is.EqualTo(.45f).Within(.001));
            v.Advance(.25f); Assert.That(v.Amount, Is.Zero); Object.DestroyImmediate(go);
        }
        [Test] public void ParkingKeepsNetworkRootAliveAndRestoresVisibilityAndColliders()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.SetActive(false);
            var avatar = go.AddComponent<PlayerController>();
            var renderer = go.GetComponent<Renderer>(); var collider = go.GetComponent<Collider>();
            renderer.enabled = false;
            go.SetActive(true);
            var park = new PlayerAvatarPark(); park.Park(avatar);
            Assert.That(go.activeSelf, Is.True); Assert.That(avatar.enabled, Is.False); Assert.That(collider.enabled, Is.False);
            park.ReleaseAll(); park.ReleaseAll();
            Assert.That(go.activeSelf, Is.True); Assert.That(avatar.enabled, Is.True);
            Assert.That(collider.enabled, Is.True); Assert.That(renderer.enabled, Is.False);
            Object.DestroyImmediate(go);
        }
    }
}
