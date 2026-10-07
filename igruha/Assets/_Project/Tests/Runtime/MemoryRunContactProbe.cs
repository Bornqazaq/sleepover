using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.MemoryRun;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.Tests
{
    /// <summary>Opt-in development check. Stages owner physics samples; all verdicts use production NGO.</summary>
    public sealed class MemoryRunContactProbe : MonoBehaviour
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float Timeout = 210;
        private MemoryRunMinigame game;
        private MemoryRunConfig config;
        private PlayerController local;
        private Rigidbody body;
        private bool ready, staged, driving, final, failed, returned;
        private float started, nextChoice, quitAt;
        private int lastAttempt = -1;
        private int detonations;
        private int LocalId => (int)NetworkManager.Singleton.LocalClientId;
        private bool Server => NetworkManager.Singleton.IsServer;
        private static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private).GetValue(obj);
        private static void Call(object obj, string method) => obj.GetType().GetMethod(method, Private).Invoke(obj, null);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.HasFlag("--memory-contact-check")) return;
            var root = new GameObject(nameof(MemoryRunContactProbe));
            DontDestroyOnLoad(root);
            root.AddComponent<MemoryRunContactProbe>();
        }
        private void Awake() => started = Time.realtimeSinceStartup;

        private void Update()
        {
            var selection = CharacterSelection.Current;
            if (selection != null && !selection.HasChosen && Time.realtimeSinceStartup >= nextChoice &&
                SceneManager.GetActiveScene().name == "Hub")
            {
                nextChoice = Time.realtimeSinceStartup + 1;
                selection.ReportReady();
                for (int i = 0; i < 8; i++)
                {
                    int index = (LocalId + i) % 8;
                    if (!selection.IsTaken(index)) { selection.Choose(index); break; }
                }
            }
            var current = MinigameControllerBase.Current as MemoryRunMinigame;
            if (current != null && current != game)
            {
                game = current;
                config = Get<MemoryRunConfig>(game, "config");
                ready = staged = false;
                detonations = 0;
                game.MineDetonated += OnMine;
                var bot = game.GetComponent<MemoryRunDebugBot>();
                if (bot != null) bot.enabled = false;
            }
            if (game != null && game.AwaitingTutorialReady && game.RosterCount > 0 && !ready)
            { ready = true; game.ToggleTutorialReady(); }
            if (game != null && game.Phase == MinigamePhase.Round)
            {
                if (!staged)
                {
                    staged = true;
                    local = SessionScoreboard.Current.LocalPlayer.Avatar;
                    body = local.GetComponent<Rigidbody>();
                    local.enabled = false;
                    body.useGravity = false;
                    body.linearVelocity = Vector3.zero;
                    if (Server)
                    {
                        // Test fixture only; no route data is added to the production protocol.
                        var lanes = Get<List<int>>(Get<MemoryRunRoute>(game, "route"), "safeLanes");
                        for (int i = 0; i < config.Steps; i++) lanes[i] = i % MemoryRunConfig.LaneCount;
                        var queue = Get<TurnQueue>(game, "queue");
                        queue.ApplyOrder(new[] { 0, 1, 2 });
                        queue.Advance();
                        game.GetComponent<MemoryRunNetwork>().PublishTurnOrder(queue.Order);
                        Call(game, "BeginTurn");
                    }
                }
                int attempt = Get<int>(game, "turnNumber");
                if (!driving && game.CurrentWalkerId == LocalId && game.TurnArmed && attempt != lastAttempt)
                {
                    lastAttempt = attempt;
                    StartCoroutine(Drive(game.DeathsOf(LocalId)));
                }
            }
            if (game != null && game.Phase == MinigamePhase.Results && !final)
            {
                final = true;
                Check(detonations == 3, "one brief mine contact per participant: " + detonations);
                var state = Get<MemoryRunState>(game, "state");
                string snapshot = "";
                for (int id = 0; id < 3; id++)
                {
                    Check(state.TryGet(id, out var p), "missing progress " + id);
                    Check(p.Deaths == 3, "three rejected attempts " + id + ": " + p.Deaths);
                    Check(p.Finished == (id < 2), "finish eligibility " + id);
                    Check(p.BestStep == (id < 2 ? config.Steps : config.Steps - 1), "step record " + id);
                    snapshot += $" {id}:{p.BestStep}/{p.Deaths}/{p.Finished}/{p.ArrivalOrder}";
                }
                Debug.Log("MEMORY_CONTACT FINAL" + snapshot);
                local.enabled = true;
                body.useGravity = true;
            }
            if (final && !returned && SceneManager.GetActiveScene().name == "Hub")
            {
                returned = true;
                Debug.Log("MEMORY_CONTACT RETURN passed=" + !failed + " id=" + LocalId);
                quitAt = Time.realtimeSinceStartup + (Server ? 5 : 1);
            }
            if (quitAt > 0 && Time.realtimeSinceStartup >= quitAt) Application.Quit(failed ? 2 : 0);
            if (Time.realtimeSinceStartup - started > Timeout)
            { Check(false, "timeout"); Application.Quit(2); }
        }

        private IEnumerator Drive(int scenario)
        {
            driving = true;
            Debug.Log("MEMORY_CONTACT ATTEMPT id=" + LocalId + " scenario=" + scenario);
            int count = scenario == 2 ? 3 : (scenario < 2 ? config.Steps - 1 : config.Steps);
            for (int step = 0; step < count && game.CurrentWalkerId == LocalId; step++)
            {
                Vector3 contact = config.CellCenter(step, step % MemoryRunConfig.LaneCount);
                yield return FlyTo(contact + Vector3.up * .7f);
                Put(contact);
                yield return new WaitForSeconds(.18f);
                Check(game.CurrentWalkerId != LocalId || Get<int>(game, "reportedStep") == step,
                    "owner sampled row " + step);
            }
            if (game.CurrentWalkerId == LocalId)
            {
                if (scenario == 0)
                {
                    int last = config.Steps - 1;
                    Vector3 mine = config.CellCenter(last, (last + 1) % MemoryRunConfig.LaneCount);
                    yield return FlyTo(mine + Vector3.up * .7f);
                    // One physics step on the mine, then immediately airborne again.
                    Put(mine);
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    Put(mine + Vector3.up * .7f);
                    Debug.Log("MEMORY_CONTACT BRIEF_MINE id=" + LocalId);
                }
                Vector3 target = scenario == 2 ? config.CellCenter(4, 1) :
                    new Vector3(0, config.PlateSurfaceY, config.ExitPadZ + .6f);
                yield return FlyTo(target + Vector3.up * .7f);
                if (game.CurrentWalkerId == LocalId) Put(target);
            }
            float deadline = Time.realtimeSinceStartup + 4;
            while (game.CurrentWalkerId == LocalId && game.Phase == MinigamePhase.Round &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Check(game.CurrentWalkerId != LocalId || game.Phase != MinigamePhase.Round, "attempt resolved");
            // Failure presentation owns the body until respawn; stop staging positions now.
            yield return new WaitForSeconds(4);
            if (local != null)
            {
                local.enabled = false;
                body.useGravity = false;
                body.linearVelocity = Vector3.zero;
            }
            driving = false;
        }

        private IEnumerator FlyTo(Vector3 target)
        {
            Vector3 origin = local.Position;
            float elapsed = 0;
            const float TravelSeconds = .65f;
            while (elapsed < TravelSeconds && game.CurrentWalkerId == LocalId)
            {
                elapsed += Time.fixedDeltaTime;
                Vector3 p = Vector3.Lerp(origin, target, Mathf.Clamp01(elapsed / TravelSeconds));
                p.y = target.y;
                Put(p);
                yield return new WaitForFixedUpdate();
            }
        }
        private void Put(Vector3 position)
        {
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.position = position;
        }
        private void Check(bool condition, string message)
        {
            if (condition) return;
            failed = true;
            Debug.LogError("MEMORY_CONTACT FAIL id=" + LocalId + " " + message);
        }
        private void OnMine(Vector3 center)
        {
            detonations++;
            Debug.Log("MEMORY_CONTACT MINE count=" + detonations);
        }
    }
}
