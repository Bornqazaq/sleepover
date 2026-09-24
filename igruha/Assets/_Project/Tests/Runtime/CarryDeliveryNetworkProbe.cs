using System.Collections;
using Igruha.Core.Items;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Minigames.CarryItem;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    // Development-only, opt-in end-to-end check of real scene prefabs and NGO state.
    public sealed class CarryDeliveryNetworkProbe : MonoBehaviour
    {
        private const int Deliveries = 5;
        private const float Timeout = 150f;
        private CarryItemMinigame game;
        private bool failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--carry-delivery-check", out _)) return;
            var root = new GameObject(nameof(CarryDeliveryNetworkProbe));
            DontDestroyOnLoad(root);
            root.AddComponent<CarryDeliveryNetworkProbe>();
        }

        private IEnumerator Start()
        {
            float deadline = Time.realtimeSinceStartup + Timeout;
            bool requestedCharacter = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                var selection = CharacterSelection.Current;
                var network = NetworkManager.Singleton;
                if (!requestedCharacter && selection != null && network != null && network.IsConnectedClient &&
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
                {
                    requestedCharacter = true;
                    selection.ReportReady();
                    selection.Choose((int)network.LocalClientId);
                    Debug.Log("CARRY_DELIVERY_CHECK selected character in Hub id=" + network.LocalClientId);
                }
                game = MinigameControllerBase.Current as CarryItemMinigame;
                if (game != null && game.Phase == MinigamePhase.Round) break;
                yield return null;
            }
            if (game == null || game.Phase != MinigamePhase.Round)
            {
                Fail("round did not start");
                yield break;
            }

            foreach (var bot in FindObjectsByType<CarryItemDebugBot>(FindObjectsSortMode.None))
                bot.enabled = false;
            yield return new WaitForSeconds(5f);

            if (NetworkManager.Singleton.IsServer)
            {
                game.StackOf(TeamSide.A).ClearLiveBottle();
                game.StackOf(TeamSide.B).ClearLiveBottle();
                for (int i = 1; i <= Deliveries && !failed; i++)
                {
                    yield return Deliver(TeamSide.A, i);
                    yield return Deliver(TeamSide.B, i);
                }
            }
            else
            {
                int lastA = 0;
                int lastB = 0;
                while (Time.realtimeSinceStartup < deadline)
                {
                    var state = game.State;
                    if (state.TeamA.Deliveries != lastA)
                    {
                        lastA = state.TeamA.Deliveries;
                        Check(TeamSide.A, lastA);
                    }
                    if (state.TeamB.Deliveries != lastB)
                    {
                        lastB = state.TeamB.Deliveries;
                        Check(TeamSide.B, lastB);
                    }
                    if (lastA == Deliveries && lastB == Deliveries) break;
                    yield return null;
                }
            }

            Check(TeamSide.A, Deliveries);
            Check(TeamSide.B, Deliveries);
            if (!failed)
                Debug.Log("CARRY_DELIVERY_CHECK PASS role=" +
                    (NetworkManager.Singleton.IsServer ? "host" : "client") +
                    " id=" + NetworkManager.Singleton.LocalClientId);
            yield return new WaitForSeconds(5f);
            Application.Quit(failed ? 1 : 0);
        }

        private IEnumerator Deliver(TeamSide side, int number)
        {
            var bottle = game.StackOf(side).Dispense(null);
            if (bottle == null) { Fail("dispense " + side); yield break; }
            bottle.Carry.enabled = false;
            bottle.enabled = false;
            var body = bottle.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.linearVelocity = Vector3.zero;
            body.position = game.TankOf(side).transform.position + new Vector3(-2.5f, 0.15f, 0f);
            Physics.SyncTransforms();
            float deadline = Time.realtimeSinceStartup + game.Config.PourSeconds + 3f;
            while (bottle != null && !bottle.IsGone && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (bottle != null && !bottle.IsGone) Fail("delivery timeout " + side);
            Check(side, number);
            yield return new WaitForSeconds(1f);
        }

        private void Check(TeamSide side, int number)
        {
            var state = game.State.Of(side);
            int expected = Mathf.Min(game.Config.TankCapacity, number * game.Config.BottleCapacity);
            if (state.Deliveries != number || state.Water != expected || game.TankOf(side).Water != expected)
                Fail($"{side} expected={expected}/{number} actual={state.Water}/{state.Deliveries} tank={game.TankOf(side).Water}");
            else
                Debug.Log($"CARRY_DELIVERY_CHECK step={number} team={side} water={state.Water} id={NetworkManager.Singleton.LocalClientId}");
        }

        private void Fail(string reason)
        {
            failed = true;
            Debug.LogError("CARRY_DELIVERY_CHECK FAIL " + reason);
        }
    }
}
