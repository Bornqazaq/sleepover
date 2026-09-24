using System.Collections;
using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.Mosquitoes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Igruha.Tests.PlayMode
{
    public sealed class MosquitoesSceneTests
    {
        private int requestedCount;
        private IDictionary profile;
        private Hashtable savedProfile;
        private bool oldBackground;
        private MosquitoesMinigame game;
        private int giantId;
        [SetUp] public void Setup()
        {
            profile = (IDictionary)typeof(LocalPartyProfile).GetField("entries", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            savedProfile = new Hashtable(profile);
            oldBackground = Application.runInBackground; Application.runInBackground = true;
            SceneManager.sceneLoaded += Configure;
        }
        private void Configure(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Mosquitoes") return;
            game = Object.FindFirstObjectByType<MosquitoesMinigame>();
            typeof(MosquitoesMinigame).GetField("debugGiantId", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, giantId);
            typeof(MosquitoesMinigame).GetField("debugDisableBots", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, true);
            var spawner = Object.FindFirstObjectByType<Igruha.Core.Spawning.PlayerSpawner>();
            typeof(Igruha.Core.Spawning.PlayerSpawner).GetField("debugPlayerCount", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(spawner, requestedCount);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            SceneManager.sceneLoaded -= Configure;
            if (game != null && game.GameplayActive) game.EndMinigame();
            profile.Clear(); foreach (DictionaryEntry entry in savedProfile) profile.Add(entry.Key, entry.Value);
            Application.runInBackground = oldBackground;
            yield return null;
        }
        [UnityTest] public IEnumerator ThreePlayersGiantBitesSwatAndCleanup() => Run(3, 0);
        [UnityTest] public IEnumerator FourPlayersMosquitoBitesSwatAndCleanup() => Run(4, 1);
        [UnityTest] public IEnumerator EightPlayersMosquitoBitesSwatAndCleanup() => Run(8, 7);
        private IEnumerator Run(int count, int giant)
        {
            giantId = giant; requestedCount = count; profile.Clear();
            yield return SceneManager.LoadSceneAsync("Mosquitoes", LoadSceneMode.Single);
            yield return new WaitForSeconds(3.3f);
            Assert.That(game.GiantId, Is.EqualTo(giant)); Assert.That(game.Bodies.Count, Is.EqualTo(count - 1));
            Assert.That(game.AliveCount, Is.EqualTo(count - 1)); Assert.That(game.ControlsAvailable);
            Assert.That(GameObject.Find("Floor").GetComponent<BoxCollider>().bounds.size.x, Is.EqualTo(14.4f).Within(.01f));
            Assert.That(GameObject.Find("Floor").GetComponent<BoxCollider>().bounds.size.z, Is.EqualTo(11.52f).Within(.01f));
            Assert.That(GameObject.Find("_Arena/Ceiling").GetComponent<BoxCollider>().bounds.min.y, Is.EqualTo(3.6f).Within(.01f));
            Assert.That(GameObject.Find("BedCollider").GetComponent<BoxCollider>().bounds.max.y, Is.EqualTo(.45f).Within(.01f));
            Assert.That(Physics.GetIgnoreLayerCollision(12, 0), Is.True);
            Assert.That(Physics.GetIgnoreLayerCollision(12, LayerMask.NameToLayer("Ground")), Is.False);
            Assert.That(Physics.GetIgnoreLayerCollision(12, LayerMask.NameToLayer("Cover")), Is.False);
            var positions = new System.Collections.Generic.HashSet<Vector3>();
            foreach (var body in game.Bodies)
            {
                body.enabled = false; body.GetComponent<Rigidbody>().linearVelocity = Vector3.zero; body.Holding = false;
                Assert.That(game.InFlightBounds(body.Position)); Assert.That(positions.Add(game.FlightSpawn(body.Id)), "spawn points must be unique");
                Assert.That(Physics.CheckSphere(game.FlightSpawn(body.Id), .12f, LayerMask.GetMask("Ground", "Cover"), QueryTriggerInteraction.Ignore), Is.False, "spawn must clear furniture");
            }
            Assert.That(game.TrySwat(game.Bodies[0].Id), Is.False, "wrong role");
            Assert.That(game.TrySwat(giant), Is.False, "sleeping giant cannot swat");
            for (int i = 0; i < 2; i++)
            {
                var b = game.Bodies[i]; b.SetBiteState(false, 0); b.ResetPosition(game.BiteTarget + Vector3.right * (i == 0 ? -.12f : .12f)); b.Holding = true;
            }
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.25f);
            Assert.That(game.Sleep.Phase, Is.EqualTo(GiantPhase.Sleeping)); Assert.That(game.Bodies[0].IsAttached);
            yield return new WaitForSeconds(.3f);
            Assert.That(game.Sleep.Phase, Is.EqualTo(GiantPhase.Waking)); Assert.That(game.Sleep.ImmunityRemaining, Is.GreaterThan(1));
            Assert.That(game.Bodies[0].BiteCooldown, Is.GreaterThan(4));
            foreach (var b in game.Bodies) b.Holding = false;
            yield return new WaitForSeconds(.6f);
            Assert.That(game.Sleep.CanSwat); float sleep = game.Sleep.Sleep;
            yield return new WaitForSeconds(.2f); Assert.That(game.Sleep.Sleep, Is.EqualTo(sleep));
            PlayerController avatar = null;
            foreach (var player in SessionScoreboard.Current.Players) if (player.Id == giant) avatar = player.Avatar;
            Assert.That(avatar, Is.Not.Null);
            var victim = game.Bodies[0];
            victim.ResetPosition(avatar.Position + Vector3.up * .9f + avatar.Facing * .7f); Physics.SyncTransforms();
            Assert.That(game.TrySwat(giant)); Assert.That(game.TrySwat(giant), Is.False, "server cooldown");
            yield return new WaitForSeconds(.35f); Assert.That(victim.IsAlive, Is.False, "blind swat must hit");
            foreach (var b in game.Bodies) b.Kill();
            yield return null; yield return null;
            Assert.That(game.Phase, Is.EqualTo(MinigamePhase.PracticeComplete));
            foreach (var player in SessionScoreboard.Current.Players) Assert.That(player.Avatar.gameObject.activeSelf, Is.True, "avatar must be restored");
            Assert.That(Object.FindObjectsByType<MosquitoBody>(FindObjectsSortMode.None), Is.Empty);
        }
    }
}
