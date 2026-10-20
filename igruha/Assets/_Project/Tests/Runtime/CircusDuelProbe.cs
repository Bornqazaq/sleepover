using System;
using System.Reflection;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.Circus;
using Igruha.Minigames.Stopwatch;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Opt-in real NGO check: --circus-duel-check pair|transition|disconnect.</summary>
    [DefaultExecutionOrder(500)]
    public sealed class CircusDuelProbe : MonoBehaviour
    {
        private MinigameControllerBase game;
        private CircusDuelPresentation duel;
        private CircusDuelCamera cameraShow;
        private CircusDuelComicView comic;
        private CircusDuelPortraits portraits;
        private MinigameStageState stage;
        private string scenario, screenshots;
        private float started, roundAt = -1, quitAt = -1;
        private bool ready, failed, staged, repeated, finished, launchObserved;
        private int seenCues, lastCue = -1, capturedCue = -1;
        private CanShelf[] privateShelves;
        private double introAt;
        private float originalAmbient;
        private Light[] houseLights;
        private float[] houseIntensities;
        private bool spectatorCaught, runnerCaught, sawOwnPit, sawSpectator;
        private int spectatorReadyFrame = -1;
        private bool AudienceScenario => scenario == "pit-client" || scenario == "pit-host";
        private int RunnerId => scenario == "pit-host" ? 0 : 2;
        private int LocalId => (int)NetworkManager.Singleton.LocalClientId;
        private bool Server => NetworkManager.Singleton.IsServer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.HasFlag("--circus-duel-check")) return;
            var go = new GameObject(nameof(CircusDuelProbe));
            DontDestroyOnLoad(go); go.AddComponent<CircusDuelProbe>();
        }

        private void Awake()
        {
            started = Time.realtimeSinceStartup;
            LaunchArguments.TryGetValue("--circus-duel-check", out scenario);
            LaunchArguments.TryGetValue("--duel-screenshots", out screenshots);
        }

        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (quitAt > 0) { if (now > quitAt) Application.Quit(failed ? 1 : 0); return; }
            if (now - started > 110) { Fail("timeout"); Application.Quit(1); return; }
            var selection = CharacterSelection.Current;
            if (selection != null && !selection.HasChosen && NetworkManager.Singleton != null &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
            {
                selection.ReportReady();
                for (int i = 0; i < 8; i++)
                    if (!selection.IsTaken((LocalId + i) % 8)) { selection.Choose((LocalId + i) % 8); break; }
            }
            var current = MinigameControllerBase.Current;
            if (current != null && current != game && (current is StopwatchMinigame || current is CansOrderMinigame))
            {
                game = current; duel = game.GetComponent<CircusDuelPresentation>();
                cameraShow = game.GetComponent<CircusDuelCamera>();
                comic = Get<CircusDuelComicView>(duel, "comicView");
                portraits = game.GetComponent<CircusDuelPortraits>();
                stage = game.GetComponent<MinigameStageState>(); originalAmbient = RenderSettings.ambientIntensity;
                houseLights = Get<Light[]>(duel, "houseLights");
                houseIntensities = new float[houseLights.Length];
                for (int i = 0; i < houseLights.Length; i++) houseIntensities[i] = houseLights[i].intensity;
                foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (behaviour.GetType().Name.EndsWith("DebugBot", StringComparison.Ordinal)) behaviour.enabled = false;
            }
            if (game == null) return;
            int count = game is StopwatchMinigame sw ? sw.ContestantCount : ((CansOrderMinigame)game).ContestantCount;
            if (game.AwaitingTutorialReady && count > 0 && !ready) { ready = true; game.ToggleTutorialReady(); }
            if (game.Phase == MinigamePhase.Round)
            {
                if (roundAt < 0) roundAt = now;
                float elapsed = now - roundAt;
                if (AudienceScenario && Server)
                {
                    if (!staged && elapsed > .25f)
                    {
                        staged = true;
                        Drop(RunnerId); Drop(3);
                        stage.EnterStage(game is StopwatchMinigame ? (byte)6 : (byte)5, 3f);
                    }
                    // Hold the pursuers away until the scheduled contact: the test needs a live
                    // runner, an actual spectator and two finalists in the same introduction.
                    if (staged && !runnerCaught)
                        for (int i = 0; i < 2; i++) Bear(i).transform.position = new Vector3(90 + i * 10, 0, 90);
                    if (staged && !spectatorCaught && elapsed > 1)
                    { spectatorCaught = true; Catch(3); }
                    if (duel.State.Active && !runnerCaught && NetworkClock.Now - duel.State.StartedAt > 3)
                    { runnerCaught = true; Catch(RunnerId); }
                }
                if (scenario == "transition")
                {
                    if (Server && !staged && elapsed > .25f)
                    {
                        staged = true;
                        // A real disconnect reduces three participants to two before the next safe boundary.
                        stage.EnterStage(game is StopwatchMinigame ? (byte)6 : (byte)5, 2f);
                    }
                    if (LocalId == 2 && elapsed > 1)
                    {
                        Debug.Log("DUEL_CHECK DISCONNECT transition");
                        NetworkManager.Singleton.Shutdown(); Application.Quit(0); return;
                    }
                }
                if (duel == null) { Fail("presentation missing"); return; }
                if (duel.State.Active)
                {
                    var state = duel.State;
                    if (introAt == 0)
                    {
                        introAt = state.StartedAt;
                        privateShelves = Get<CanShelf[]>(cameraShow, "privateShelves");
                        Debug.Log("DUEL_CHECK SNAPSHOT " + state.FirstCage + ":" + state.SecondCage + ":" + state.StartedAt.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    if (state.StartedAt != introAt) Fail("intro repeated");
                    int cue = state.Cue(NetworkClock.Now);
                    if (cue >= 0) seenCues |= 1 << cue;
                    byte expected = game is StopwatchMinigame ? StopwatchMinigame.StageDuel : CansOrderMinigame.StageDuel;
                    bool introStage = stage.Stage == expected;
                    if (!introStage && !launchObserved)
                    {
                        launchObserved = true;
                        // A packet can arrive before the client's interpolated ServerTime catches up.
                        // Judge an early input window by the authoritative stage timestamp in that packet.
                        double launchElapsed = stage.StageEndTime - stage.StageDuration - introAt;
                        if (launchElapsed + .00001 < CircusDuelState.IntroSeconds) Fail("input stage opened during intro");
                        Debug.Log("DUEL_CHECK LAUNCH elapsedServer=" + launchElapsed.ToString("F3",
                            System.Globalization.CultureInfo.InvariantCulture));
                    }
                    if (introStage && game is StopwatchMinigame watch && watch.LocalButton != null && watch.LocalButton.WindowOpen)
                        Fail("stopwatch accepts measurement during intro");
                    if (cue != lastCue)
                    {
                        lastCue = cue;
                        Debug.Log("DUEL_CHECK CUE " + cue + " elapsed=" + (NetworkClock.Now - introAt).ToString("F3",
                            System.Globalization.CultureInfo.InvariantCulture));
                    }
                    if (scenario == "disconnect" && LocalId == 1 && cue == 2)
                    {
                        Debug.Log("DUEL_CHECK DISCONNECT intro");
                        NetworkManager.Singleton.Shutdown(); Application.Quit(0); return;
                    }
                    double duelElapsed = NetworkClock.Now - introAt;
                    float cueStart = cue == 0 ? 0 : cue == 1 ? CircusDuelState.BlackoutSeconds :
                        cue == 2 ? CircusDuelState.SecondSpotAt : cue == 3 ? CircusDuelState.BothSpotsAt : CircusDuelState.IntroSeconds;
                    if (capturedCue != cue && duelElapsed > cueStart + .65 && !string.IsNullOrEmpty(screenshots) &&
                        SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    {
                        capturedCue = cue;
                        ScreenCapture.CaptureScreenshot(screenshots + "-cue" + cue + ".png");
                    }
                    if (duelElapsed > CircusDuelState.IntroSeconds + .2 && cameraShow != null && cameraShow.IsActive) Fail("duel camera blocked gameplay");
                    if (Server && !repeated && duelElapsed > CircusDuelState.IntroSeconds + 1.4)
                    {
                        repeated = true;
                        stage.EnterStage(game is StopwatchMinigame ? (byte)6 : (byte)5, .4f);
                    }
                    if (Server && duelElapsed > CircusDuelState.IntroSeconds + 3.9) game.EndMinigame();
                }
            }
            if (game.Phase == MinigamePhase.Results && !finished)
            {
                finished = true;
                if (duel.State.Active) Fail("duel did not clear at Results");
                if (portraits.IsActive || comic.IsVisible || duel.PitLightingActive) Fail("local comic/lighting leaked into Results");
                if (cameraShow != null && cameraShow.IsActive) Fail("camera did not restore at Results");
                CheckCanPrivacy(false);
                if (Mathf.Abs(RenderSettings.ambientIntensity - originalAmbient) > .001f) Fail("ambient light leaked out of finale");
                if (scenario != "disconnect" && (seenCues & 30) != 30) Fail("missing first/second/both/start cue");
                if (AudienceScenario && LocalId == RunnerId && (!sawOwnPit || !sawSpectator)) Fail("runner never kept own view or joined as spectator");
                if (AudienceScenario && LocalId == 3 && !sawSpectator) Fail("eliminated spectator missed introduction");
                Debug.Log("DUEL_CHECK PASS=" + !failed + " cues=" + seenCues);
                quitAt = now + (Server ? 4 : 1);
            }
        }

        private void LateUpdate()
        {
            if (duel == null || !duel.State.Active || game.Phase != MinigamePhase.Round) return;
            float showAge = (float)(NetworkClock.Now - duel.State.StartedAt);
            CheckCanPrivacy(cameraShow != null && cameraShow.IsActive);
            // A delayed first snapshot can already be past .1s when Update receives it.
            // Evaluate camera ownership after the presentation's LateUpdate, not before it runs.
            if (!AudienceScenario && showAge > .1f && showAge < CircusDuelState.IntroSeconds &&
                stage.Stage == (game is StopwatchMinigame ? StopwatchMinigame.StageDuel : CansOrderMinigame.StageDuel) &&
                (cameraShow == null || !cameraShow.IsActive)) Fail("duel camera did not take the shot");
            if (!AudienceScenario) { CheckComic(showAge); return; }
            double age = NetworkClock.Now - duel.State.StartedAt;
            if (age < .15 || age >= CircusDuelState.IntroSeconds) return;
            if (stage.Stage != (game is StopwatchMinigame ? StopwatchMinigame.StageDuel : CansOrderMinigame.StageDuel)) return;
            var local = Call(game, "Find", LocalId);
            bool inPit = Get<bool>(local, "InPit");
            var knockout = Get<CircusKnockout>(local, "Elimination");
            bool ownAction = inPit || knockout.IsPresenting;
            if (ownAction)
            {
                if (duel.IsVisible || cameraShow.IsActive) Fail("pit/knockout camera hijacked");
                if (Mathf.Abs(RenderSettings.ambientIntensity - originalAmbient) > .001f) Fail("pit ambient darkened");
                for (int i = 0; i < houseLights.Length; i++)
                    if (Mathf.Abs(houseLights[i].intensity - houseIntensities[i]) > .001f) Fail("pit house lighting changed");
                if (comic.IsVisible || portraits.IsActive) Fail("pit covered by comic or portrait cameras");
                if (!duel.PitLightingActive) Fail("pit missed finale celebration lights");
                foreach (var spot in Get<CircusSpotlight[]>(duel, "spots"))
                    if (!spot.FollowsBear && !spot.IsCelebrating) Fail("pit decorative spotlight missed celebration");
                if (Mathf.Abs(Time.timeScale - 1f) > .001f) Fail("slow motion changed chase simulation speed");
                if (!sawOwnPit) { sawOwnPit = true; Debug.Log("DUEL_CHECK OWN_PIT camera/base light preserved; celebration active; speed=1"); }
            }
            else
            {
                bool watching = Get<SpectatorCamera>(game, "spectator").IsActive;
                // BodyHidden is raised in knockout LateUpdate, after the cinematic's camera pass.
                // Its new spectator view joins on the next frame, before Cinemachine renders.
                if (watching && spectatorReadyFrame < 0) { spectatorReadyFrame = Time.frameCount; return; }
                if (!duel.IsVisible || !cameraShow.IsActive) Fail("finalist/spectator missed introduction");
                if (watching && !sawSpectator)
                { sawSpectator = true; Debug.Log("DUEL_CHECK SPECTATOR joinedCue=" + duel.State.Cue(NetworkClock.Now)); }
            }
            CheckComic(showAge);
            if (game is StopwatchMinigame && age < 1)
                foreach (var source in FindFirstObjectByType<DistractionDirector>().GetComponentsInChildren<AudioSource>())
                    if (source.name == "Tick") Fail("stopwatch still creates ticking audio");
        }

        private void CheckCanPrivacy(bool hidden)
        {
            if (privateShelves == null) return;
            // Inspect the shelf's current objects after presentation, excluding cans awaiting Destroy.
            foreach (var shelf in privateShelves)
                foreach (Can can in Get<System.Collections.IList>(shelf, "cans"))
                    if (can != null && can.PresentationHidden != hidden)
                        Fail("can identities escaped cinematic privacy or stayed hidden: " + can.name + ", expected=" + hidden);
        }

        private void CheckComic(float showAge)
        {
            if (duel.CanWatchLocally && showAge > CircusDuelState.BlackoutSeconds + .15f && showAge < CircusDuelState.ReturnAt)
            {
                if (!comic.IsVisible) Fail("comic missing for audience");
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null && !portraits.IsActive) Fail("portrait render cameras missing");
            }
        }

        private PitBear Bear(int index) => game is StopwatchMinigame watch ? watch.GetBear(index) : ((CansOrderMinigame)game).GetBear(index);
        private void Drop(int id)
        {
            var contestant = Call(game, "Find", id);
            Get<CageStation>(contestant, "Cage").SnapToLevel(0);
            if (game is StopwatchMinigame)
            {
                contestant.GetType().GetField("Errors").SetValue(contestant, Get<int>(game, "errorLimit"));
                Call(game, "EnterHatch");
            }
            else
            {
                var entry = Get<CansOrderEntry>(contestant, "Entry"); entry.Alive = false; entry.HeightFraction = 0;
                contestant.GetType().GetField("Entry").SetValue(contestant, entry);
                Call(game, "BeginPitFall", contestant, NetworkClock.Now);
            }
        }
        private void Catch(int id) => Call(game, "HandleBearCaught", Bear(0), Get<SessionPlayer>(Call(game, "Find", id), "Session").Avatar, Vector3.zero);
        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

        private void Fail(string reason)
        {
            if (!failed) Debug.LogError("DUEL_CHECK FAIL " + reason);
            failed = true;
        }
    }
}
