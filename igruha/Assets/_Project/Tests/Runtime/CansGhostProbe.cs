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
    /// <summary>Opt-in real NGO regression check; never installed during normal play.</summary>
    [DefaultExecutionOrder(500)]
    public sealed class CansGhostProbe : MonoBehaviour
    {
        private CansOrderMinigame game;
        private CansGhostController ghost;
        private CansOrderNetwork network;
        private MinigameStageState stage;
        private PlayerInputReader input;
        private PlayerController original;
        private Rigidbody originalBody;
        private bool savedBody, originalMotorEnabled, originalKinematic;
        private string scenario, screenshots;
        private float started, quitAt = -1, staleAt = -1;
        private bool ready, solved, opened, secondTurn, requestedFirst, requestedSecond, unauthorized;
        private bool staleSent, retried, accepted, firstSeen, secondSeen, failed, finished, captured, swappedCapture;
        private readonly List<int> before = new List<int>();
        private int Winner => scenario == "host" ? 0 : 1;
        private const int Victim = 2;
        private int Local => (int)NetworkManager.Singleton.LocalClientId;
        private bool Server => NetworkManager.Singleton.IsServer;
        private double Age => NetworkClock.Now - ghost.State.StartedAt;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.HasFlag("--cans-ghost-check")) return;
            var go = new GameObject(nameof(CansGhostProbe));
            DontDestroyOnLoad(go); go.AddComponent<CansGhostProbe>();
        }
        private void Awake()
        {
            started = Time.realtimeSinceStartup;
            LaunchArguments.TryGetValue("--cans-ghost-check", out scenario);
            LaunchArguments.TryGetValue("--ghost-screenshots", out screenshots);
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
                    if (!selection.IsTaken((Local + i) % 8)) { selection.Choose((Local + i) % 8); break; }
            }
            if (game == null && MinigameControllerBase.Current is CansOrderMinigame cans)
            {
                game = cans; ghost = game.Ghost; network = game.GetComponent<CansOrderNetwork>();
                stage = game.GetComponent<MinigameStageState>();
                foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (behaviour.GetType().Name.EndsWith("DebugBot", StringComparison.Ordinal)) behaviour.enabled = false;
            }
            if (game == null) return;
            if (!savedBody && game.TryGetGhostPlayer(Winner, out original, out _, out _))
            {
                savedBody = true; originalMotorEnabled = original.enabled;
                originalBody = original.GetComponent<Rigidbody>(); originalKinematic = originalBody.isKinematic;
            }
            if (game.AwaitingTutorialReady && game.ContestantCount > 0 && !ready)
            { ready = true; game.ToggleTutorialReady(); }
            if (game.Phase == MinigamePhase.Round)
            {
                if (Server && !solved && game.Stage == 2)
                {
                    solved = true;
                    var answer = Get<List<int>>(game, "solution");
                    if (!game.ServerApplyArrangement(Winner, answer, NetworkClock.Now, NetworkClock.Now)) Fail("winner answer refused");
                    stage.EndStageNow();
                }
                if (ghost == null) { Fail("ghost component missing"); return; }
                if (!ghost.HasAwarded) return;
                if (ghost.State.OwnerId != Winner) Fail("wrong first finisher");
                if (ghost.State.Active && Age > 2 && original != null &&
                    (original.enabled || !originalBody.isKinematic)) Fail("hidden body still simulates");
                if (ghost.State.Active && Age > 2 && ghost.VisualRoot != null)
                {
                    var visual = ghost.VisualRoot.GetComponent<CansGhostVisual>();
                    if (visual.BodyMeshCount == 0 || visual.EyeCrossCount != 2) Fail("avatar or eye crosses missing");
                }
                if (Server && !opened && Age > 3)
                {
                    opened = true; Call(game, "BeginCircle"); stage.EnterStage(2, 70);
                }
                if (Age > 4 && ghost.State.Used == 0 && before.Count == 0 &&
                    game.TryGetGhostPlayer(Victim, out _, out var baselineShelf, out _)) baselineShelf.TryGetArrangement(before);
                if (Local == Winner && ghost.IsLocalGhost)
                {
                    game.TryGetGhostPlayer(Winner, out var avatar, out _, out _);
                    input = avatar.GetComponent<PlayerInputReader>(); input.EngageAutopilot();
                    game.TryGetGhostPlayer(Victim, out _, out var targetShelf, out _);
                    Vector3 inward = -new Vector3(targetShelf.Board.position.x, 0, targetShelf.Board.position.z).normalized;
                    Vector3 desired = targetShelf.Board.position + inward * 3.5f + Vector3.up * .3f;
                    Vector3 delta = desired - ghost.Pose.Position;
                    if (Age > 3 && delta.magnitude > .25f)
                    {
                        Quaternion heading = Quaternion.LookRotation(delta.normalized);
                        float p = heading.eulerAngles.x; if (p > 180) p -= 360;
                        Set(ghost, "yaw", heading.eulerAngles.y); Set(ghost, "pitch", Mathf.Clamp(p, -65, 65));
                        input.DriveMove(new Vector2(0, Mathf.Clamp01(delta.magnitude)));
                    }
                    else
                    {
                        input.DriveMove(Vector2.zero);
                        Quaternion look = Quaternion.LookRotation(targetShelf.Board.position + Vector3.up * .4f - ghost.Pose.Position);
                        Set(ghost, "yaw", look.eulerAngles.y); float p = look.eulerAngles.x;
                        Set(ghost, "pitch", p > 180 ? p - 360 : p);
                    }
                    if (Age > 8 && !requestedFirst && game.CanGhostTrick(Winner, Victim, ghost.Pose.Position, NetworkClock.Now))
                    { requestedFirst = true; Request(Victim); }
                    if (Age > 19 && !requestedSecond && ghost.State.Used == 1 && game.CanGhostTrick(Winner, Victim, ghost.Pose.Position, NetworkClock.Now))
                    { requestedSecond = true; Request(Victim); }
                }
                if (!unauthorized && Local != Winner && Local != Victim && Age > 5)
                { unauthorized = true; Request(Victim); }
                if (Age > 6 && Age < 7 && ghost.State.Used != 0) Fail("non-winner trick accepted");
                if (ghost.State.Used >= 1 && !firstSeen)
                {
                    firstSeen = true; CheckExchange(ghost.State.First);
                    Debug.Log("GHOST_CHECK FIRST " + ghost.State.First.FirstSlot + ":" + ghost.State.First.SecondSlot);
                }
                if (Local == Victim && firstSeen && !staleSent)
                {
                    staleSent = true; staleAt = now;
                    game.TryGetGhostPlayer(Victim, out _, out var shelf, out _); shelf.Active = false;
                    network.SubmitArrangement(before.ToArray(), NetworkClock.Now, 0, game.Round.Circle);
                }
                if (Local == Victim && staleSent && !retried && now - staleAt > 1)
                {
                    retried = true;
                    game.TryGetGhostPlayer(Victim, out _, out var shelf, out _);
                    if (!shelf.Active || Entry(Victim).Confirmed) Fail("stale confirmation did not reopen shelf");
                    shelf.SetArrangement(before); // The player can undo the trick and confirm again.
                    network.SubmitArrangement(before.ToArray(), NetworkClock.Now, ghost.AppliedRevision(Victim), game.Round.Circle);
                }
                if (Local == Victim && retried && !accepted && now - staleAt > 2)
                {
                    accepted = true;
                    if (!Entry(Victim).Confirmed) Fail("revised confirmation refused");
                    Debug.Log("GHOST_CHECK CONFIRM stale rejected; shelf reopened; corrected answer accepted");
                }
                if (Server && !secondTurn && Age > 17)
                { secondTurn = true; stage.EnterStage(5, 1); Call(game, "BeginCircle"); stage.EnterStage(2, 50); }
                if (ghost.State.Used == 1 && Age > 18 && !secondSeen)
                {
                    game.TryGetGhostPlayer(Victim, out _, out var shelf, out _); before.Clear(); shelf.TryGetArrangement(before);
                }
                if (ghost.State.Used == 2 && !secondSeen)
                {
                    secondSeen = true; CheckExchange(ghost.State.Second);
                    if (ghost.State.Charges != 0) Fail("third charge available");
                    Request(Victim);
                    Debug.Log("GHOST_CHECK SECOND " + ghost.State.Second.FirstSlot + ":" + ghost.State.Second.SecondSlot);
                }
                if (scenario == "disconnect" && Local == Winner && Age > 12)
                { Debug.Log("GHOST_CHECK DISCONNECT owner"); NetworkManager.Singleton.Shutdown(); Application.Quit(0); return; }
                if (scenario == "disconnect" && Server && Age > 14 && ghost.State.Active) Fail("disconnected ghost still active");
                if (Server && Age > 24) game.EndMinigame();
            }
            if (game.Phase == MinigamePhase.Results && !finished)
            {
                finished = true;
                if (ghost.State.Active || ghost.VisualRoot != null || ghost.IsLocalGhost) Fail("ghost leaked into results");
                if (original != null && (original.enabled != originalMotorEnabled ||
                    originalBody.isKinematic != originalKinematic)) Fail("source physics did not restore");
                if (!firstSeen || (scenario != "disconnect" && !secondSeen)) Fail("missing replicated tricks");
                if (Local == Victim && !accepted) Fail("confirmation race not tested");
                Debug.Log("GHOST_CHECK PASS=" + !failed + " owner=" + Winner + " charges=" + ghost.State.Charges);
                quitAt = now + (Server ? 4 : 1);
            }
        }
        private void LateUpdate()
        {
            if (ghost == null || !ghost.State.Active || string.IsNullOrEmpty(screenshots)) return;
            if (!captured && Age > 7.5)
            {
                captured = true; ScreenCapture.CaptureScreenshot(screenshots + "-flight.png");
                if (Local == Winner) CapturePortrait();
            }
            if (!swappedCapture && ghost.State.Used > 0)
            { swappedCapture = true; ScreenCapture.CaptureScreenshot(screenshots + "-swap.png"); }
        }
        private void CapturePortrait()
        {
            var root = ghost.VisualRoot;
            var nodes = root.GetComponentsInChildren<Transform>();
            var layers = new int[nodes.Length];
            for (int i = 0; i < nodes.Length; i++) { layers[i] = nodes[i].gameObject.layer; nodes[i].gameObject.layer = 30; }
            var go = new GameObject("Ghost check portrait");
            var camera = go.AddComponent<Camera>(); camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .085f, .12f);
            camera.cullingMask = 1 << 30; camera.fieldOfView = 35; camera.nearClipPlane = .05f; camera.farClipPlane = 8;
            camera.transform.position = root.TransformPoint(new Vector3(.55f, .68f, 2.5f));
            camera.transform.LookAt(root.position + Vector3.up * .45f);
            var texture = new RenderTexture(640, 640, 24);
            var pixels = new Texture2D(640, 640, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); pixels.Apply();
                System.IO.File.WriteAllBytes(screenshots + "-portrait.png", pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                for (int i = 0; i < nodes.Length; i++) nodes[i].gameObject.layer = layers[i];
                camera.targetTexture = null; texture.Release();
                Destroy(pixels); Destroy(texture); Destroy(go);
            }
        }
        private void CheckExchange(CansGhostTrick trick)
        {
            game.TryGetGhostPlayer(Victim, out _, out var shelf, out _);
            var after = new List<int>(); shelf.TryGetArrangement(after);
            if (before.Count != after.Count || before.Count == 0) { Fail("no shelf baseline"); return; }
            for (int i = 0; i < before.Count; i++)
            {
                int expected = i == trick.FirstSlot ? before[trick.SecondSlot] : i == trick.SecondSlot ? before[trick.FirstSlot] : before[i];
                if (after[i] != expected) Fail("swap did not exchange exactly two slots: " + i);
            }
            if (ghost.AppliedRevision(Victim) != trick.Serial) Fail("missing applied revision");
        }
        private void Request(int target)
        { if (Server) ghost.TryTrick(Local, target); else network.RequestGhostTrick(target); }
        private CansOrderEntry Entry(int id) => Get<CansOrderEntry>(Call(game, "Find", id), "Entry");
        private void Fail(string reason) { if (!failed) Debug.LogError("GHOST_CHECK FAIL " + reason); failed = true; }
        private static T Get<T>(object o, string name) => (T)o.GetType().GetField(name, Flags).GetValue(o);
        private static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Flags).Invoke(o, args);
    }
}
#endif
