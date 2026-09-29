using System;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Minigames.Stopwatch;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Tests
{
    /// <summary>Opt-in development probe. Server stages deaths; clients receive ordinary cage/impact messages.</summary>
    public sealed class StopwatchSpectatorProbe : MonoBehaviour
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int Victim = 2;
        private const float Timeout = 120;
        private StopwatchMinigame game;
        private SpectatorCamera spectator;
        private string scenario, screenshots;
        private float started, roundAt = -1, nextChoice, quitAt = -1;
        private bool ready, staged, changed, selected, checkedTarget, final, returned, failed;
        private int LocalId => (int)NetworkManager.Singleton.LocalClientId;
        private bool Server => NetworkManager.Singleton.IsServer;
        private static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
        private static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private).Invoke(obj, args);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--stopwatch-spectator-check", out _)) return;
            var root = new GameObject(nameof(StopwatchSpectatorProbe));
            DontDestroyOnLoad(root);
            root.AddComponent<StopwatchSpectatorProbe>();
        }
        private void Awake()
        {
            started = Time.realtimeSinceStartup;
            LaunchArguments.TryGetValue("--stopwatch-spectator-check", out scenario);
            LaunchArguments.TryGetValue("--spectator-screenshots", out screenshots);
        }
        private void Update()
        {
            var selection = CharacterSelection.Current;
            if (selection != null && !selection.HasChosen && Time.realtimeSinceStartup >= nextChoice &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
            {
                nextChoice = Time.realtimeSinceStartup + 1;
                selection.ReportReady();
                for (int i = 0; i < 8; i++)
                {
                    int index = (LocalId + i) % 8;
                    if (!selection.IsTaken(index)) { selection.Choose(index); break; }
                }
            }
            var current = MinigameControllerBase.Current as StopwatchMinigame;
            if (current != null && current != game)
            {
                game = current; ready = false; roundAt = -1;
                spectator = Get<SpectatorCamera>(game, "spectator");
            }
            if (game != null && game.AwaitingTutorialReady && game.ContestantCount > 0 && !ready)
            { ready = true; game.ToggleTutorialReady(); }
            if (game != null && game.Phase == MinigamePhase.Round)
            {
                if (roundAt < 0) roundAt = Time.realtimeSinceStartup;
                float elapsed = Time.realtimeSinceStartup - roundAt;
                if (Server && !staged && elapsed > 2)
                {
                    staged = true;
                    // Hold briefing so the probe isolates spectating from random round outcomes.
                    game.GetComponent<MinigameStageState>().EnterStage(1, 100);
                    Eliminate(0); Eliminate(1);
                }
                if (LocalId < 2 && spectator.IsActive && !selected)
                {
                    selected = true;
                    Call(spectator, "SetTarget", Player(Victim));
                    Check(spectator.Target?.Id == Victim, "initial target");
                    Debug.Log("STOPWATCH_CHECK WATCHING id=" + LocalId + " target=" + spectator.Target?.Id);
                }
                if (elapsed > 8 && !changed)
                {
                    changed = true;
                    if (scenario == "disconnect" && LocalId == Victim)
                    {
                        Debug.Log("STOPWATCH_CHECK DISCONNECT id=" + LocalId);
                        NetworkManager.Singleton.Shutdown(); Application.Quit(0); return;
                    }
                    if (Server && scenario == "eliminate") Eliminate(Victim);
                }
                if (elapsed > 12 && !checkedTarget)
                {
                    checkedTarget = true;
                    var living = new List<int>();
                    for (int i = 0; i < game.ContestantCount; i++)
                        if (game.TryGetCageState(i, out var state) && state.Alive && Player(state.PlayerId)?.Avatar != null)
                            living.Add(state.PlayerId);
                    living.Sort();
                    Check(string.Join(",", living) == "3,4", "two eligible survivors");
                    Debug.Log("STOPWATCH_CHECK STATE " + string.Join(",", living));
                    if (LocalId < 2)
                    {
                        Check(selected && spectator.IsActive, "observer active after target left");
                        Check(living.Contains(spectator.Target?.Id ?? -1), "automatically follows living target");
                        var controller = Get<MinigameCameraController>(spectator, "cameraController");
                        Check(spectator.Target != null && controller.CurrentTarget == spectator.Target.Avatar.CameraTarget,
                            "camera follows replacement avatar");
                        for (int i = 0; i < 4; i++)
                        {
                            Call(spectator, "Advance", i % 2 == 0 ? 1 : -1);
                            Check(living.Contains(spectator.Target?.Id ?? -1), "cycling excludes eliminated/disconnected player");
                        }
                        if (scenario == "eliminate")
                            Check(Player(Victim)?.Avatar != null && Player(Victim).Avatar.gameObject.activeInHierarchy,
                                "eliminated network avatar remains active");
                        Debug.Log("STOPWATCH_CHECK RETARGET id=" + LocalId + " passed=" + !failed + " target=" + spectator.Target?.Id);
                        if (!string.IsNullOrEmpty(screenshots) && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                        {
                            System.IO.Directory.CreateDirectory(screenshots);
                            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(screenshots, scenario + "-spectator.png"));
                        }
                    }
                }
                if (Server && elapsed > 15) game.EndMinigame();
            }
            if (game != null && game.Phase == MinigamePhase.Results && !final)
            {
                final = true;
                Check(checkedTarget && !spectator.IsActive, "checks completed and spectator ended");
            }
            if (final && !returned && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub" &&
                SessionScoreboard.Current?.LocalPlayer?.Avatar != null)
            {
                returned = true;
                var avatar = SessionScoreboard.Current.LocalPlayer.Avatar;
                Check(avatar.enabled && !avatar.MovementLocked && !avatar.ImpulseImmune, "hub movement restored");
                bool visible = false;
                foreach (var renderer in avatar.GetComponentsInChildren<Renderer>()) visible |= renderer.enabled;
                Check(visible, "avatar visible in hub");
                Debug.Log("STOPWATCH_CHECK RETURN passed=" + !failed + " id=" + LocalId);
                quitAt = Time.realtimeSinceStartup + (Server ? 8 : 1);
            }
            if (quitAt > 0 && Time.realtimeSinceStartup >= quitAt) Application.Quit(failed ? 2 : 0);
            if (Time.realtimeSinceStartup - started > Timeout)
            { Check(false, "timeout"); Application.Quit(2); }
        }
        private SessionPlayer Player(int id)
        {
            var contestant = Call(game, "Find", id);
            return contestant == null ? null : (SessionPlayer)contestant.GetType().GetField("Session").GetValue(contestant);
        }
        private void Eliminate(int id)
        {
            var contestant = Call(game, "Find", id);
            contestant.GetType().GetField("Alive").SetValue(contestant, false);
            contestant.GetType().GetField("InPit").SetValue(contestant, true);
            Get<EliminationRanking>(game, "ranking").AddEliminationGroup(new[] { id });
            var avatar = Player(id).Avatar;
            Call(game, "HandleBearCaught", avatar, Vector3.zero);
            Debug.Log("STOPWATCH_CHECK ELIMINATE id=" + id);
        }
        private void Check(bool condition, string message)
        {
            if (condition) return;
            failed = true;
            Debug.LogError("STOPWATCH_CHECK FAIL id=" + LocalId + " " + message);
        }
    }
}
