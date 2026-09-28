using System.Collections;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Minigames.CarryItem;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    // Opt-in development build check: real scene, bottle, carriers and NGO teardown.
    public sealed class CarryLifecycleProbe : MonoBehaviour
    {
        private const float Timeout = 100f;
        private string scenario;
        private CarryItemMinigame game;
        private WaterBottle bottle;
        private int localId, lossEvents, lostWater;
        private WaterLossReason lossReason;
        private bool server, failed, closing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--carry-lifecycle-check", out _)) return;
            var root = new GameObject(nameof(CarryLifecycleProbe));
            DontDestroyOnLoad(root);
            root.AddComponent<CarryLifecycleProbe>();
        }

        private IEnumerator Start()
        {
            LaunchArguments.TryGetValue("--carry-lifecycle-check", out scenario);
            float deadline = Time.realtimeSinceStartup + Timeout;
            float nextChoice = 0;
            bool ready = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                var network = NetworkManager.Singleton;
                var selection = CharacterSelection.Current;
                if (network != null && network.IsConnectedClient && selection != null && !selection.HasChosen &&
                    Time.realtimeSinceStartup >= nextChoice &&
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
                {
                    nextChoice = Time.realtimeSinceStartup + 1;
                    selection.ReportReady();
                    for (int i = 0; i < 8; i++)
                    {
                        int index = ((int)network.LocalClientId + i) % 8;
                        if (!selection.IsTaken(index)) { selection.Choose(index); break; }
                    }
                }
                game = MinigameControllerBase.Current as CarryItemMinigame;
                if (game != null && game.AwaitingTutorialReady && !ready)
                { ready = true; game.ToggleTutorialReady(); }
                if (game != null && game.Phase == MinigamePhase.Round) break;
                yield return null;
            }
            if (game == null || game.Phase != MinigamePhase.Round)
            { Check(false, "round timeout"); Finish(); yield break; }

            var manager = NetworkManager.Singleton;
            localId = (int)manager.LocalClientId;
            server = manager.IsServer;
            int carrierId = scenario == "host-held-quit" ? 0 : 1;
            yield return new WaitForSeconds(game.Config.CountdownSeconds + 1);
            // The phase can arrive before the roster on a client.
            while (game.TeamOfPlayer(carrierId) == TeamSide.None && Time.realtimeSinceStartup < deadline)
                yield return null;
            TeamSide side = game.TeamOfPlayer(carrierId);
            Check(side != TeamSide.None, "roster received");
            if (failed) { Finish(); yield break; }

            if (server)
            {
                bottle = game.StackOf(side).Dispense(null);
                Check(bottle != null, "dispense");
                if (bottle == null) { Finish(); yield break; }
                // Keep this fixture upright so incidental tilt/impact cannot mask the loss being tested.
                var settings = bottle.Carry.Settings;
                settings.tiltFromSupportLoss = 0;
                settings.tiltFromTorque = 0;
                bottle.Carry.Configure(settings);
                bottle.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.FreezeAll;
                var carrier = SessionScoreboard.Current.FindPlayer(carrierId).Avatar;
                carrier.RequestTeleport(bottle.Carry.StationOf(0), Quaternion.identity);
                yield return new WaitForSeconds(1);
                Check(bottle.Carry.TryGrab(carrier), "grab");
            }
            float carrierDeadline = Time.realtimeSinceStartup + 10;
            while (Time.realtimeSinceStartup < carrierDeadline)
            {
                bottle = game.StackOf(side).LiveBottle;
                if (bottle != null && bottle.Carry.CarrierCount == 1) break;
                yield return null;
            }
            Check(bottle != null && bottle.Carry.CarrierCount == 1, "replicated carrier");
            if (failed) { Finish(); yield break; }
            bottle.WaterSpent += OnWaterSpent;
            Check(bottle.Water == game.Config.BottleCapacity, "initial water");
            Debug.Log($"CARRY_LIFECYCLE READY scenario={scenario} id={localId} carrier={carrierId}");
            yield return new WaitForSeconds(3);

            bool hostExit = scenario.StartsWith("host-");
            if ((hostExit && server) || (scenario == "client-exit" && localId == carrierId))
            {
                closing = true;
                Check(bottle.Carry.CarrierCount == 1, "still carrying at shutdown");
                if (scenario.EndsWith("quit"))
                {
                    // The runner also checks process exit and exceptions after this marker.
                    Finish();
                    yield break;
                }
                manager.Shutdown();
                yield return new WaitForSeconds(2);
                Check(!manager.IsListening, "network stopped");
                Check(lossEvents == 0, "no loss on closing peer");
                Finish();
                yield break;
            }

            if (hostExit)
            {
                float disconnectDeadline = Time.realtimeSinceStartup + 10;
                while (manager.IsListening && Time.realtimeSinceStartup < disconnectDeadline)
                    yield return null;
                Check(!manager.IsListening, "host disconnect received");
                Check(lossEvents == 0, "no teardown loss event");
                Finish();
                yield break;
            }

            if (server && scenario == "drop")
                bottle.Carry.Interact(SessionScoreboard.Current.FindPlayer(carrierId).Avatar);
            if (localId == carrierId && scenario == "throw")
                bottle.Carry.HandlePushButton(SessionScoreboard.Current.FindPlayer(carrierId).Avatar);

            int expectedLoss = scenario == "throw"
                ? Mathf.FloorToInt(game.Config.BottleCapacity * game.Config.ThrowLossFraction)
                : game.Config.DropLoss;
            float lossDeadline = Time.realtimeSinceStartup + 8;
            while (Time.realtimeSinceStartup < lossDeadline &&
                   (lossEvents == 0 || bottle.Water != game.Config.BottleCapacity - expectedLoss))
                yield return null;
            yield return new WaitForSeconds(1);
            Check(bottle.Water == game.Config.BottleCapacity - expectedLoss, "replicated water");
            Check(lossEvents == 1 && lostWater == expectedLoss, "one replicated loss event");
            Check(lossReason == (scenario == "throw" ? WaterLossReason.Throw : WaterLossReason.Drop), "loss reason");
            Check(bottle.Carry.CarrierCount == 0, "handle released");
            Check(manager.IsListening && game.Phase == MinigamePhase.Round, "round continues");
            if (scenario == "client-exit" && server)
                Check(manager.ConnectedClientsIds.Count == 3, "other clients remain");
            Debug.Log($"CARRY_LIFECYCLE STATE id={localId} water={bottle.Water} events={lossEvents} reason={lossReason}");
            // Let clients verify the live state before the host closes.
            yield return new WaitForSeconds(server ? 3 : 1);
            Finish();
        }

        private void OnWaterSpent(int amount, WaterLossReason reason)
        {
            lossEvents++;
            lostWater += amount;
            lossReason = reason;
            Check(!closing, "water loss during shutdown");
        }

        private void Check(bool condition, string message)
        {
            if (condition) return;
            failed = true;
            Debug.LogError($"CARRY_LIFECYCLE FAIL scenario={scenario} id={localId} {message}");
        }

        private void Finish()
        {
            if (!failed) Debug.Log($"CARRY_LIFECYCLE PASS scenario={scenario} id={localId}");
            Application.Quit(failed ? 1 : 0);
        }
    }
}
