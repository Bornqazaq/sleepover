using System.Collections;
using Igruha.Core.Items;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Minigames.CarryItem;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    // Development-only, opt-in end-to-end check of real scene prefabs and NGO state:
    // one permanent cart per team fills at the tap, empties at the tank, four trips each.
    public sealed class CarryDeliveryNetworkProbe : MonoBehaviour
    {
        private const int Deliveries = 4;
        private const float Timeout = 150f;
        private CarryItemMinigame game;
        private bool failed;
        private bool sawPartialPump;

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
            bool tutorialReady = false;
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
                if (game != null && game.AwaitingTutorialReady && !tutorialReady)
                {
                    tutorialReady = true;
                    game.ToggleTutorialReady();
                }
                if (game != null && game.Phase == MinigamePhase.Round) break;
                yield return null;
            }
            if (game == null || game.Phase != MinigamePhase.Round)
            {
                Fail("round did not start");
                yield break;
            }

            // --bot can re-enable autopilot when a delayed roster arrives. It races
            // this probe for the cart, so this runner must own all deliveries.
            if (LaunchArguments.BotEnabled)
            {
                Fail("run without --bot");
                Application.Quit(1);
                yield break;
            }
            yield return new WaitForSeconds(5f);

            if (NetworkManager.Singleton.IsServer)
            {
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
                    var pumpingCart = game.CartOf(TeamSide.A);
                    if (pumpingCart != null && pumpingCart.IsPouring && pumpingCart.Water > 0 &&
                        pumpingCart.Water < game.Config.CartCapacity)
                    {
                        sawPartialPump = true;
                        var pump = game.TankOf(TeamSide.A).GetComponent<WaterPumpPresentation>();
                        if (pump == null || !pump.IsPumping) Fail("client pump presentation not active");
                    }
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
                if (!sawPartialPump) Fail("client never observed gradual pumping");
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
            var cart = game.CartOf(side);
            if (cart == null)
            {
                Fail($"no cart {side}");
                yield break;
            }
            cart.Carry.ResetPose(cart.HomePosition, cart.HomeRotation);
            float fillDeadline = Time.realtimeSinceStartup + game.Config.CartCapacity / game.Config.FillRate + 3f;
            while (cart.Water < game.Config.CartCapacity && Time.realtimeSinceStartup < fillDeadline)
                yield return null;
            if (cart.Water != game.Config.CartCapacity) { Fail("tap fill timeout " + side); yield break; }
            var tank = game.TankOf(side);
            if (number == 1)
            {
                // Close to storage, but not the pump: the old broad trigger accepted this side.
                cart.Carry.ResetPose(tank.transform.position + new Vector3(0f, 0.03f, -2.5f), Quaternion.identity);
                Physics.SyncTransforms();
                yield return new WaitForSeconds(0.5f);
                if (cart.Water != game.Config.CartCapacity || cart.IsPouring) Fail("pumping outside dock " + side);
            }
            cart.Carry.ResetPose(tank.DockPoint + Vector3.up * 0.03f, Quaternion.identity);
            Physics.SyncTransforms();
            if (number == 1)
            {
                yield return new WaitForSeconds(1f);
                if (!cart.IsPouring || cart.Water <= 0 || cart.Water >= game.Config.CartCapacity)
                    Fail("expected partial pumping after one second " + side);
                int remaining = cart.Water;
                int delivered = tank.Water;
                cart.Carry.ResetPose(tank.DockPoint + new Vector3(-3f, 0.03f, 0f), Quaternion.identity);
                Physics.SyncTransforms();
                yield return new WaitForSeconds(0.6f);
                if (cart.IsPouring || cart.Water != remaining || tank.Water != delivered)
                    Fail("leaving pump did not preserve remaining water " + side);
                cart.Carry.ResetPose(tank.DockPoint + Vector3.up * 0.03f, Quaternion.identity);
                Physics.SyncTransforms();
            }
            float deadline = Time.realtimeSinceStartup + game.Config.CartCapacity / game.Config.PourRate + 3f;
            while (cart != null && cart.Water > 0 && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (cart != null && cart.Water > 0) Fail("delivery timeout " + side);
            yield return new WaitForSeconds(0.5f);
            Check(side, number);
            // Back to the tap dock for the next trip, as a team would push it.
            cart.Carry.ResetPose(cart.HomePosition, cart.HomeRotation);
            yield return new WaitForSeconds(1f);
        }

        private void Check(TeamSide side, int number)
        {
            var state = game.State.Of(side);
            int expected = Mathf.Min(game.Config.TankCapacity, number * game.Config.CartCapacity);
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
