using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Core.UI;
using Igruha.Minigames.BelieveOrNot;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Igruha.Tests.PlayMode
{
    public sealed class BelieveOrNotRegressionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<Object> objects = new List<Object>();
        private float savedTime;
        private CursorLockMode savedCursor;
        private bool savedVisible;

        [SetUp] public void SetUp()
        {
            savedTime = Time.timeScale;
            savedCursor = Cursor.lockState;
            savedVisible = Cursor.visible;
        }

        [TearDown] public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            Time.timeScale = savedTime;
            Cursor.lockState = savedCursor;
            Cursor.visible = savedVisible;
        }

        private GameObject Root(string name)
        {
            var root = new GameObject(name);
            objects.Add(root);
            return root;
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, Private).SetValue(target, value);
        private static T Get<T>(object target, string field) =>
            (T)target.GetType().GetField(field, Private).GetValue(target);
        private static void Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        [TestCase(Decision.Keep, Key.LeftArrow)]
        [TestCase(Decision.Swap, Key.RightArrow)]
        public void PauseBlocksKeyboardAndButtonsButResumeAllowsOneDecision(Decision decision, Key key)
        {
            var pause = Root("Pause").AddComponent<PauseScreen>();
            var root = Root("Decision");
            root.SetActive(false);
            var panel = root.AddComponent<BelieveDecisionPanel>();
            var keep = Root("Keep").AddComponent<Button>();
            var swap = Root("Swap").AddComponent<Button>();
            Set(panel, "keepButton", keep);
            Set(panel, "swapButton", swap);
            root.SetActive(true);
            panel.Open();
            int picks = 0;
            Decision picked = Decision.None;
            panel.DecisionPicked += value => { picks++; picked = value; };
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Call(pause, "Pause");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                InputSystem.Update();
                Call(panel, "Update");
                keep.onClick.Invoke();
                swap.onClick.Invoke();
                Assert.That(picks, Is.Zero);
                Assert.That(panel.IsOpen, Is.True);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                pause.Resume();
                var button = decision == Decision.Keep ? keep : swap;
                button.onClick.Invoke();
                button.onClick.Invoke();
                Assert.That(picks, Is.EqualTo(1));
                Assert.That(picked, Is.EqualTo(decision));
                Assert.That(panel.IsOpen, Is.False);
            }
            finally { InputSystem.RemoveDevice(keyboard); }
        }

        [TestCase(BelieveStage.Seating)]
        [TestCase(BelieveStage.Peek)]
        [TestCase(BelieveStage.Persuasion)]
        public void KnowerLeavingAwardsForfeitWithoutRevealingCardsAndNextRoundStillScores(byte interruptedStage)
        {
            var root = Root("Game");
            root.SetActive(false);
            var stage = root.AddComponent<MinigameStageState>();
            var table = root.AddComponent<BelieveTable>();
            var game = root.AddComponent<BelieveOrNotMinigame>();
            var config = ScriptableObject.CreateInstance<BelieveOrNotConfig>();
            objects.Add(config);
            var hud = Root("Hud").AddComponent<BelieveSeatHud>();
            var talk = Root("Talk").AddComponent<TextMeshProUGUI>();
            Set(hud, "talkText", talk);
            var boxes = new[] { Root("Box0").AddComponent<BelieveBox>(), Root("Box1").AddComponent<BelieveBox>() };
            Set(table, "boxes", boxes);
            Set(game, "config", config);
            Set(game, "stageState", stage);
            Set(game, "table", table);
            Set(game, "seatHud", hud);
            root.SetActive(true);
            var players = (List<SessionPlayer>)typeof(MinigameControllerBase).GetField("playerList", Private).GetValue(game);
            for (int i = 0; i < 4; i++) players.Add(new SessionPlayer(i, "Player " + i));
            Call(game, "OnPlayersReady");
            Call(game, "BeginRound", 1);
            stage.EnterStage(interruptedStage, 40f);
            int reveals = 0;
            game.RevealStarted += (decision, correct) => reveals++;

            game.HandlePlayerLeft(game.KnowerPlayerId);

            var match = Get<BelieveMatchState>(game, "match");
            Assert.That(stage.Stage, Is.EqualTo(BelieveStage.Cancelled));
            Assert.That(match.Cancelled, Is.True);
            Assert.That(match.Resolved, Is.True);
            Assert.That(match.Decision, Is.EqualTo(Decision.None));
            Assert.That(match.TeamAWins + match.TeamBWins, Is.Zero);
            Assert.That(reveals, Is.Zero, "cancel must not raise outcome audio/animation");
            Assert.That(talk.enabled, Is.True);
            Assert.That(talk.text, Does.Contain("СОПЕРНИК ВЫШЕЛ"));
            foreach (var box in boxes) Assert.That(box.Card, Is.EqualTo(BelieveCard.Unknown));
            Assert.That(game.TournamentEntries[game.TournamentEntries[0].PlayerId == match.ForfeitWinner ? 0 : Get<List<BelieveEntry>>(game, "entries").FindIndex(e => e.PlayerId == match.ForfeitWinner)].ForfeitWins, Is.EqualTo(1));

            stage.EndStageNow();
            Assert.That(stage.Subround, Is.EqualTo(2));
            Assert.That(stage.Stage, Is.EqualTo(BelieveStage.Seating));
            Assert.That(Get<BelieveMatchState>(game, "match").IsRematch, Is.False, "cancelled hand has no rematch");
            stage.EnterStage(BelieveStage.Persuasion, 40f);
            game.HandleDecision(game.DeciderPlayerId, Decision.Keep);
            match = Get<BelieveMatchState>(game, "match");
            Assert.That(match.Cancelled, Is.False);
            Assert.That(match.Resolved, Is.True);
            Call(game, "CommitDuelResult");
            Assert.That(match.TeamAWins + match.TeamBWins, Is.Zero);
            Assert.That(reveals, Is.EqualTo(1));
        }
    }
}
