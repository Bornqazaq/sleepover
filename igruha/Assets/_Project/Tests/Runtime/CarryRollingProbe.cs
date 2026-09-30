using System.Collections;
using System.Collections.Generic;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.CarryItem;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Opt-in real input + real floor regression: 1–4 holders, full cart, host/client.</summary>
    public sealed class CarryRollingProbe : MonoBehaviour
    {
        private CarryItemMinigame game;
        private WaterCart cart;
        private PlayerController local;
        private PlayerInputReader reader;
        private readonly List<SessionPlayer> team = new List<SessionPlayer>();
        private bool driving, holding, failed;
        private int localSlot = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--carry-rolling-check", out _)) return;
            var root = new GameObject(nameof(CarryRollingProbe)); DontDestroyOnLoad(root);
            root.AddComponent<CarryRollingProbe>();
        }

        private void Update()
        {
            if (reader == null) return;
            reader.DriveInteractHold(holding);
            reader.DriveMove(driving ? local.WorldToMoveInput(Vector3.right) : Vector2.zero);
        }

        private IEnumerator Start()
        {
            float deadline = Time.realtimeSinceStartup + 100f;
            bool ready = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                var nm = NetworkManager.Singleton;
                var selection = CharacterSelection.Current;
                if (nm != null && nm.IsConnectedClient && selection != null && !selection.HasChosen &&
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
                {
                    selection.ReportReady();
                    for (int i = 0; i < 8; i++)
                    {
                        int slot = ((int)nm.LocalClientId + i) % 8;
                        if (!selection.IsTaken(slot)) { selection.Choose(slot); break; }
                    }
                }
                game = MinigameControllerBase.Current as CarryItemMinigame;
                if (game != null && game.AwaitingTutorialReady && !ready)
                { ready = true; game.ToggleTutorialReady(); }
                if (game != null && game.Phase == MinigamePhase.Round && !game.StartCountdownActive) break;
                yield return null;
            }
            if (game == null || game.Phase != MinigamePhase.Round)
            { Check(false, "round timeout"); Application.Quit(1); yield break; }
            yield return new WaitForSeconds(2f);
            var manager = NetworkManager.Singleton;
            foreach (var entry in SessionScoreboard.Current.Players)
            {
                if (game.TeamOfPlayer(entry.Id) == TeamSide.A) team.Add(entry);
                if (entry.Avatar != null && entry.Avatar.GetComponent<NetworkObject>().IsOwner) local = entry.Avatar;
            }
            team.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (int i = 0; i < team.Count; i++) if (team[i].Avatar == local) localSlot = i;
            Check(local != null && team.Count >= 4, "eight players and four teammates");
            if (failed) { Application.Quit(1); yield break; }
            reader = local.GetComponent<PlayerInputReader>(); reader.EngageAutopilot();
            cart = game.CartOf(TeamSide.A);
            for (int count = 1; count <= 4; count++)
            {
                // Round timer is replicated, so all owners drive the same time window.
                float start = 12f + (count - 1) * 10f;
                yield return WaitElapsed(start);
                holding = driving = false;
                if (manager.IsServer)
                {
                    cart.Carry.ResetPose(new Vector3(-20f, 0.03f, 4f), Quaternion.Euler(0f, 90f, 0f));
                    cart.SetHandleCount(count);
                    cart.ChangeWater(game.Config.CartCapacity, WaterLossReason.Filled);
                    for (int i = 0; i < team.Count; i++)
                        team[i].Avatar.RequestTeleport(i < count ? cart.Carry.StationOf(i) : new Vector3(-24f, 0f, 2f + i), Quaternion.Euler(0f, 90f, 0f));
                }
                yield return WaitElapsed(start + 1.5f);
                holding = localSlot >= 0 && localSlot < count;
                yield return WaitElapsed(start + 3f);
                Check(cart.Carry.CarrierCount == count, "E grab count=" + count + " actual=" + cart.Carry.CarrierCount);
                Vector3 before = cart.transform.position;
                driving = holding;
                yield return WaitElapsed(start + 5.5f);
                driving = false;
                Check(cart.transform.position.x - before.x > 1f, "roll count=" + count + " moved=" + (cart.transform.position.x - before.x));
                Check(cart.Carry.CarrierCount == count, "retain count=" + count);
                yield return WaitElapsed(start + 6.3f);
                holding = false;
                yield return WaitElapsed(start + 8f);
                Check(cart.Carry.CarrierCount == 0, "release count=" + count);
            }
            if (!failed) Debug.Log("CARRY_ROLLING_CHECK PASS id=" + manager.LocalClientId);
            yield return new WaitForSeconds(2f);
            Application.Quit(failed ? 1 : 0);
        }

        private IEnumerator WaitElapsed(float target)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (game.TryGetRoundTime(out float remaining, out float duration) && duration - remaining >= target) yield break;
                yield return null;
            }
            Check(false, "timer timeout");
        }

        private void Check(bool success, string message)
        {
            if (success) Debug.Log("CARRY_ROLLING_CHECK " + message);
            else { failed = true; Debug.LogError("CARRY_ROLLING_CHECK FAIL " + message); }
        }
    }
}
