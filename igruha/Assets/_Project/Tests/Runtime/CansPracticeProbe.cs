#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.CansOrder;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    [DefaultExecutionOrder(500)]
    public sealed class CansPracticeProbe : MonoBehaviour
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly int[] Answer = { 0, 1, 2, 3, 4 };
        private static readonly int[][] Initial = { new[] { 0, 1, 2, 4, 3 }, new[] { 1, 0, 2, 4, 3 }, new[] { 1, 2, 3, 4, 0 } };
        private static readonly int[] Expected = { 3, 1, 0 };
        private CansOrderMinigame game;
        private CanShelf shelf;
        private float started, practiceAt = -1, roundAt = -1, quitAt = -1;
        private int step;
        private bool configured, initialChecked, wrongChecked, fullChecked, ready, failed;
        private string screenshot;
        private int Local => (int)NetworkManager.Singleton.LocalClientId;
        private bool Server => NetworkManager.Singleton.IsServer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.HasFlag("--cans-practice-check")) return;
            var go = new GameObject(nameof(CansPracticeProbe)); DontDestroyOnLoad(go); go.AddComponent<CansPracticeProbe>();
        }
        private void Awake()
        {
            started = Time.realtimeSinceStartup;
            LaunchArguments.TryGetValue("--practice-screenshot", out screenshot);
        }
        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (quitAt > 0) { if (now > quitAt) Application.Quit(failed ? 1 : 0); return; }
            if (now - started > 120) { Fail("timeout"); Application.Quit(1); return; }
            var selection = CharacterSelection.Current;
            if (selection != null && !selection.HasChosen && NetworkManager.Singleton != null &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
            {
                selection.ReportReady();
                for (int i = 0; i < 8; i++) if (!selection.IsTaken((Local + i) % 8)) { selection.Choose((Local + i) % 8); break; }
            }
            var current = MinigameControllerBase.Current as CansOrderMinigame;
            if (current == null) return;
            game = current;
            if (!game.TryGetGhostPlayer(Local, out var avatar, out shelf, out _)) return;
            avatar.GetComponent<PlayerInputReader>().EngageAutopilot();
            if (game.Phase == MinigamePhase.Practice && game.Stage == 2)
            {
                if (!configured)
                {
                    configured = true; practiceAt = now;
                    if (Server)
                    {
                        var answer = (List<int>)typeof(CansOrderMinigame).GetField("solution", Private).GetValue(game);
                        answer.Clear(); answer.AddRange(Answer);
                        typeof(CansOrderMinigame).GetMethod("ResetPracticePreview", Private).Invoke(game, null);
                        game.GetComponent<MinigameStageState>().EnterStage(2, 45);
                        CansOrderEntryNetState.TryPack(Answer, out ulong packed, out byte count);
                        if (game.TryGetPracticeMatches(99, packed, count, game.Round.Circle, out _) ||
                            game.TryGetPracticeMatches(0, packed, count, game.Round.Circle - 1, out _) ||
                            game.TryGetPracticeMatches(0, 0, count, game.Round.Circle, out _)) Fail("invalid preview accepted");
                    }
                    shelf.SetArrangement(Initial[Local]);
                }
                float age = now - practiceAt;
                if (age > 1 && !initialChecked && game.PracticeMatches == Expected[Local])
                {
                    initialChecked = true; CheckHud();
                    Debug.Log("CANS_PRACTICE INITIAL local=" + Local + " matches=" + game.PracticeMatches);
                }
                if (age > 2 && step == 0) { step = 1; shelf.SetArrangement(Answer); }
                if (age > 2.05f && step == 1) { step = 2; shelf.SetArrangement(Initial[0]); }
                if (age > 3 && !wrongChecked && step == 2 && game.PracticeMatches == 3)
                {
                    wrongChecked = true;
                    CansOrderEntryNetState.TryPack(Answer, out ulong old, out byte count);
                    game.ReceivePracticePreview(old, count, game.Round.Circle, 5);
                    if (game.PracticeMatches != 3) Fail("stale preview replaced current score");
                    Debug.Log("CANS_PRACTICE RAPID_SWAP current=3 stale ignored");
                    shelf.SetArrangement(Answer); step = 3;
                }
                if (age > 4 && !fullChecked && game.PracticeMatches == 5)
                {
                    fullChecked = true; CheckHud();
                    for (int i = 0; i < game.ContestantCount; i++)
                    { game.TryGetEntryNetState(i, out var state); if (state.Solved || state.Confirmed || state.Attempts != 0) Fail("preview changed a scored attempt"); }
                    Debug.Log("CANS_PRACTICE FULL 5/5 without confirmation");
                    if (!string.IsNullOrEmpty(screenshot)) ScreenCapture.CaptureScreenshot(screenshot);
                }
                if (age > 6 && !ready)
                {
                    ready = true;
                    if (!initialChecked || !wrongChecked || !fullChecked) Fail("missing preview check");
                    game.ToggleTutorialReady();
                }
            }
            if (game.Phase == MinigamePhase.Round && game.Stage == 2)
            {
                if (roundAt < 0)
                {
                    roundAt = now;
                    CansOrderEntryNetState.TryPack(Answer, out ulong packed, out byte count);
                    if (Server && game.TryGetPracticeMatches(Local, packed, count, game.Round.Circle, out _)) Fail("preview leaked in real round");
                    if (!Server) game.GetComponent<CansOrderNetwork>().RequestPracticePreview(packed, count, game.Round.Circle);
                }
                if (now - roundAt > 1)
                {
                    if (game.PracticeMatches != -1) Fail("practice score carried into real round");
                    var hud = FindAnyObjectByType<CansOrderLocalHud>();
                    var card = (GameObject)typeof(CansOrderLocalHud).GetField("practiceCanvas", Private).GetValue(hud);
                    if (card != null && card.activeInHierarchy) Fail("practice HUD survived in real round");
                    if (!fullChecked || !initialChecked || !wrongChecked) Fail("practice was skipped");
                    Debug.Log("CANS_PRACTICE PASS=" + !failed + " local=" + Local + " round oracle blocked");
                    quitAt = now + (Server ? 4 : 1);
                }
            }
        }
        private void CheckHud()
        {
            var hud = FindAnyObjectByType<CansOrderLocalHud>();
            var text = (TMP_Text)typeof(CansOrderLocalHud).GetField("practiceScore", Private).GetValue(hud);
            if (text == null || !text.gameObject.activeInHierarchy || !text.text.Contains(game.PracticeMatches + " / 5")) Fail("visible counter missing");
        }
        private void Fail(string message) { if (!failed) Debug.LogError("CANS_PRACTICE FAIL " + message); failed = true; }
    }
}
#endif
