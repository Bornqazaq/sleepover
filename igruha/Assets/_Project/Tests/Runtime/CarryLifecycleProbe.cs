using System.Collections;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Minigames.CarryItem;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    // Opt-in development build check: real scene, permanent cart, carriers and NGO teardown.
    public sealed class CarryLifecycleProbe : MonoBehaviour
    {
        private const float Timeout = 100f;
        private string scenario;
        private CarryItemMinigame game;
        private WaterCart bottle;
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
                bottle = game.CartOf(side);
                Check(bottle != null, "cart spawned");
                if (bottle == null) { Finish(); yield break; }
                // Fill at the server directly and keep the fixture upright so incidental
                // slosh from the teleport/impact cannot mask the loss being tested.
                bottle.ChangeWater(game.Config.CartCapacity, WaterLossReason.Filled);
                var settings = bottle.Carry.Settings;
                settings.sloshPerDeltaSpeed = 0;
                bottle.Carry.Configure(settings);
                if (scenario != "void")
                    bottle.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.FreezeAll;
                var carrier = SessionScoreboard.Current.FindPlayer(carrierId).Avatar;
                carrier.RequestTeleport(bottle.Carry.StationOf(0), Quaternion.identity);
                yield return new WaitForSeconds(1);
                Check(bottle.Carry.TryGrab(carrier), "grab");
            }
            float carrierDeadline = Time.realtimeSinceStartup + 10;
            while (Time.realtimeSinceStartup < carrierDeadline)
            {
                bottle = game.CartOf(side);
                if (bottle != null && bottle.Carry.CarrierCount == 1 && bottle.Water == game.Config.CartCapacity) break;
                yield return null;
            }
            Check(bottle != null && bottle.Carry.CarrierCount == 1, "replicated carrier");
            if (failed) { Finish(); yield break; }
            bottle.WaterChanged += OnWaterSpent;
            Check(bottle.Water == game.Config.CartCapacity, "initial water");
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

            // shove: the carrier pushes the cart off and a quarter splashes out.
            // void: the server drops the cart below the pit floor; everything is lost
            // and the cart returns to its tap dock after the respawn delay.
            if (localId == carrierId && scenario == "shove")
                bottle.Carry.HandlePushButton(SessionScoreboard.Current.FindPlayer(carrierId).Avatar);
            if (server && scenario == "void")
                bottle.Carry.ResetPose(bottle.transform.position + Vector3.down * 40f, Quaternion.identity);

            int expectedLoss = scenario == "void"
                ? game.Config.CartCapacity
                : Mathf.FloorToInt(game.Config.CartCapacity * game.Config.ShoveLossFraction);
            float lossDeadline = Time.realtimeSinceStartup + 8;
            while (Time.realtimeSinceStartup < lossDeadline &&
                   (lossEvents == 0 || bottle.Water != game.Config.CartCapacity - expectedLoss))
                yield return null;
            yield return new WaitForSeconds(1);
            Check(bottle.Water == game.Config.CartCapacity - expectedLoss, "replicated water");
            Check(lossEvents == 1 && lostWater == expectedLoss, "one replicated loss event");
            Check(lossReason == (scenario == "void" ? WaterLossReason.Void : WaterLossReason.Shove), "loss reason");
            Check(bottle.Carry.CarrierCount == 0, "handle released");
            if (scenario == "void")
            {
                float returnDeadline = Time.realtimeSinceStartup + game.Config.CartRespawnSeconds + 4;
                while (Time.realtimeSinceStartup < returnDeadline && bottle.IsLost) yield return null;
                Check(!bottle.IsLost, "cart returned");
                Check(Vector3.Distance(bottle.transform.position, bottle.HomePosition) < 1.5f, "cart at dock");
            }
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
