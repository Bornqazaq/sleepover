using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.MemoryRun;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests.PlayMode
{
    public sealed class MemoryRunContactTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject root;
        private MemoryRunMinigame game;
        private MemoryRunConfig config;
        private CharacterConfig characterConfig;
        private PlayerController walker;
        private int id, attempt;
        private Random.State randomState;
        private MemoryRunState State => Get<MemoryRunState>(game, "state");
        private MemoryRunRoute Route => Get<MemoryRunRoute>(game, "route");
        private Vector3 Exit => new Vector3(0, config.PlateSurfaceY, config.ExitPadZ + 1);
        private static T Get<T>(object target, string name) =>
            (T)target.GetType().GetField(name, Private).GetValue(target);
        private static void Call(object target, string name) =>
            target.GetType().GetMethod(name, Private).Invoke(target, null);

        [SetUp] public void SetUp()
        {
            randomState = Random.state;
            Random.InitState(728);
            root = new GameObject("Memory contact regression");
            root.SetActive(false);
            game = root.AddComponent<MemoryRunMinigame>();
            config = ScriptableObject.CreateInstance<MemoryRunConfig>();
            characterConfig = ScriptableObject.CreateInstance<CharacterConfig>();
            typeof(MemoryRunMinigame).GetField("config", Private).SetValue(game, config);
            var players = (List<SessionPlayer>)typeof(MinigameControllerBase)
                .GetField("playerList", Private).GetValue(game);
            for (int i = 0; i < 3; i++)
            {
                var avatar = new GameObject("Walker " + i);
                avatar.transform.SetParent(root.transform);
                var capsule = avatar.AddComponent<CapsuleCollider>();
                capsule.radius = .25f;
                var player = avatar.AddComponent<PlayerController>();
                typeof(PlayerController).GetField("config", Private).SetValue(player, characterConfig);
                player.enabled = false;
                players.Add(new SessionPlayer(i, "Player " + i) { Avatar = player });
            }
            root.SetActive(true);
            Call(game, "OnPlayersReady");
            typeof(MinigameControllerBase).GetField("phase", Private).SetValue(game, MinigamePhase.Round);
            Call(game, "BeginTurn");
            walker = game.CurrentWalker;
            id = game.CurrentWalkerId;
            attempt = Get<int>(game, "turnNumber");
        }

        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(characterConfig);
            Random.state = randomState;
        }

        private int SafeLane(int step)
        {
            for (int lane = 0; lane < MemoryRunConfig.LaneCount; lane++)
                if (Route.IsSafe(step, lane)) return lane;
            throw new AssertionException("Route has no safe plate");
        }
        private void Move(Vector3 position)
        {
            walker.transform.position = position;
            walker.GetComponent<Rigidbody>().position = position;
        }
        private void Touch(Vector3 position)
        {
            Move(position);
            Call(game, "FixedUpdate");
        }
        private void SafeSteps(int count)
        {
            for (int step = 0; step < count; step++) Touch(config.CellCenter(step, SafeLane(step)));
        }
        private MemoryRunProgress Progress
        {
            get { Assert.That(State.TryGet(id, out var value), Is.True); return value; }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(9)]
        public void ExitRequiresEveryStepInThisAttempt(int steps)
        {
            SafeSteps(steps);
            Touch(Exit);
            Assert.That(Progress.Finished, Is.False);
            Assert.That(Progress.Deaths, Is.EqualTo(1));
            Assert.That(Progress.BestStep, Is.EqualTo(steps));
        }

        [Test] public void FullSequenceFinishesAndRepeatedContactsDoNotDuplicateSteps()
        {
            for (int step = 0; step < config.Steps; step++)
            {
                Touch(config.CellCenter(step, SafeLane(step)));
                Touch(config.CellCenter(step, SafeLane(step)));
                Assert.That(Progress.BestStep, Is.EqualTo(step + 1));
            }
            Touch(Exit);
            Assert.That(Progress.Finished, Is.True);
            Assert.That(Progress.ArrivalOrder, Is.EqualTo(1));
            Assert.That(Progress.Deaths, Is.Zero);
        }

        [Test] public void PreviousRecordCannotAuthorizeFinishOnANewAttempt()
        {
            State.RegisterReach(id, config.Steps - 1, 1);
            Touch(Exit);
            Assert.That(Progress.Finished, Is.False);
            Assert.That(Progress.BestStep, Is.EqualTo(config.Steps));
            Assert.That(Progress.Deaths, Is.EqualTo(1));
        }

        [Test] public void SkippingAMiddleRowDoesNotAdvanceRecord()
        {
            SafeSteps(3);
            Touch(config.CellCenter(4, SafeLane(4)));
            Assert.That(Progress.BestStep, Is.EqualTo(3));
            Assert.That(Progress.Deaths, Is.EqualTo(1));
        }

        [Test] public void BeginningAnotherTurnResetsSequenceAndRejectsOldContacts()
        {
            SafeSteps(config.Steps);
            Call(game, "BeginTurn");
            Move(Exit);
            game.ApplyContactReport(id, attempt, Exit);
            Assert.That(Progress.Finished, Is.False);
            Assert.That(Progress.Deaths, Is.Zero);
            Touch(Exit);
            Assert.That(Progress.Finished, Is.False);
            Assert.That(Progress.Deaths, Is.EqualTo(1));
        }

        [Test] public void ReliableMineContactBeatsFollowingExitReport()
        {
            SafeSteps(config.Steps - 1);
            Vector3 mine = config.CellCenter(config.Steps - 1, (SafeLane(config.Steps - 1) + 1) % 3);
            // The transform has already moved beyond the brief landing.
            Move(mine + Vector3.forward);
            game.ApplyContactReport(id, attempt, mine);
            Move(Exit);
            game.ApplyContactReport(id, attempt, Exit);
            Assert.That(Progress.Finished, Is.False);
            Assert.That(Progress.Deaths, Is.EqualTo(1));
            Assert.That(Progress.BestStep, Is.EqualTo(config.Steps - 1));
        }

        [Test] public void BacktrackingIsAllowedButAnotherPlateInSameRowStillExplodes()
        {
            SafeSteps(2);
            Touch(config.CellCenter(0, SafeLane(0)));
            Assert.That(Progress.Deaths, Is.Zero);
            Touch(config.CellCenter(0, (SafeLane(0) + 1) % 3));
            Assert.That(Progress.Deaths, Is.EqualTo(1));
        }

        [Test] public void FlyingOverMineDoesNotCountAsLanding()
        {
            Touch(config.CellCenter(0, (SafeLane(0) + 1) % 3) + Vector3.up);
            Assert.That(Progress.BestStep, Is.Zero);
            Assert.That(Progress.Deaths, Is.Zero);
            Touch(config.CellCenter(0, SafeLane(0)));
            Assert.That(Progress.BestStep, Is.EqualTo(1));
        }

        [Test] public void ReportsRejectOtherPlayersOldAttemptsAndImpossiblePositions()
        {
            Vector3 first = config.CellCenter(0, SafeLane(0));
            Move(first);
            game.ApplyContactReport(id + 10, attempt, first);
            game.ApplyContactReport(id, attempt - 1, first);
            game.ApplyContactReport(id, attempt, new Vector3(float.NaN, 0, 0));
            Move(first + Vector3.back * config.StepPitch * 2);
            game.ApplyContactReport(id, attempt, first);
            Assert.That(Progress.BestStep, Is.Zero);
            Move(first);
            game.ApplyContactReport(id, attempt, first);
            Assert.That(Progress.BestStep, Is.EqualTo(1));
        }

        [Test] public void DelayedReliableContactIsValidatedAgainstRecentTransformHistory()
        {
            Vector3 first = config.CellCenter(0, SafeLane(0));
            typeof(MemoryRunMinigame).GetMethod("RememberWalkerPosition", Private)
                .Invoke(game, new object[] { first + Vector3.up });
            // Unreliable movement can overtake a reliable contact waiting for retransmission.
            Move(first + Vector3.forward * config.StepPitch * 2);
            game.ApplyContactReport(id, attempt, first);
            Assert.That(Progress.BestStep, Is.EqualTo(1));

            Call(game, "BeginTurn");
            int nextAttempt = Get<int>(game, "turnNumber");
            game.ApplyContactReport(id, nextAttempt, first);
            Assert.That(Get<int>(game, "completedSteps"), Is.Zero, "old attempt history must be cleared");
        }

        [Test] public void DiagonalContactMayArriveBeforeTheNextTransformSnapshot()
        {
            var lanes = Get<List<int>>(Route, "safeLanes");
            lanes[0] = 0;
            lanes[1] = 2;
            Touch(config.CellCenter(0, 0));
            // The server still observes the previous row's opposite corner.
            game.ApplyContactReport(id, attempt, config.CellCenter(1, 2));
            Assert.That(Progress.BestStep, Is.EqualTo(2));
        }

        [Test] public void PhysicsContactIsUsedEvenWhenRenderedTransformIsStillAirborne()
        {
            Vector3 first = config.CellCenter(0, SafeLane(0));
            walker.transform.position = first + Vector3.up;
            walker.GetComponent<Rigidbody>().position = first;
            Call(game, "FixedUpdate");
            Assert.That(Progress.BestStep, Is.EqualTo(1));
        }
    }
}
