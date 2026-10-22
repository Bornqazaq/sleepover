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
        private List<int> answer;
        private List<int> copied;
        private List<int> almost;
        private static readonly int[][] Initial = { new[] { 0, 1, 2, 4, 3 }, new[] { 1, 0, 2, 4, 3 }, new[] { 1, 2, 3, 4, 0 } };
        private static readonly int[] Expected = { 3, 1, 0 };
        private CansOrderMinigame game;
        private CanShelf shelf;
        private float started, practiceAt = -1, roundAt = -1, quitAt = -1;
        private int step;
        private bool configured, initialChecked, wrongChecked, fullChecked, ready, failed, copiedChecked, roundConfigured;
        private int sentCircle;
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
                    answer = CansSolutionFixture.Known(Local, game.Round.CanCount);
                    copied = CansSolutionFixture.Known((Local + 1) % 3, game.Round.CanCount);
                    almost = new List<int>(); foreach (int index in Initial[0]) almost.Add(answer[index]);
                    if (Server)
                    {
                        if (!CansSolutionFixture.HasUniqueAnswers(game)) Fail("practice answers repeated");
                        CansSolutionFixture.SetKnown(game);
                        typeof(CansOrderMinigame).GetMethod("ResetPracticePreview", Private).Invoke(game, null);
                        game.GetComponent<MinigameStageState>().EnterStage(2, 45);
                        CansOrderEntryNetState.TryPack(answer, out ulong packed, out byte count);
                        if (game.TryGetPracticeMatches(99, packed, count, game.Round.Circle, out _) ||
                            game.TryGetPracticeMatches(0, packed, count, game.Round.Circle - 1, out _) ||
                            game.TryGetPracticeMatches(0, 0, count, game.Round.Circle, out _)) Fail("invalid preview accepted");
                    }
                    var initial = new List<int>(); foreach (int index in Initial[Local]) initial.Add(answer[index]);
                    shelf.SetArrangement(initial);
                }
                float age = now - practiceAt;
                if (age > 1 && !initialChecked && game.PracticeMatches == Expected[Local])
                {
                    initialChecked = true; CheckHud();
                    Debug.Log("CANS_PRACTICE INITIAL local=" + Local + " matches=" + game.PracticeMatches);
                }
                if (age > 2 && step == 0) { step = 1; shelf.SetArrangement(answer); }
                if (age > 2.05f && step == 1) { step = 2; shelf.SetArrangement(almost); }
                if (age > 3 && !wrongChecked && step == 2 && game.PracticeMatches == 3)
                {
                    wrongChecked = true;
                    CansOrderEntryNetState.TryPack(answer, out ulong old, out byte count);
                    game.ReceivePracticePreview(old, count, game.Round.Circle, 5);
                    if (game.PracticeMatches != 3) Fail("stale preview replaced current score");
                    Debug.Log("CANS_PRACTICE RAPID_SWAP current=3 stale ignored");
                    shelf.SetArrangement(answer); step = 3;
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
            if (game.Phase != MinigamePhase.Round) return;
            if (!roundConfigured && game.Stage == 2)
            {
                roundConfigured = true; roundAt = now;
                if (Server)
                {
                    if (!CansSolutionFixture.HasUniqueAnswers(game)) Fail("round answers repeated");
                    Debug.Log("CANS_PRACTICE UNIQUE personal answers generated for every player");
                    CansSolutionFixture.SetKnown(game);
                }
                else
                {
                    for (int i = 0; i < game.ContestantCount; i++)
                    {
                        game.TryGetEntry(i, out var entry, out _);
                        if (CansSolutionFixture.Answer(game, entry.PlayerId).Count != 0) Fail("secret reached client");
                    }
                }
                CansOrderEntryNetState.TryPack(answer, out ulong packed, out byte count);
                if (Server && game.TryGetPracticeMatches(Local, packed, count, game.Round.Circle, out _)) Fail("preview leaked in real round");
                if (!Server) game.GetComponent<CansOrderNetwork>().RequestPracticePreview(packed, count, game.Round.Circle);
            }
            if (!roundConfigured || now - roundAt < 1.5f) return;
            if (game.PracticeMatches != -1) Fail("practice score carried into real round");
            var hud = FindAnyObjectByType<CansOrderLocalHud>();
            var card = (GameObject)typeof(CansOrderLocalHud).GetField("practiceCanvas", Private).GetValue(hud);
            if (card != null && card.activeInHierarchy) Fail("practice HUD survived in real round");
            if (!fullChecked || !initialChecked || !wrongChecked) Fail("practice was skipped");
            var turn = game.GetComponent<MinigameStageState>();
            if (game.Stage == 2 && sentCircle != game.Round.Circle && turn.StageDuration - turn.StageRemaining > 1)
            {
                sentCircle = game.Round.Circle;
                var attempt = sentCircle == 1 ? copied : answer;
                Debug.Log("CANS_PRACTICE SEND local=" + Local + " circle=" + sentCircle);
                shelf.SetArrangement(attempt);
                double stamp = NetworkClock.Now;
                if (Server)
                {
                    if (!game.ServerApplyArrangement(Local, attempt, stamp, stamp, game.RequiredGhostRevision(Local))) Fail("host attempt refused");
                }
                else game.GetComponent<CansOrderNetwork>().SubmitArrangement(attempt, stamp, game.RequiredGhostRevision(Local), game.Round.Circle);
            }
            for (int i = 0; i < game.ContestantCount; i++)
            {
                game.TryGetEntryNetState(i, out var state);
                if (!Server)
                {
                    // The stage and the entry list travel independently. Do not rebuild a
                    // server snapshot from a client's accepted lamp and not-yet-revealed score.
                    var states = (NetworkList<CansOrderEntryNetState>)typeof(CansOrderNetwork)
                        .GetField("entries", Private).GetValue(game.GetComponent<CansOrderNetwork>());
                    if (i >= states.Count) continue;
                    state = states[i];
                }
                if (state.PlayerId != Local || !state.Revealed || !state.Confirmed || state.Attempts != sentCircle) continue;
                if (sentCircle == 1 && !copiedChecked)
                {
                    copiedChecked = true;
                    int expected = CansOrderSolutions.CountMatches(copied, answer);
                    if (state.Solved || state.Matches != expected || state.Attempts != 1) Fail("copied solution scored as own");
                    Debug.Log("CANS_PRACTICE COPY local=" + Local + " matches=" + state.Matches + " solved=" + state.Solved);
                }
                if (sentCircle > 1 && state.Solved)
                {
                    if (!copiedChecked || state.Matches != 5 || state.Attempts != 2 || state.ArrangementCount != 0) Fail("own solution or answer privacy failed");
                    if (Server && game.Round.SolvedCount != game.ContestantCount) continue;
                    Debug.Log("CANS_PRACTICE PASS=" + !failed + " local=" + Local + " own answer 5/5, copied answer rejected, secrets private");
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
