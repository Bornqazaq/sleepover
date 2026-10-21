#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.CansOrder;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Opt-in NGO regression for later and simultaneous solvers with shared targets.</summary>
    [DefaultExecutionOrder(500)]
    public sealed class CansGhostGroupProbe : MonoBehaviour
    {
        private const int Victim = 2, FirstSolver = 1;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private CansOrderMinigame game;
        private CansOrderNetwork network;
        private MinigameStageState stage;
        private string scenario, screenshots;
        private bool ready, firstSolved, laterSolved, opened, positioned, firstRequest, secondRequest;
        private bool firstChecked, secondChecked, secondTurn, staleSent, retried, confirmed, ended, failed, finished;
        private bool saved, motorEnabled, kinematic, capture, rolesChecked;
        private PlayerController avatar;
        private Rigidbody body;
        private int participantCount;
        private float started, staleAt, quitAt = -1, solvedSnapshotAt = -1;
        private float noticeAt = -1, localGhostAt = -1;
        private bool noticeCaptured, transitionChecked;
        private readonly List<int> baseline = new List<int>();
        private readonly List<CansGhostTrick> tricks = new List<CansGhostTrick>();
        private int Local => (int)NetworkManager.Singleton.LocalClientId;
        private bool Server => NetworkManager.Singleton.IsServer;
        private bool Disconnect => scenario == "multiple-disconnect";
        private double Age => NetworkClock.Now - game.GetGhost(FirstSolver).State.StartedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.HasFlag("--cans-ghost-group-check")) return;
            var go = new GameObject(nameof(CansGhostGroupProbe));
            DontDestroyOnLoad(go); go.AddComponent<CansGhostGroupProbe>();
        }
        private void Awake()
        {
            started = Time.realtimeSinceStartup;
            LaunchArguments.TryGetValue("--cans-ghost-group-check", out scenario);
            LaunchArguments.TryGetValue("--ghost-screenshots", out screenshots);
        }
        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (quitAt > 0) { if (now > quitAt) Application.Quit(failed ? 1 : 0); return; }
            if (now - started > 130) { Fail("timeout"); Application.Quit(1); return; }
            var selection = CharacterSelection.Current;
            if (selection != null && !selection.HasChosen && NetworkManager.Singleton != null &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
            {
                selection.ReportReady();
                for (int i = 0; i < 8; i++)
                    if (!selection.IsTaken((Local + i) % 8)) { selection.Choose((Local + i) % 8); break; }
            }
            if (game == null && MinigameControllerBase.Current is CansOrderMinigame cans)
            {
                game = cans; network = game.GetComponent<CansOrderNetwork>(); stage = game.GetComponent<MinigameStageState>();
                foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (behaviour.GetType().Name.EndsWith("DebugBot", StringComparison.Ordinal)) behaviour.enabled = false;
            }
            if (game == null) return;
            if (!saved && game.TryGetGhostPlayer(Local, out avatar, out _, out _))
            {
                saved = true; motorEnabled = avatar.enabled; body = avatar.GetComponent<Rigidbody>(); kinematic = body.isKinematic;
                avatar.GetComponent<PlayerInputReader>().EngageAutopilot();
            }
            if (game.AwaitingTutorialReady && game.ContestantCount > 0 && !ready)
            { ready = true; participantCount = game.ContestantCount; game.ToggleTutorialReady(); }
            if (game.Phase == MinigamePhase.Round)
            {
                CheckSuccessTransition(now);
                if (Server && !firstSolved && game.Stage == 2)
                {
                    firstSolved = true; SubmitSolution(FirstSolver); stage.EndStageNow();
                }
                var first = game.GetGhost(FirstSolver);
                if (first == null) return;
                if (Server && !laterSolved && Age > 2)
                {
                    laterSolved = true; BeginTurn();
                    for (int i = 0; i < participantCount; i++) if (i != FirstSolver && i != Victim) SubmitSolution(i);
                    stage.EndStageNow();
                }
                // Server-time age is not a delivery barrier: a busy host may still be baking
                // six avatar copies while headless clients advance their network clocks.
                if (game.Round.SolvedCount == participantCount - 1 && solvedSnapshotAt < 0) solvedSnapshotAt = now;
                if (!rolesChecked && solvedSnapshotAt >= 0)
                {
                    var mine = game.GetGhost(Local);
                    bool complete = game.Ghosts.Count == participantCount - 1;
                    if (Local != Victim) complete &= mine != null && mine.IsLocalGhost && mine.VisualRoot != null;
                    foreach (var role in game.Ghosts) complete &= role.VisualRoot != null;
                    if (complete)
                    {
                        rolesChecked = true;
                        foreach (var role in game.Ghosts)
                            if (role.VisualRoot.GetComponent<CansGhostVisual>().ArmForwardDot < .98f) Fail("ghost arms are not forward");
                        if (Local != Victim && avatar != null && (avatar.enabled || !body.isKinematic)) Fail("ghost body still simulates");
                        Debug.Log("GHOST_GROUP ROLES " + game.Ghosts.Count + " local=" + Local + " delay=" + (now - solvedSnapshotAt).ToString("F3"));
                    }
                    else if (now - solvedSnapshotAt > CansGhostRules.SolvedSeconds + 5)
                        Fail("solved players did not receive visible controllable ghosts");
                }
                if (Server && !opened && Age > 5) { opened = true; BeginTurn(); }
                if (Server && !positioned && Age > 6)
                {
                    positioned = true;
                    game.TryGetGhostPlayer(Victim, out _, out var shelf, out _);
                    Vector3 inward = -new Vector3(shelf.Board.position.x, 0, shelf.Board.position.z).normalized;
                    foreach (var role in game.Ghosts)
                    {
                        var pose = role.Pose; pose.Position = shelf.Board.position + inward * 3 + Vector3.up * .5f;
                        typeof(CansGhostController).GetProperty("Pose").SetValue(role, pose);
                    }
                }
                if (Age > 7 && baseline.Count == 0) ReadShelf(baseline);
                var localRole = game.GetGhost(Local);
                if (Local != Victim && Age > 9 && !firstRequest && localRole != null &&
                    game.CanGhostTrick(Local, Victim, localRole.Pose.Position, NetworkClock.Now))
                { firstRequest = true; Request(Victim); }
                if (!firstChecked && Age > 12 && AllUsed(1))
                {
                    firstChecked = true; CheckShelf(false);
                    if (Server)
                    {
                        if (game.GetGhost(0).TryTrick(Victim, Victim)) Fail("forged owner accepted");
                        if (game.GetGhost(0).TryTrick(0, FirstSolver)) Fail("solved target accepted");
                    }
                    Debug.Log("GHOST_GROUP FIRST ghosts=" + game.Ghosts.Count);
                }
                if (Local == Victim && firstChecked && !staleSent)
                {
                    staleSent = true; staleAt = now;
                    game.TryGetGhostPlayer(Victim, out _, out var shelf, out _); shelf.Active = false;
                    network.SubmitArrangement(baseline.ToArray(), NetworkClock.Now, 0, game.Round.Circle);
                }
                if (Local == Victim && staleSent && !retried && now - staleAt > 1)
                {
                    retried = true;
                    game.TryGetGhostPlayer(Victim, out _, out var shelf, out _);
                    if (!shelf.Active || Entry(Victim).Confirmed) Fail("combined stale answer not rejected");
                    var current = new List<int>(); shelf.TryGetArrangement(current);
                    network.SubmitArrangement(current.ToArray(), NetworkClock.Now, game.AppliedGhostRevision(Victim), game.Round.Circle);
                }
                if (Local == Victim && retried && !confirmed && now - staleAt > 2)
                { confirmed = true; if (!Entry(Victim).Confirmed) Fail("combined revision not accepted"); }
                if (Disconnect && Local == FirstSolver && Age > 17)
                {
                    Debug.Log("GHOST_GROUP DISCONNECT first solver"); NetworkManager.Singleton.Shutdown(); Application.Quit(0); return;
                }
                if (Server && !secondTurn && Age > 20) { secondTurn = true; BeginTurn(); }
                if (Age > 21 && Age < 23) { baseline.Clear(); ReadShelf(baseline); }
                if (Local != Victim && Age > 24 && !secondRequest && localRole != null &&
                    game.CanGhostTrick(Local, Victim, localRole.Pose.Position, NetworkClock.Now))
                { secondRequest = true; Request(Victim); }
                if (!secondChecked && Age > 27 && AllUsed(2))
                {
                    secondChecked = true; CheckShelf(true);
                    foreach (var role in game.Ghosts)
                        if (role.State.Active && role.State.Charges != 0) Fail("charge budget shared or exceeded");
                    if (Local != Victim) Request(Victim);
                    if (Disconnect && first.State.Active) Fail("departed ghost still active");
                    Debug.Log("GHOST_GROUP SECOND revision=" + game.AppliedGhostRevision(Victim));
                }
                if (Server && !ended && Age > 30)
                {
                    ended = true; SubmitSolution(Victim);
                    // The final submission already closes Placement automatically.
                    // Do not skip its Reveal (and success card) a second time.
                    if (game.Stage == 2) stage.EndStageNow();
                    foreach (var role in game.Ghosts) if (role.State.Active) Fail("ghost survived last solution");
                    if (game.GetGhost(Victim) != null) Fail("last solver briefly became ghost");
                    Debug.Log("GHOST_GROUP ALL_SOLVED roles stopped");
                }
                if (Server && ended && Age > 33) game.EndMinigame();
            }
            if (game.Phase == MinigamePhase.Results && !finished)
            {
                finished = true;
                foreach (var role in game.Ghosts)
                    if (role.State.Active || role.IsLocalGhost || role.VisualRoot != null) Fail("ghost leaked into results");
                if (saved && avatar != null && (avatar.enabled != motorEnabled || body.isKinematic != kinematic)) Fail("body not restored");
                if (!rolesChecked || !firstChecked || !secondChecked || noticeAt < 0 ||
                    Local != Victim && !transitionChecked || Local == Victim && !confirmed) Fail("missing scenario checks");
                Debug.Log("GHOST_GROUP PASS=" + !failed + " ghosts=" + game.Ghosts.Count);
                quitAt = now + (Server ? 4 : 1);
            }
        }
        private void LateUpdate()
        {
            if (game == null || string.IsNullOrEmpty(screenshots)) return;
            if (!noticeCaptured && noticeAt >= 0 && Time.realtimeSinceStartup - noticeAt > .35f)
            { noticeCaptured = true; ScreenCapture.CaptureScreenshot(screenshots + "-solved.png"); }
            if (capture || game.LocalGhost == null || localGhostAt < 0 || Time.realtimeSinceStartup - localGhostAt < .35f) return;
            capture = true; ScreenCapture.CaptureScreenshot(screenshots + "-multiple.png");
        }
        private void CheckSuccessTransition(float now)
        {
            bool showing = false;
            foreach (var hud in game.GetComponentsInChildren<CansGhostHud>()) showing |= hud.ShowingSolved;
            if (showing && noticeAt < 0) { noticeAt = now; Debug.Log("GHOST_GROUP SUCCESS_CARD local=" + Local); }
            if (showing && game.LocalGhost != null) Fail("ghost appeared before success card ended");
            if (game.LocalGhost == null || transitionChecked) return;
            localGhostAt = now; transitionChecked = true;
            if (noticeAt < 0 || now - noticeAt < CansGhostRules.SolvedSeconds - .05f) Fail("success card skipped or too short");
            Debug.Log("GHOST_GROUP TRANSFORM after=" + (now - noticeAt).ToString("F3"));
        }
        private bool AllUsed(int used)
        {
            foreach (var role in game.Ghosts)
                if (role.State.Used != (Disconnect && role.State.OwnerId == FirstSolver && used == 2 ? 1 : used)) return false;
            return game.Ghosts.Count == participantCount - 1;
        }
        private void BeginTurn()
        {
            typeof(CansOrderMinigame).GetMethod("BeginCircle", Flags).Invoke(game, null); stage.EnterStage(2, 70);
        }
        private void SubmitSolution(int id)
        {
            var answer = (List<int>)typeof(CansOrderMinigame).GetField("solution", Flags).GetValue(game);
            double windowStart = (double)typeof(CansOrderMinigame).GetField("placementWindowStart", Flags).GetValue(game);
            double stamp = Math.Max(NetworkClock.Now, windowStart);
            if (!game.ServerApplyArrangement(id, answer, stamp, NetworkClock.Now, game.RequiredGhostRevision(id))) Fail("solution refused " + id);
        }
        private void Request(int target)
        {
            if (Server) game.GetGhost(Local)?.TryTrick(Local, target); else network.RequestGhostTrick(target);
        }
        private void ReadShelf(List<int> into)
        {
            if (game.TryGetGhostPlayer(Victim, out _, out var shelf, out _)) shelf.TryGetArrangement(into);
        }
        private void CheckShelf(bool second)
        {
            tricks.Clear();
            foreach (var role in game.Ghosts)
            {
                var trick = second ? role.State.Second : role.State.First;
                if (trick.Serial != 0) tricks.Add(trick);
            }
            tricks.Sort((a, b) => a.Serial.CompareTo(b.Serial));
            var expected = new List<int>(baseline);
            foreach (var trick in tricks)
                (expected[trick.FirstSlot], expected[trick.SecondSlot]) = (expected[trick.SecondSlot], expected[trick.FirstSlot]);
            var actual = new List<int>(); ReadShelf(actual);
            if (expected.Count != actual.Count) { Fail("shelf size differs"); return; }
            for (int i = 0; i < actual.Count; i++) if (expected[i] != actual[i]) { Fail("shared target swapped out of order"); break; }
            if (tricks.Count == 0 || game.AppliedGhostRevision(Victim) != tricks[tricks.Count - 1].Serial) Fail("global target revision differs");
            Debug.Log("GHOST_GROUP ORDER " + string.Join(",", tricks.ConvertAll(t => t.Serial + ":" + t.OwnerId + ":" + t.FirstSlot + ":" + t.SecondSlot)));
        }
        private CansOrderEntry Entry(int id)
        {
            var contestant = typeof(CansOrderMinigame).GetMethod("Find", Flags).Invoke(game, new object[] { id });
            return (CansOrderEntry)contestant.GetType().GetField("Entry", Flags).GetValue(contestant);
        }
        private void Fail(string why) { if (!failed) Debug.LogError("GHOST_GROUP FAIL " + why); failed = true; }
    }
}
#endif
