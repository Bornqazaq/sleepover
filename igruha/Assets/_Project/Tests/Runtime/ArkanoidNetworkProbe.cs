using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using Igruha.Core.Hub;
using Igruha.Core.Hub.Activities;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.Tests
{
    /// <summary>Opt-in development check. Uses normal interaction and input consumers over real NGO peers.</summary>
    public sealed class ArkanoidNetworkProbe : MonoBehaviour
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly PropertyInfo PushInput = typeof(PlayerInputReader).GetProperty("PushPressed");
        private ArkanoidStation station;
        private ArkanoidCamera view;
        private PlayerController body;
        private PlayerInputReader input;
        private PlayerPushAbility push;
        private SkinnedMeshRenderer model;
        private bool failed, autoplay, lose, closing, offline;
        private int localId;
        private float began, nextPress;
        private uint loggedTick, loggedSession;
        private ArkanoidPhase loggedPhase;
        private string scenario;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--arkanoid-check", out _)) return;
            var go = new GameObject(nameof(ArkanoidNetworkProbe));
            DontDestroyOnLoad(go); go.AddComponent<ArkanoidNetworkProbe>();
        }

        private IEnumerator Start()
        {
            began = Time.realtimeSinceStartup;
            LaunchArguments.TryGetValue("--arkanoid-check", out scenario);
            scenario ??= "solo";
            offline = Application.isEditor && !WorldAuthority.IsNetworkSession;
            float selectionAt = 0;
            while (Time.realtimeSinceStartup - began < 60)
            {
                var network = NetworkManager.Singleton;
                if (!offline && network != null && network.IsConnectedClient && SceneManager.GetActiveScene().name == "Hub")
                {
                    localId = (int)network.LocalClientId;
                    var selection = CharacterSelection.Current;
                    if (selection != null && !selection.HasChosen && Time.realtimeSinceStartup >= selectionAt)
                    {
                        selectionAt = Time.realtimeSinceStartup + 1;
                        selection.ReportReady();
                        for (int i = 0; i < 8; i++)
                            if (!selection.IsTaken((localId + i) % 8)) { selection.Choose((localId + i) % 8); break; }
                    }
                }
                body = SessionScoreboard.Current?.LocalPlayer?.Avatar;
                if (offline)
                    foreach (var candidate in FindObjectsByType<PlayerInputReader>(FindObjectsSortMode.None))
                        if (candidate.LocallyControlled) { body = candidate.GetComponent<PlayerController>(); break; }
                if (body != null && SceneManager.GetActiveScene().name == "Hub") break;
                yield return null;
            }
            Check(body != null, "avatar spawned");
            if (failed) { Finish(); yield break; }
            station = FindFirstObjectByType<ArkanoidStation>();
            view = FindFirstObjectByType<ArkanoidCamera>();
            Check(station != null && view != null, "cabinet present");
            if (failed) { Finish(); yield break; }
            input = body.GetComponent<PlayerInputReader>(); push = body.GetComponent<PlayerPushAbility>();
            model = body.GetComponentInChildren<SkinnedMeshRenderer>();
            input.EngageAutopilot();
            foreach (var bot in FindObjectsByType<DebugPlayerBot>(FindObjectsSortMode.None)) bot.enabled = false;

            if (localId == 0)
            {
                yield return Enter(); autoplay = true;
                yield return Wait(() => station.State.Score >= 80, 55, "host breaks bricks through normal input");
                if (offline)
                {
                    yield return LossAndRestart();
                    yield return Leave();
                    yield return Enter();
                    body.TeleportTo(station.StandPosition + Vector3.forward * 6, Quaternion.identity);
                    yield return Wait(() => !station.IsLocal && !view.IsActive, 5, "teleport restores camera");
                    Check(!body.MovementLocked && push.ButtonOverride == null, "teleport restores input");
                    Finish(); yield break;
                }
                // Keep the host game visible long enough for a deliberately late observer to join.
                yield return new WaitForSeconds(18);
                if (scenario == "host-exit")
                {
                    autoplay = false; NetworkManager.Singleton.Shutdown();
                    yield return new WaitForSeconds(2);
                    Check(push == null || push.ButtonOverride == null, "host shutdown restores input");
                    Check(view == null || !view.IsActive, "host shutdown restores camera");
                    Finish(); yield break;
                }
                yield return Leave();
                yield return Wait(() => station.Occupant == 1, 20, "client takes cabinet");
                Check(!view.IsActive && !body.MovementLocked, "host spectator keeps ordinary controls");
                if (scenario == "disconnect")
                {
                    yield return Wait(() => station.Occupant == HubActivityStation.NoOccupant, 65, "disconnect frees cabinet on host");
                    yield return Enter(); autoplay = true;
                    yield return Wait(() => station.State.Score > 0, 20, "cabinet playable after disconnect");
                    yield return Leave(); Finish(); yield break;
                }
                yield return Wait(() => station.State.Session >= 5 && station.Occupant == 1, 95, "client completed restart and reentered");
                yield return new WaitForSeconds(2);
                var hub = FindFirstObjectByType<HubController>();
                var catalog = (MinigameCatalog)typeof(HubController).GetField("catalog", Private).GetValue(hub);
                var loader = (MinigameLoader)typeof(HubController).GetField("loader", Private).GetValue(hub);
                Check(loader.TryLoad(catalog.Get(catalog.IndexOfScene("Stopwatch"))), "normal minigame loader started");
            }
            else
            {
                yield return Wait(() => station.Occupant != HubActivityStation.NoOccupant, 40, "spectator receives occupied cabinet");
                Check(!view.IsActive, "spectator camera untouched");
                ulong owner = station.Occupant;
                body.TeleportTo(station.StandPosition + station.transform.right, Quaternion.identity);
                yield return new WaitForSeconds(.5f);
                Check(!station.CanInteract(body), "occupied cabinet unavailable");
                body.GetComponent<IInteractionRelay>()?.TryRelayInteract(station.gameObject);
                // Exercise the same server gate with an actual RPC from a non-owner.
                typeof(ArkanoidStation).GetMethod("PlayRpc", Private).Invoke(station,
                    new object[] { .8f, station.State.Session, default(RpcParams) });
                yield return new WaitForSeconds(.5f);
                Check(station.Occupant == owner, "invalid interaction and play cannot replace owner");
                body.TeleportTo(new Vector3(0, 0, -2), Quaternion.identity);
                if (scenario == "host-exit")
                {
                    yield return Wait(() => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, 90, "host departure shuts down client");
                    Check(view == null || !view.IsActive, "client camera clean after host departure");
                    Finish(); yield break;
                }
                if (localId == 1)
                {
                    yield return Wait(() => station.State.Session >= 2 && station.Occupant == HubActivityStation.NoOccupant, 80, "host leaves");
                    yield return Enter(); autoplay = true;
                    yield return Wait(() => station.State.Score >= 60, 55, "client breaks bricks through RPC input");
                    if (scenario == "disconnect")
                    {
                        autoplay = false; NetworkManager.Singleton.Shutdown();
                        yield return new WaitForSeconds(2);
                        Check(push == null || push.ButtonOverride == null, "client shutdown restores input");
                        Finish(); yield break;
                    }
                    yield return LossAndRestart();
                    autoplay = false;
                    body.Knockdown(KnockdownType.FallForward);
                    yield return Wait(() => !station.IsLocal && !view.IsActive, 8, "knockdown frees cabinet and camera");
                    Check(!body.MovementLocked && push.ButtonOverride == null, "knockdown restores input");
                    yield return Wait(() => !body.IsKnockedDown, 8, "stood up");
                    yield return Enter(); autoplay = true;
                }
            }
            yield return Wait(() => station == null && SceneManager.GetActiveScene().name != "Hub", 120, "hub unloaded");
            autoplay = false;
            float readyUntil = Time.realtimeSinceStartup + 40;
            bool ready = false;
            while (Time.realtimeSinceStartup < readyUntil)
            {
                var game = MinigameControllerBase.Current;
                if (game != null && game.AwaitingTutorialReady && !ready) { game.ToggleTutorialReady(); ready = true; }
                if (game != null && game.Phase == MinigamePhase.Round && !game.StartCountdownActive) break;
                yield return null;
            }
            Check(body != null && !body.MovementLocked && push.ButtonOverride == null, "main game has movement and punch");
            Check(view == null || !view.IsActive, "main game has no arcade camera override");
            Finish();
        }

        private IEnumerator LossAndRestart()
        {
            lose = true;
            yield return Wait(() => station.State.Phase == ArkanoidPhase.GameOver, 45, "three misses reach game over");
            autoplay = false; lose = false;
            yield return new WaitForSeconds(.3f);
            PushInput.SetValue(input, true);
            yield return Wait(() => station.State.Phase == ArkanoidPhase.Playing && station.State.Score == 0 &&
                station.State.Lives == 3 && station.State.Level == 1, 6, "click restarts score lives and level");
        }

        private IEnumerator Enter()
        {
            body.TeleportTo(station.StandPosition, station.StandRotation);
            input.DriveMove(Vector2.zero);
            yield return new WaitForSeconds(.7f);
            Check(ReferenceEquals(body.GetComponent<PlayerInteractor>().CurrentInteractable, station), "normal E targets arcade");
            input.DriveInteract();
            yield return Wait(() => station.IsLocal && body.MovementLocked, 6, "station binding");
            yield return Wait(() => view.ReadyForInput, 4, "smooth camera reached screen");
            Check(ReferenceEquals(push.ButtonOverride, station), "LMB intercepted");
            Check(model != null && model.forceRenderingOff, "own model hidden");
            Check(station.InteractionPrompt == string.Empty, "no prompt obscures screen");
        }

        private IEnumerator Leave()
        {
            autoplay = false; input.DriveMove(Vector2.zero); input.DriveInteract();
            yield return Wait(() => !station.IsLocal && !view.IsActive, 6, "E returns camera");
            Check(!body.MovementLocked && push.ButtonOverride == null, "E restores controls");
            Check(model != null && !model.forceRenderingOff, "E restores own model");
        }

        private void Update()
        {
            if (closing) return;
            if (Time.realtimeSinceStartup - began > 260) { Check(false, "timeout"); Finish(); return; }
            if (station == null) return;
            var s = station.State;
            if (s.Tick != loggedTick || s.Session != loggedSession || s.Phase != loggedPhase)
            {
                loggedTick = s.Tick; loggedSession = s.Session; loggedPhase = s.Phase;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "ARKANOID_STATE {0} {1} {2} {3} {4} {5} {6} {7:F4} {8:F4} {9:F4}",
                    s.Session, s.Tick, (byte)s.Phase, s.Bricks, s.Score, s.Lives, s.Level, s.Ball.x, s.Ball.y, s.Paddle));
            }
            if (!autoplay || !station.IsLocal || !view.ReadyForInput) return;
            if (s.Phase == ArkanoidPhase.Ready && Time.realtimeSinceStartup >= nextPress)
            {
                nextPress = Time.realtimeSinceStartup + .6f;
                PushInput.SetValue(input, true);
            }
            float landing = s.Ball.x;
            if (s.Velocity.y < -.01f)
            {
                float seconds = (ArkanoidRules.PaddleY + ArkanoidRules.BallRadius + ArkanoidRules.PaddleHeight * .5f - s.Ball.y) / s.Velocity.y;
                float edge = ArkanoidRules.HalfWidth - ArkanoidRules.BallRadius;
                landing = Mathf.PingPong(s.Ball.x + s.Velocity.x * seconds + edge, edge * 2) - edge;
            }
            float target = lose ? (landing < 0 ? ArkanoidRules.PaddleLimit : -ArkanoidRules.PaddleLimit) :
                landing + Mathf.Sin(Time.realtimeSinceStartup * .9f) * .055f;
            input.DriveMove(new Vector2(Mathf.Clamp((target - station.LocalPaddle) * 12, -1, 1), 0));
        }

        private IEnumerator Wait(Func<bool> condition, float seconds, string message)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Check(condition(), message);
        }
        private void Check(bool value, string message)
        {
            Debug.Log("ARKANOID_CHECK " + (value ? "PASS " : "FAIL ") + message); failed |= !value;
        }
        private void Finish()
        {
            if (closing) return;
            closing = true; autoplay = false;
            Debug.Log("ARKANOID_CHECK COMPLETE passed=" + !failed);
            if (!Application.isEditor) StartCoroutine(Quit());
        }
        private IEnumerator Quit()
        {
            yield return new WaitForSecondsRealtime(localId == 0 ? 4 : .5f);
            Application.Quit(failed ? 2 : 0);
        }
    }
}
