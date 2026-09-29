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
                game.RevealStarted += (decision, won) => { reveals++; };
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
                        if (observedStage == BelieveStage.Cancelled)
                        { cancelled = true; StartCoroutine(CheckCancellation()); }
                    }
                    byte leaveStage = scenario == "cancel-seating" ? BelieveStage.Seating :
                        scenario == "cancel-peek" ? BelieveStage.Peek : BelieveStage.Persuasion;
                    bool cancelThisRound = scenario.StartsWith("cancel-") && !cancelled && game.KnowerPlayerId != 0;
                    if (cancelThisRound && stage.Stage == leaveStage && game.KnowerPlayerId == LocalId && !departing)
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

        private IEnumerator CheckDecision()
        {
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
            Debug.Log("BELIEVE_CHECK CANCEL_PASS id=" + LocalId + " round=" + stage.Subround);
        }

        private IEnumerator CheckFinal()
        {
            yield return new WaitForSeconds(.25f);
            if (scenario.StartsWith("cancel-")) Check(cancelled, "cancellation scenario happened");
            else Check(decisions > 0, "local decision controls exercised");
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
