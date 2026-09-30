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
        [UnityTest] public IEnumerator AllEightLiveGiantSpawnsStayInsideBed()
        {
            requestedCount = 8; profile.Clear();
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int id = 0; id < 8; id++)
            {
                giantId = id;
                yield return SceneManager.LoadSceneAsync("Mosquitoes", LoadSceneMode.Single);
                yield return new WaitForSeconds(3.3f);
                Assert.That(game.GiantId, Is.EqualTo(id));
                var bounds = game.GiantRig.MeasureSkinBounds();
                int character = -1;
                foreach (var p in SessionScoreboard.Current.Players) if (p.Id == id) character = p.CharacterIndex;
                seen.Add(character);
                Assert.That(bounds.min.x, Is.GreaterThan(-3.02f), "live head clearance: " + character);
                Assert.That(bounds.max.x, Is.LessThan(-.10f), "live feet clearance: " + character);
                Assert.That(bounds.min.z, Is.GreaterThan(-2.01f), "live bed side: " + character);
                Assert.That(bounds.max.z, Is.LessThan(-.19f), "live bed side: " + character);
                Assert.That(game.GiantRig.BackContactHeight, Is.InRange(.665f,.680f), "live back contact: " + character);
                game.EndMinigame(); yield return null;
                Assert.That(Object.FindObjectsByType<MosquitoGiantRig>(FindObjectsSortMode.None), Is.Empty);
            }
            Assert.That(seen.Count, Is.EqualTo(8), "exercise the entire roster");
        }
        [UnityTest] public IEnumerator OutsideSpawnWindowIngressOneWayAndFiveSecondDragonfly()
        {
            requestedCount=3; giantId=0; profile.Clear();
            yield return SceneManager.LoadSceneAsync("Mosquitoes",LoadSceneMode.Single);
            yield return new WaitForSeconds(3.05f);
            Assert.That(game.Bodies.Count,Is.EqualTo(2));
            foreach(var b in game.Bodies) { b.enabled=false; b.GetComponent<Rigidbody>().linearVelocity=Vector3.zero; b.ResetPosition(game.FlightSpawn(b.Id)); }
            Assert.That(game.EntryRemaining,Is.GreaterThan(4.7f));
            var entered=game.Bodies[0]; var outside=game.Bodies[1];
            Assert.That(entered.Position.x,Is.GreaterThan(5));
            Assert.That(Physics.SphereCast(new Ray(new Vector3(5.7f,1.85f,.94f),Vector3.left),.12f,2.6f,LayerMask.GetMask("Cover","Ground"),QueryTriggerInteraction.Ignore),Is.False,"actual opening must allow flight");
            entered.ResetPosition(new Vector3(4.0f,1.85f,.94f));
            entered.GetComponent<Rigidbody>().linearVelocity=Vector3.left*5;
            yield return new WaitForSeconds(.30f);
            entered.GetComponent<Rigidbody>().linearVelocity=Vector3.zero;
            Assert.That(entered.HasEntered,Is.True,"server observes crossing through opening");
            Assert.That(outside.HasEntered,Is.False);
            entered.ResetPosition(new Vector3(4.5f,1.85f,.94f));
            yield return null; yield return new WaitForFixedUpdate();
            Assert.That(entered.Position.x,Is.LessThanOrEqualTo(MosquitoWindowEntry.Plane+.02f),"server prevents return to exterior");
            yield return new WaitForSeconds(Mathf.Max(0,game.EntryRemaining-.12f));
            Assert.That(outside.IsAlive,Is.True,"five-second grace period");
            yield return new WaitForSeconds(.16f);
            Assert.That(outside.IsAlive,Is.False,"deadline death requires no player input or attack collision");
            Assert.That(entered.IsAlive,Is.True,"entered player is safe from predator");
            Assert.That(game.DragonflyKills,Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<MosquitoDragonflyAttack>(FindObjectsSortMode.None).Length,Is.EqualTo(1));
            game.EndMinigame();yield return null;
        }
        [UnityTest] public IEnumerator GiantDisconnectEndsRoundForRemainingMosquitoes()
        {
            requestedCount = 3; giantId = 0; profile.Clear();
            yield return StartScoredRound();
            MinigameResults reported = null;
            game.ResultsReported += result => reported = result;
            int departedMosquito = game.Bodies[0].Id;

            game.ApplyLeaveRound(departedMosquito);
            Assert.That(game.GameplayActive, Is.True);
            game.ApplyLeaveRound(giantId);
            yield return null;

            Assert.That(game.Phase, Is.EqualTo(MinigamePhase.Results));
            Assert.That(PlaceOf(reported, giantId), Is.EqualTo(3));
            Assert.That(PlaceOf(reported, departedMosquito), Is.EqualTo(1), "departed mosquito keeps its team's winning place");
            Assert.That(Object.FindObjectsByType<MosquitoBody>(FindObjectsSortMode.None), Is.Empty);
        }
        [UnityTest] public IEnumerator MosquitoDisconnectKeepsMatchAliveUntilLastMosquitoLeaves()
        {
            requestedCount = 3; giantId = 0; profile.Clear();
            yield return StartScoredRound();
            MinigameResults reported = null;
            game.ResultsReported += result => reported = result;
            int firstMosquito = game.Bodies[0].Id;
            int lastMosquito = game.Bodies[1].Id;

            game.ApplyLeaveRound(firstMosquito);
            yield return null;
            Assert.That(game.GameplayActive, Is.True);
            Assert.That(game.AliveCount, Is.EqualTo(1));
            Assert.That(game.Bodies.Count, Is.EqualTo(1), "departed bodies must be removed from the live registry");
            Assert.That(game.FindBody(firstMosquito), Is.Null);

            game.ApplyLeaveRound(lastMosquito);
            yield return null;

            Assert.That(game.Phase, Is.EqualTo(MinigamePhase.Results));
            Assert.That(PlaceOf(reported, giantId), Is.EqualTo(1));
            Assert.That(PlaceOf(reported, firstMosquito), Is.EqualTo(3));
            Assert.That(PlaceOf(reported, lastMosquito), Is.EqualTo(3));
        }
        [UnityTest] public IEnumerator FlightBoundaryCorrectionRunsOnFixedStepAndUsesFarWall()
        {
            requestedCount = 8; giantId = 0; profile.Clear();
            yield return SceneManager.LoadSceneAsync("Mosquitoes", LoadSceneMode.Single);
            yield return new WaitForSeconds(3.3f);
            var body = game.Bodies[0];
            foreach (var mosquito in game.Bodies)
                Assert.That(Physics.CheckSphere(game.FlightRecovery(mosquito.Id), game.Config.BodyRadius,
                    LayerMask.GetMask("Ground", "Cover"), QueryTriggerInteraction.Ignore), Is.False, "recovery must clear room furniture");
            body.ConfirmEntry();
            body.ResetPosition(new Vector3(0, 1.5f, 3.2f));

            yield return new WaitForFixedUpdate();

            Assert.That(Vector3.Distance(body.Position, game.FlightRecovery(body.Id)), Is.LessThan(.01f));
            Assert.That(game.InFlightBounds(body.Position), Is.True);
        }
        private IEnumerator StartScoredRound()
        {
            yield return SceneManager.LoadSceneAsync("Mosquitoes", LoadSceneMode.Single);
            yield return new WaitForSeconds(3.3f);
            game.EndMinigame();
            yield return null;
            game.ApplyPhase(MinigamePhase.Round);
            yield return new WaitForSeconds(3.3f);
        }
        private static int PlaceOf(MinigameResults results, int id)
        {
            Assert.That(results, Is.Not.Null, "round must report results");
            int index = results.IndexOf(id);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "results must preserve the starting roster");
            return results.Entries[index].Place;
        }
        [UnityTest] public IEnumerator ThreePlayersGiantBitesSwatAndCleanup() => Run(3, 0);
        [UnityTest] public IEnumerator ThreePlayersMosquitoBitesSwatAndCleanup() => Run(3, 1);
        [UnityTest] public IEnumerator FourPlayersGiantBitesSwatAndCleanup() => Run(4, 0);
        [UnityTest] public IEnumerator FourPlayersMosquitoBitesSwatAndCleanup() => Run(4, 1);
        [UnityTest] public IEnumerator EightPlayersGiantBitesSwatAndCleanup() => Run(8, 0);
        [UnityTest] public IEnumerator EightPlayersMosquitoBitesSwatAndCleanup() => Run(8, 7);
        private IEnumerator Run(int count, int giant)
        {
            giantId = giant; requestedCount = count; profile.Clear();
            yield return SceneManager.LoadSceneAsync("Mosquitoes", LoadSceneMode.Single);
            yield return new WaitForSeconds(3.3f);
            Assert.That(game.GiantId, Is.EqualTo(giant)); Assert.That(game.Bodies.Count, Is.EqualTo(count - 1));
            Assert.That(game.AliveCount, Is.EqualTo(count - 1)); Assert.That(game.ControlsAvailable);
            Assert.That(game.GiantRig.SkinBounds.size.z, Is.LessThan(1.25f), "live rigidbody spawn must align the body along the bed");
            Assert.That(game.GiantRig.SkinBounds.min.x, Is.GreaterThan(-3.02f));
            Assert.That(game.GiantRig.SkinBounds.max.x, Is.LessThan(-.10f));
            Assert.That(GameObject.Find("Floor").GetComponent<BoxCollider>().bounds.size.x, Is.EqualTo(7.2f).Within(.01f));
            Assert.That(GameObject.Find("Floor").GetComponent<BoxCollider>().bounds.size.z, Is.EqualTo(6.4f).Within(.01f));
            Assert.That(GameObject.Find("_Arena/Ceiling").GetComponent<BoxCollider>().bounds.min.y, Is.EqualTo(3.4f).Within(.01f));
            Assert.That(GameObject.Find("BedCollider").GetComponent<BoxCollider>().bounds.max.y, Is.EqualTo(.665f).Within(.01f));
            Assert.That(Physics.GetIgnoreLayerCollision(12, 0), Is.True);
            Assert.That(Physics.GetIgnoreLayerCollision(12, LayerMask.NameToLayer("Ground")), Is.False);
            Assert.That(Physics.GetIgnoreLayerCollision(12, LayerMask.NameToLayer("Cover")), Is.False);
            var positions = new System.Collections.Generic.HashSet<Vector3>();
            foreach (var body in game.Bodies)
            {
                var selected = -1;
                foreach (var player in SessionScoreboard.Current.Players) if (player.Id == body.Id) selected = player.CharacterIndex;
                Assert.That(body.VisualCharacterIndex, Is.EqualTo(selected), "mosquito keeps the selected roster character");
                Assert.That(body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, Is.GreaterThan(0));
                Assert.That(body.GetComponentsInChildren<PlayerController>(true), Is.Empty, "visual copies must not contain player motors");
                int wings = 0;
                foreach (var t in body.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("WingPivot_")) wings++;
                Assert.That(wings, Is.EqualTo(4));
                body.enabled = false; body.GetComponent<Rigidbody>().linearVelocity = Vector3.zero; body.Holding = false;
                Assert.That(MosquitoWindowEntry.ExteriorBounds(game.FlightSpawn(body.Id))); body.ConfirmEntry(); Assert.That(positions.Add(game.FlightSpawn(body.Id)), "spawn points must be unique");
                Assert.That(Physics.CheckSphere(game.FlightSpawn(body.Id), .12f, LayerMask.GetMask("Ground", "Cover"), QueryTriggerInteraction.Ignore), Is.False, "spawn must clear furniture");
            }
            // Strike the actual sleeping collision shell at speed: query presence alone is insufficient.
            var contactProbe = game.Bodies[0];
            contactProbe.ResetPosition(game.BiteTarget + Vector3.up * .4f);
            contactProbe.GetComponent<Rigidbody>().linearVelocity = Vector3.down * 8;
            Physics.SyncTransforms(); yield return new WaitForSeconds(.16f);
            Assert.That(contactProbe.Position.y, Is.GreaterThan(game.BiteTarget.y - .12f), "mosquito must stop at giant skin");
            contactProbe.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            Assert.That(game.TrySwat(game.Bodies[0].Id), Is.False, "wrong role");
            Assert.That(game.TrySwat(giant), Is.False, "sleeping giant cannot swat");
            for (int i = 0; i < 2; i++)
            {
                var b = game.Bodies[i]; b.SetBiteState(false, 0); b.ResetPosition(game.BiteTarget + Vector3.up * .16f + Vector3.forward * (i == 0 ? -.16f : .16f)); b.Holding = true;
            }
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.25f);
            Assert.That(game.Sleep.Phase, Is.EqualTo(GiantPhase.Sleeping)); Assert.That(game.Bodies[0].IsAttached, "bite pos="+game.Bodies[0].Position+" target="+game.BiteTarget+" hold="+game.Bodies[0].Holding+" entered="+game.Bodies[0].HasEntered+" distance="+Vector3.Distance(game.Bodies[0].Position,game.BiteTarget));
            yield return new WaitForSeconds(.3f);
            Assert.That(game.Sleep.Phase, Is.EqualTo(GiantPhase.Waking)); Assert.That(game.Sleep.ImmunityRemaining, Is.GreaterThan(1));
            Assert.That(game.Bodies[0].BiteCooldown, Is.GreaterThan(4));
            Assert.That(game.ConfirmedBiteEffects, Is.GreaterThanOrEqualTo(2), "accepted bites trigger visible confirmation");
            Assert.That(Object.FindObjectsByType<MosquitoBiteFeedback>(FindObjectsSortMode.None).Length, Is.GreaterThan(0));
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
            var survivor = game.Bodies[game.Bodies.Count - 1];
            foreach (var b in game.Bodies) if (b != survivor) b.Kill();
            yield return null;
            Assert.That(game.AliveCount, Is.EqualTo(1));
            Assert.That(game.GameplayActive, Is.True, "one surviving mosquito must keep the round running");
            survivor.Kill();
            yield return null; yield return null;
            Assert.That(game.Phase, Is.EqualTo(MinigamePhase.PracticeComplete));
            foreach (var player in SessionScoreboard.Current.Players) Assert.That(player.Avatar.gameObject.activeSelf, Is.True, "avatar must be restored");
            Assert.That(Object.FindObjectsByType<MosquitoBody>(FindObjectsSortMode.None), Is.Empty);
            yield return RunTeamOutcome(true);
            yield return RunTeamOutcome(false);
        }
        private IEnumerator RunTeamOutcome(bool giantWins)
        {
            yield return StartScoredRound();
            MinigameResults reported = null;
            game.ResultsReported += result => reported = result;
            typeof(MinigameControllerBase).GetField("resultsDisplaySeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, .1f);
            if (giantWins) game.Sleep.Tick(game.Config.SleepTarget);
            else game.GetComponent<RoundTimer>().StartTimer(.05f);
            yield return null;
            yield return new WaitForSeconds(.06f);

            Assert.That(reported, Is.Not.Null);
            Assert.That(reported.Entries.Count, Is.EqualTo(requestedCount));
            foreach (var entry in reported.Entries)
                Assert.That(entry.Place, Is.EqualTo((entry.PlayerId == giantId) == giantWins ? 1 : requestedCount));

            float deadline = Time.realtimeSinceStartup + 10;
            while (SceneManager.GetActiveScene().name != "Hub" && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Hub"));
            yield return null; yield return null;
            Assert.That(Object.FindObjectsByType<MosquitoBody>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(Object.FindObjectsByType<MosquitoGiantRig>(FindObjectsSortMode.None), Is.Empty);
            var local = SessionScoreboard.Current.LocalPlayer.Avatar;
            Assert.That(local, Is.Not.Null);
            Assert.That(local.enabled, Is.True);
            Assert.That(local.MovementLocked, Is.False, "hub must restore movement");
            Assert.That(local.GetComponent<PlayerInputReader>().Suspended, Is.False);
        }
    }
}
