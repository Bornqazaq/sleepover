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
    /// <summary>Opt-in development probe: real input consumers, scene objects, RPCs, latency and cleanup.</summary>
    public sealed class PingPongNetworkProbe : MonoBehaviour
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly PropertyInfo PushInput = typeof(PlayerInputReader).GetProperty("PushPressed");
        private PingPongTable table;
        private PlayerController body;
        private PlayerInputReader input;
        private PlayerPushAbility push;
        private PingPongSeat seat;
        private PingPongCamera tableView;
        private SkinnedMeshRenderer ownModel;
        private string scenario;
        private bool failed, autoplay, injectEarly, earlySent, earlyRetry, sawMiss, closing;
        private bool offline;
        private uint pressedFlight, loggedFlight;
        private PingPongPhase loggedPhase;
        private int side, localId;
        private float began;
        private int maxSolo, maxPair;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--pingpong-check", out _)) return;
            var go = new GameObject(nameof(PingPongNetworkProbe));
            DontDestroyOnLoad(go); go.AddComponent<PingPongNetworkProbe>();
        }

        private IEnumerator Start()
        {
            began = Time.realtimeSinceStartup;
            if (LaunchArguments.TryGetValue("--pingpong-check", out string requestedScenario)) scenario = requestedScenario;
            scenario ??= "solo-left";
            offline = NetworkManager.Singleton == null;
            localId = 0;
            float choiceAt = 0;
            while (Time.realtimeSinceStartup - began < 60)
            {
                var network = NetworkManager.Singleton;
                if (!offline && network != null && network.IsConnectedClient &&
                    SceneManager.GetActiveScene().name == "Hub")
                {
                    localId = (int)network.LocalClientId;
                    var selection = CharacterSelection.Current;
                    if (selection != null && !selection.HasChosen && Time.realtimeSinceStartup >= choiceAt)
                    {
                        choiceAt = Time.realtimeSinceStartup + 1;
                        selection.ReportReady();
                        for (int i = 0; i < 8; i++)
                        {
                            int index = (localId + i) % 8;
                            if (!selection.IsTaken(index)) { selection.Choose(index); break; }
                        }
                    }
                }
                body = SessionScoreboard.Current?.LocalPlayer?.Avatar;
                if (body != null && SceneManager.GetActiveScene().name == "Hub") break;
                yield return null;
            }
            Check(body != null, "local avatar spawned");
            if (failed) { Finish(); yield break; }
            table = FindFirstObjectByType<PingPongTable>();
            Check(table != null, "table present");
            if (failed) { Finish(); yield break; }
            side = offline && scenario == "solo-right" ? 1 : localId == 0 ? 0 : 1;
            seat = table.Seat(side);
            tableView = table.GetComponentInParent<PingPongCamera>();
            ownModel = body.GetComponentInChildren<SkinnedMeshRenderer>();
            input = body.GetComponent<PlayerInputReader>();
            push = body.GetComponent<PlayerPushAbility>();
            input.EngageAutopilot();
            foreach (DebugPlayerBot bot in body.GetComponents<DebugPlayerBot>()) bot.enabled = false;

            if (offline)
            {
                yield return Enter();
                autoplay = true;
                yield return Wait(() => maxSolo >= 26, 45, "solo rally through maximum difficulty");
                autoplay = false;
                yield return Leave();
                Check(table.State.Phase == PingPongPhase.Idle, "empty table reset");
                // A teleport must also release input ownership, without an E press.
                yield return Enter();
                body.TeleportTo(seat.StandPosition + Vector3.forward * 6, Quaternion.identity);
                yield return new WaitForSeconds(.4f);
                Check(!body.MovementLocked && push.ButtonOverride == null, "teleport cleanup");
                Check(tableView != null && !tableView.IsActive, "teleport restores camera");
                Finish(); yield break;
            }

            if (localId > 1)
            {
                yield return Wait(() => BothOccupied(), 40, "both seats occupied for observer");
                ulong first = table.Seat(0).Occupant, second = table.Seat(1).Occupant;
                body.TeleportTo(table.Seat(0).StandPosition + Vector3.forward, Quaternion.identity);
                yield return new WaitForSeconds(.5f);
                Check(!table.Seat(0).CanInteract(body) && !table.Seat(1).CanInteract(body), "third player cannot take occupied seats");
                Check(tableView != null && !tableView.IsActive, "observer retains ordinary camera");
                body.GetComponent<IInteractionRelay>()?.TryRelayInteract(table.Seat(0).gameObject);
                yield return new WaitForSeconds(.5f);
                Check(table.Seat(0).Occupant == first && table.Seat(1).Occupant == second, "third player's RPC did not replace owners");
                body.TeleportTo(new Vector3(0, 0, -2), Quaternion.identity);
                yield return WaitForSceneCleanup();
                Finish(); yield break;
            }

            if (localId == 1) yield return Wait(() => table.State.Flight.Rally >= 4, 35, "host solo before joining");
            yield return Enter();
            autoplay = true;
            injectEarly = localId == 1;
            // A host shutdown may intentionally discard its last unsent flight. The client
            // arms its disconnect assertion one exchange earlier instead of waiting for it.
            int requiredPair = scenario == "flow" ? 26 : scenario == "host-exit" && localId == 1 ? 6 : 8;
            yield return Wait(() => sawMiss && maxPair >= requiredPair, 60, "pair rally after intentional early miss");
            Check(sawMiss, "miss replicated");
            if (failed) { Finish(); yield break; }

            if (scenario == "knockdown")
            {
                if (localId == 1)
                {
                    autoplay = false;
                    body.Knockdown(KnockdownType.FallForward);
                    yield return Wait(() => !seat.IsLocal && !body.MovementLocked && push.ButtonOverride == null,
                        6, "client knockdown releases seat and input");
                    yield return null;
                    Check(tableView != null && !tableView.IsActive, "knockdown restores camera");
                    // Stay connected while the host verifies the automatic replacement.
                    yield return new WaitForSeconds(9);
                    Finish(); yield break;
                }
                yield return Wait(() => table.Seat(1).Occupant == HubActivityStation.NoOccupant,
                    10, "knocked-down client's seat freed on server");
                maxSolo = 0;
                yield return Wait(() => maxSolo >= 4, 18, "automatic partner after knockdown");
                yield return Leave();
                Finish(); yield break;
            }
            if (scenario == "disconnect")
            {
                if (localId == 1)
                {
                    closing = true; autoplay = false;
                    Debug.Log("PINGPONG_CHECK DISCONNECT client");
                    NetworkManager.Singleton.Shutdown();
                    yield return new WaitForSeconds(2);
                    Finish(); yield break;
                }
                yield return Wait(() => table.Seat(1).Occupant == HubActivityStation.NoOccupant, 12, "disconnected seat freed");
                maxSolo = 0;
                yield return Wait(() => maxSolo >= 4, 18, "automatic partner after disconnect");
                yield return Leave();
                Finish(); yield break;
            }
            if (scenario == "host-exit")
            {
                if (localId == 0)
                {
                    autoplay = false;
                    closing = true;
                    Debug.Log("PINGPONG_CHECK DISCONNECT host");
                    NetworkManager.Singleton.Shutdown();
                }
                yield return Wait(() => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, 15, "host departure shuts down session");
                autoplay = false;
                yield return new WaitForSeconds(1);
                Check(body == null || push == null || push.ButtonOverride == null, "shutdown releases push override");
                Check(tableView == null || !tableView.IsActive, "shutdown releases table camera");
                Finish(); yield break;
            }

            // Exit, automatic replacement, rejoin, then leave Hub through the normal loader.
            if (localId == 1)
            {
                autoplay = false;
                yield return Leave();
                yield return Wait(() => table.State.Flight.Rally >= 4, 25, "solo resumes after voluntary exit");
                yield return Enter();
                autoplay = true;
            }
            else
            {
                yield return Wait(() => !BothOccupied(), 12, "client voluntarily left");
                yield return Wait(() => BothOccupied(), 30, "client rejoined");
                yield return Wait(() => table.State.Flight.Rally >= 4, 18, "rally after rejoin");
                Check(body.MovementLocked, "scene switch while still seated");
                var hub = FindFirstObjectByType<HubController>();
                var catalog = (MinigameCatalog)typeof(HubController).GetField("catalog", Private).GetValue(hub);
                var loader = (MinigameLoader)typeof(HubController).GetField("loader", Private).GetValue(hub);
                Check(loader.TryLoad(catalog.Get(catalog.IndexOfScene("Stopwatch"))), "normal minigame loader started");
            }
            yield return WaitForSceneCleanup();
            Finish();
        }

        private IEnumerator WaitForSceneCleanup()
        {
            yield return Wait(() => table == null && SceneManager.GetActiveScene().name != "Hub", 90, "hub unloaded");
            autoplay = false;
            var game = MinigameControllerBase.Current;
            float deadline = Time.realtimeSinceStartup + 35;
            bool ready = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                game = MinigameControllerBase.Current;
                if (game != null && game.AwaitingTutorialReady && !ready) { game.ToggleTutorialReady(); ready = true; }
                // Practice is also GameplayActive. Wait through its scene reload into the actual round.
                if (game != null && game.Phase == MinigamePhase.Round && !game.StartCountdownActive) break;
                yield return null;
            }
            yield return new WaitForSeconds(.3f);
            game = MinigameControllerBase.Current;
            Check(game != null && game.Phase == MinigamePhase.Round, "entered main minigame");
            Check(body != null && !body.MovementLocked && push.ButtonOverride == null, "movement and punch restored in minigame");
            Check(tableView == null || !tableView.IsActive, "minigame has no table camera override");
        }

        private IEnumerator Enter()
        {
            body.TeleportTo(seat.StandPosition, seat.StandRotation);
            input.DriveMove(Vector2.zero);
            yield return new WaitForSeconds(.7f);
            var interactor = body.GetComponent<PlayerInteractor>();
            Check(ReferenceEquals(interactor.CurrentInteractable, seat), "normal E targets the seat");
            input.DriveInteract();
            yield return Wait(() => seat.IsLocal && body.MovementLocked, 6, "seat binding");
            Check(ReferenceEquals(push.ButtonOverride, seat), "LMB overrides punch");
            yield return Wait(() => tableView != null && tableView.IsActive && tableView.ViewSide == side,
                3, "first-person table camera active on correct side");
            Check(ownModel != null && ownModel.forceRenderingOff, "own head hidden in first-person view");
        }

        private IEnumerator Leave()
        {
            input.DriveInteract();
            yield return Wait(() => !seat.IsLocal, 6, "E release");
            yield return null;
            Check(!body.MovementLocked && push.ButtonOverride == null, "release restores movement and punch");
            Check(tableView != null && !tableView.IsActive, "E restores ordinary camera");
            Check(ownModel != null && !ownModel.forceRenderingOff, "E restores own model");
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup - began > 210 && !closing) { Check(false, "probe timeout"); Finish(); return; }
            if (table == null) return;
            PingPongState state = table.State;
            if (state.Flight.Id != loggedFlight || state.Phase != loggedPhase)
            {
                loggedFlight = state.Flight.Id; loggedPhase = state.Phase;
                string record = string.Format(CultureInfo.InvariantCulture,
                    "PINGPONG_STATE {0} {1} {2} {3:F5} {4:F5} {5} {6:F4} {7:F4}",
                    state.Flight.Id, (byte)state.Phase, state.Flight.Target, state.Flight.StartsAt,
                    state.Flight.Duration, state.Flight.Rally, state.Flight.From.z, state.Flight.To.z);
                Debug.Log(record);
            }
            if (BothOccupied()) maxPair = Math.Max(maxPair, state.Flight.Rally);
            else maxSolo = Math.Max(maxSolo, state.Flight.Rally);
            if (state.Phase == PingPongPhase.Missed && BothOccupied()) sawMiss = true;
            if (!autoplay || input == null || !seat.IsLocal || state.Phase != PingPongPhase.Playing || state.Flight.Target != side) return;
            double now = NetworkClock.Now;
            if (now < state.Flight.StartsAt) return;
            if (injectEarly && !earlySent && BothOccupied() && state.Flight.Rally >= 4)
            {
                earlySent = true; pressedFlight = state.Flight.Id;
                PushInput.SetValue(input, true);
                Debug.Log("PINGPONG_CHECK EARLY_SENT");
                return;
            }
            if (pressedFlight == state.Flight.Id)
            {
                if (earlySent && !earlyRetry && now >= state.Flight.ContactAt - .1)
                {
                    earlyRetry = true; PushInput.SetValue(input, true);
                    Debug.Log("PINGPONG_CHECK EARLY_RETRY_REJECTED_EXPECTED");
                }
                return;
            }
            if (now >= state.Flight.ContactAt - PingPongRules.EarlyWindow(state.Flight.Rally) * .4f &&
                now < state.Flight.ContactAt + PingPongRules.LateWindow(state.Flight.Rally))
            {
                pressedFlight = state.Flight.Id;
                PushInput.SetValue(input, true);
            }
        }

        private bool BothOccupied() => table != null && table.Seat(0).Occupant != HubActivityStation.NoOccupant &&
            table.Seat(1).Occupant != HubActivityStation.NoOccupant;

        private IEnumerator Wait(Func<bool> condition, float seconds, string message)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Check(condition(), message);
        }

        private void Check(bool condition, string message)
        {
            Debug.Log("PINGPONG_CHECK " + (condition ? "PASS " : "FAIL ") + message);
            failed |= !condition;
        }

        private void Finish()
        {
            if (closing && !enabled) return;
            Debug.Log("PINGPONG_CHECK COMPLETE passed=" + !failed + " solo=" + maxSolo + " pair=" + maxPair);
            enabled = false;
            autoplay = false;
            if (!Application.isEditor) StartCoroutine(Quit());
        }
        private IEnumerator Quit()
        {
            yield return new WaitForSecondsRealtime(localId == 0 ? 4 : .5f);
            Application.Quit(failed ? 2 : 0);
        }
    }
}
