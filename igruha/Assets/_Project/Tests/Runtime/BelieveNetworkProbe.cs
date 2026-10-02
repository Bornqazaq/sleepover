using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Core.UI;
using Igruha.Minigames.BelieveOrNot;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Igruha.Tests
{
    /// <summary>Opt-in development check of real decision UI, NGO cancellation, scores and hub return.</summary>
    public sealed class BelieveNetworkProbe : MonoBehaviour
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float Timeout = 180f;
        private BelieveOrNotMinigame game;
        private MinigameStageState stage;
        private string scenario;
        private float started, nextChoice, quitAt = -1;
        private int handledRound, observedRound, reveals, decisions;
        private byte observedStage;
        private int predictionRound, predictionResultRound, expectedWinner;
        private float persuasionStarted;
        private bool PredictionScenario => scenario == "predictions" || scenario == "cancel-persuasion";
        private bool ready, cancelled, departing, finished, failed, returned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--believe-check", out _)) return;
            var root = new GameObject(nameof(BelieveNetworkProbe));
            DontDestroyOnLoad(root);
            root.AddComponent<BelieveNetworkProbe>();
        }

        private void Awake()
        {
            started = Time.realtimeSinceStartup;
            LaunchArguments.TryGetValue("--believe-check", out scenario);
        }

        private static T Get<T>(object target, string field) =>
            (T)target.GetType().GetField(field, Private).GetValue(target);
        private static void Call(object target, string method) =>
            target.GetType().GetMethod(method, Private).Invoke(target, null);
        private int LocalId => (int)NetworkManager.Singleton.LocalClientId;

        private void Update()
        {
            var selection = CharacterSelection.Current;
            if (selection != null && !selection.HasChosen && Time.realtimeSinceStartup >= nextChoice &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
            {
                nextChoice = Time.realtimeSinceStartup + 1;
                selection.ReportReady();
                for (int n = 0; n < 8; n++)
                {
                    int index = (LocalId + n) % 8;
                    if (!selection.IsTaken(index)) { selection.Choose(index); break; }
                }
            }
            var current = MinigameControllerBase.Current as BelieveOrNotMinigame;
            if (current != null && current != game)
            {
                game = current;
                stage = game.GetComponent<MinigameStageState>();
                ready = false; handledRound = observedRound = reveals = 0; observedStage = 0;
                game.RevealStarted += (decision, won) =>
                {
                    reveals++;
                    expectedWinner = won ? game.DeciderPlayerId : game.KnowerPlayerId;
                };
            }
            if (game != null)
            {
                // --bot handles bootstrap/readiness; this probe owns only decisions in this game.
                game.GetType().GetField("autoplay", Private).SetValue(game, false);
                if (game.AwaitingTutorialReady && game.RosterCount > 0 && !ready)
                { ready = true; game.ToggleTutorialReady(); }
                if (game.Phase == MinigamePhase.Round)
                {
                    if (stage.Subround != observedRound) { observedRound = stage.Subround; reveals = 0; }
                    if (stage.Stage != observedStage)
                    {
                        observedStage = stage.Stage;
                        if (observedStage == BelieveStage.Persuasion) persuasionStarted = Time.realtimeSinceStartup;
                        if (observedStage == BelieveStage.Cancelled)
                        { cancelled = true; StartCoroutine(CheckCancellation()); }
                    }
                    if (PredictionScenario) CheckPredictions();
                    byte leaveStage = scenario == "cancel-seating" ? BelieveStage.Seating :
                        scenario == "cancel-peek" ? BelieveStage.Peek : BelieveStage.Persuasion;
                    bool cancelThisRound = scenario.StartsWith("cancel-") && !cancelled && game.KnowerPlayerId != 0;
                    if (cancelThisRound && stage.Stage == leaveStage && game.KnowerPlayerId == LocalId && !departing &&
                        (scenario != "cancel-persuasion" || Time.realtimeSinceStartup - persuasionStarted > 2f))
                    {
                        departing = true;
                        Debug.Log("BELIEVE_CHECK DISCONNECT round=" + stage.Subround + " stage=" + stage.Stage);
                        NetworkManager.Singleton.Shutdown();
                        Application.Quit(0);
                        return;
                    }
                    if (!cancelThisRound && stage.Stage == BelieveStage.Persuasion &&
                        game.DeciderPlayerId == LocalId && handledRound != stage.Subround)
                    { handledRound = stage.Subround; StartCoroutine(CheckDecision()); }
                }
                if (game.Phase == MinigamePhase.Results && !finished)
                { finished = true; StartCoroutine(CheckFinal()); }
            }
            if (finished && !returned && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub" &&
                SessionScoreboard.Current?.LocalPlayer?.Avatar != null)
            {
                returned = true;
                var avatar = SessionScoreboard.Current.LocalPlayer.Avatar;
                Check(!avatar.MovementLocked && !avatar.ImpulseImmune, "locks restored in hub");
                Debug.Log("BELIEVE_CHECK RETURN passed=" + !failed + " decisions=" + decisions + " cancelled=" + cancelled);
                quitAt = Time.realtimeSinceStartup + (NetworkManager.Singleton.IsServer ? 5 : 1);
            }
            if (quitAt > 0 && Time.realtimeSinceStartup >= quitAt) Application.Quit(failed ? 2 : 0);
            if (Time.realtimeSinceStartup - started > Timeout)
            { Check(false, "timeout"); Application.Quit(2); }
        }

        private void CheckPredictions()
        {
            if (stage.Stage == BelieveStage.Persuasion)
            {
                Check(game.PredictionResults.Round == 0, "other predictions remain private before reveal");
                if (predictionRound != stage.Subround)
                {
                    predictionRound = stage.Subround;
                    StartCoroutine(PickPrediction());
                }
            }
            if (stage.Stage == BelieveStage.Reaction && predictionResultRound != stage.Subround)
            {
                predictionResultRound = stage.Subround;
                var result = game.PredictionResults;
                var match = Get<BelieveMatchState>(game, "match");
                Check(result.Round == stage.Subround && result.WinnerId == expectedWinner, "prediction winner matches opened boxes");
                int spectators = 0;
                foreach (var entry in Get<List<BelieveEntry>>(game, "entries"))
                    if (entry.Present && entry.PlayerId != match.Seat0PlayerId && entry.PlayerId != match.Seat1PlayerId) spectators++;
                Check(result.Picks.Length == spectators, "one prediction per present spectator");
                var snapshot = new List<string>();
                for (int i = 0; i < result.Picks.Length; i++)
                {
                    var pick = result.Picks[i];
                    Check(pick.PlayerId != match.Seat0PlayerId && pick.PlayerId != match.Seat1PlayerId, "seated RPC rejected");
                    int expected = pick.PlayerId % 2 == 0 ? match.Seat0PlayerId : match.Seat1PlayerId;
                    Check(pick.WinnerId == expected, "repeat RPC cannot change accepted prediction");
                    snapshot.Add(pick.PlayerId + ":" + pick.WinnerId);
                }
                snapshot.Sort();
                var panel = Get<BelievePredictionPanel>(game, "predictionPanel");
                Check(panel.IsVisible && !panel.CanPick, "revealed predictions visible to both roles");
                Debug.Log("BELIEVE_CHECK PREDICTIONS round=" + result.Round + " winner=" + result.WinnerId +
                    " picks=" + string.Join(",", snapshot));
                game.GetComponent<BelieveOrNotNetwork>().SubmitPrediction(stage.Subround, match.Seat0PlayerId);
            }
        }

        private IEnumerator PickPrediction()
        {
            yield return new WaitForSeconds(.2f);
            var match = Get<BelieveMatchState>(game, "match");
            var network = game.GetComponent<BelieveOrNotNetwork>();
            var panel = Get<BelievePredictionPanel>(game, "predictionPanel");
            if (LocalId == match.Seat0PlayerId || LocalId == match.Seat1PlayerId)
            {
                Check(!panel.IsVisible, "seated player has no prediction controls");
                network.SubmitPrediction(stage.Subround, match.Seat0PlayerId);
                yield break;
            }
            Check(panel.IsVisible && panel.CanPick, "spectator prediction controls visible");
            int target = LocalId % 2 == 0 ? match.Seat0PlayerId : match.Seat1PlayerId;
            int other = target == match.Seat0PlayerId ? match.Seat1PlayerId : match.Seat0PlayerId;
            var avatar = SessionScoreboard.Current.LocalPlayer.Avatar;
            Check(!avatar.MovementLocked, "spectator remains free to move");
            var cursor = Cursor.lockState;
            var pause = PauseScreen.Current;
            Call(pause, "Pause");
            panel.Choose(target);
            Check(panel.CanPick, "pause blocks prediction choice");
            pause.Resume();
            network.SubmitPrediction(stage.Subround - 1, target);
            panel.Choose(target);
            network.SubmitPrediction(stage.Subround, -7);
            panel.Choose(other);
            network.SubmitPrediction(stage.Subround, other);
            yield return new WaitForSeconds(.5f);
            Check(Get<int>(game, "localPrediction") == target, "private receipt confirms original choice");
            Check(!panel.CanPick && !avatar.MovementLocked && Cursor.lockState == cursor, "choice preserves movement and cursor");
            Check(game.PredictionResults.Round == 0, "accepted prediction not in public state");
            Debug.Log("BELIEVE_CHECK PRIVATE_PASS id=" + LocalId + " round=" + stage.Subround);
        }

        private IEnumerator CheckDecision()
        {
            if (PredictionScenario) yield return new WaitForSeconds(3f);
            var panel = Get<BelieveDecisionPanel>(game, "decisionPanel");
            var pause = PauseScreen.Current;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Call(pause, "Pause");
                foreach (var key in new[] { Key.LeftArrow, Key.RightArrow })
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                    yield return null;
                    yield return null;
                    Check(panel.IsOpen && stage.Stage == BelieveStage.Persuasion, "arrow blocked by pause");
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    yield return null;
                }
                Get<Button>(panel, "keepButton").onClick.Invoke();
                Get<Button>(panel, "swapButton").onClick.Invoke();
                Check(panel.IsOpen && stage.Stage == BelieveStage.Persuasion, "buttons blocked by pause");
                Check(pause.IsPaused && Time.timeScale == 1, "network pause leaves server running");
                pause.Resume();
                bool swap = stage.Subround % 2 == 1;
                Get<Button>(panel, swap ? "swapButton" : "keepButton").onClick.Invoke();
                Check(!panel.IsOpen, "decision accepted after resume");
                decisions++;
                Debug.Log("BELIEVE_CHECK PAUSE_PASS id=" + LocalId + " swap=" + swap);
            }
            finally
            {
                if (pause != null && pause.IsPaused) pause.Resume();
                InputSystem.RemoveDevice(keyboard);
            }
        }

        private IEnumerator CheckCancellation()
        {
            // The stage and match are distinct replicated fields. Allow the next NGO tick for statistics.
            yield return new WaitForSeconds(.25f);
            var match = Get<BelieveMatchState>(game, "match");
            var table = Get<BelieveTable>(game, "table");
            var hud = Get<BelieveSeatHud>(game, "seatHud");
            var text = Get<TMP_Text>(hud, "talkText");
            Check(match.Cancelled && !match.Resolved, "cancel flag without resolution");
            Check(reveals == 0, "no outcome event in cancelled round");
            Check(text.enabled && text.text.Contains("Кон отменён") && text.text.Contains("Очко никому"), "cancel notice");
            for (int i = 0; i < BelieveTable.SeatCount; i++)
                Check(table.GetBox(i).Card == BelieveCard.Unknown, "cancel hides cards");
            Check(!Get<BelievePeekView>(game, "peekView").IsOpen &&
                !Get<BelieveDecisionPanel>(game, "decisionPanel").IsOpen &&
                !Get<QuickPhrasePanel>(game, "phrasePanel").IsOpen, "cancel closes controls");
            Check(game.PredictionResults.Round == 0 && !Get<BelievePredictionPanel>(game, "predictionPanel").IsVisible,
                "cancel clears predictions and closes panel");
            Debug.Log("BELIEVE_CHECK CANCEL_PASS id=" + LocalId + " round=" + stage.Subround);
        }

        private IEnumerator CheckFinal()
        {
            yield return new WaitForSeconds(.25f);
            if (scenario.StartsWith("cancel-")) Check(cancelled, "cancellation scenario happened");
            else if (!PredictionScenario) Check(decisions > 0, "local decision controls exercised");
            if (scenario == "predictions") Check(predictionResultRound > 0, "prediction reveal observed");
            var entries = Get<List<BelieveEntry>>(game, "entries");
            Debug.Log("BELIEVE_CHECK FINAL " + string.Join("|", entries.ConvertAll(e =>
                e.PlayerId + ":" + e.RoundsWon + ":" + e.DeciderWins + ":" + e.RoundsSeated)));
        }

        private void Check(bool condition, string message)
        {
            if (condition) return;
            failed = true;
            Debug.LogError("BELIEVE_CHECK FAIL id=" + LocalId + " " + message);
        }
    }
}
