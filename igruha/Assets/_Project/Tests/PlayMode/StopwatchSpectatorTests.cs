using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.Stopwatch;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.Tests.PlayMode
{
    public sealed class StopwatchSpectatorTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> roots = new List<GameObject>();
        private SpectatorCamera spectator;
        private MinigameCameraController cameraController;
        private CharacterConfig characterConfig;

        private GameObject Root(string name)
        {
            var root = new GameObject(name);
            roots.Add(root);
            return root;
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, Private).SetValue(target, value);
        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        [SetUp]
        public void SetUp()
        {
            characterConfig = ScriptableObject.CreateInstance<CharacterConfig>();
            var root = Root("Spectator test camera");
            cameraController = root.AddComponent<MinigameCameraController>();
            var rigType = Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
            Set(cameraController, "thirdPersonRig", Root("Test rig").AddComponent(rigType));
            spectator = root.AddComponent<SpectatorCamera>();
            Set(spectator, "cameraController", cameraController);
        }

        [TearDown]
        public void TearDown()
        {
            spectator.Deactivate();
            for (int i = roots.Count - 1; i >= 0; i--) Object.DestroyImmediate(roots[i]);
            roots.Clear();
            Object.DestroyImmediate(characterConfig);
        }

        private SessionPlayer Player(int id)
        {
            var root = Root("Player " + id);
            root.SetActive(false);
            var avatar = root.AddComponent<PlayerController>();
            Set(avatar, "config", characterConfig);
            avatar.enabled = false;
            root.GetComponent<Rigidbody>().useGravity = false;
            root.SetActive(true);
            return new SessionPlayer(id, root.name) { Avatar = avatar };
        }

        [Test]
        public void RemovedTargetIsReplacedEvenWhileItsAvatarStaysActive()
        {
            var alive = new List<SessionPlayer> { Player(1), Player(2) };
            var original = Player(0);
            cameraController.Apply(CameraMode.ThirdPerson, original.Avatar.transform);
            var restoreTarget = cameraController.CurrentTarget;
            spectator.Activate(alive);
            var removed = spectator.Target;
            alive.Remove(removed);
            Assert.That(removed.Avatar.gameObject.activeInHierarchy, Is.True,
                "CircusKnockout hides renderers, but retains the network avatar");

            Call(spectator, "Update");

            Assert.That(spectator.Target, Is.SameAs(alive[0]));
            Assert.That(cameraController.CurrentTarget, Is.SameAs(alive[0].Avatar.CameraTarget));
            spectator.Deactivate();
            Assert.That(cameraController.CurrentTarget, Is.SameAs(restoreTarget));
        }

        [Test]
        public void EmptyRosterClearsTargetAndCanLaterAcquireANewOne()
        {
            var alive = new List<SessionPlayer> { Player(1) };
            spectator.Activate(alive);
            alive.Clear();
            Call(spectator, "Update");
            Assert.That(spectator.Target, Is.Null);
            alive.Add(Player(2));
            Call(spectator, "Update");
            Assert.That(spectator.Target, Is.SameAs(alive[0]));
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void StopwatchRefreshesSpectatorRosterOnHostAndClient(bool client, bool disconnect)
        {
            var game = Root("Stopwatch").AddComponent<StopwatchMinigame>();
            Set(game, "spectator", spectator);
            if (client)
                typeof(MinigameControllerBase).GetField("bridge", Private).SetValue(game, new ClientBridge());
            var contestants = (IList)typeof(StopwatchMinigame).GetField("contestants", Private).GetValue(game);
            var contestantType = typeof(StopwatchMinigame).GetNestedType("Contestant", BindingFlags.NonPublic);
            for (int i = 1; i <= 3; i++)
            {
                var contestant = Activator.CreateInstance(contestantType);
                contestantType.GetField("Session").SetValue(contestant, Player(i));
                contestants.Add(contestant);
            }
            var alive = (IReadOnlyList<SessionPlayer>)Call(game, "CollectAlivePlayers");
            spectator.Activate(alive);
            int departed = spectator.Target.Id;
            if (client && disconnect) Object.DestroyImmediate(spectator.Target.Avatar.gameObject);
            else if (client) game.ApplyNetworkCage(departed, 3, 0, false, false, true, false, 0, false);
            else game.HandlePlayerLeft(departed);

            Call(game, "Update");
            Call(spectator, "Update");

            Assert.That(alive.Count, Is.EqualTo(2));
            Assert.That(spectator.Target, Is.Not.Null);
            Assert.That(spectator.Target.Id, Is.Not.EqualTo(departed));
            Assert.That(cameraController.CurrentTarget, Is.SameAs(spectator.Target.Avatar.CameraTarget));
            var unchanged = spectator.Target;
            Call(game, "Update");
            Call(spectator, "Update");
            Assert.That(spectator.Target, Is.SameAs(unchanged), "a living target must not be picked anew every frame");
        }

        private sealed class ClientBridge : IMinigameNetworkBridge
        {
            public bool HasAuthority => false;
            public bool IsNetworkSession => true;
            public void PublishPhase(MinigamePhase phase) { }
            public void PublishResults(MinigameResults results, bool seriesFinal) { }
            public void RequestLeaveRound() { }
        }
    }
}
