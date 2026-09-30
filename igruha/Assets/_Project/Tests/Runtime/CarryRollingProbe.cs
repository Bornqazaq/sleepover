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
        private Vector3 driveDirection = Vector3.right;
        private int monitoredCount, contactSamples;
        private float worstContact;
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
            reader.DriveMove(driving ? local.WorldToMoveInput(driveDirection) : Vector2.zero);
        }

        private void LateUpdate()
        {
            for (int i = 0; i < monitoredCount; i++)
            {
                var pose = team[i].Avatar.GetComponent<WaterCartGripPose>();
                // A sharp reversal may legitimately break the gameplay tether. Only judge
                // contact while attached; the release/fade assertions cover detached players.
                if (!cart.Carry.IsCarriedBy(team[i].Avatar)) continue;
                if (pose == null) { worstContact = float.PositiveInfinity; continue; }
                if (pose.Weight < 0.99f) continue;
                worstContact = Mathf.Max(worstContact, pose.MaxPalmError);
                contactSamples++;
            }
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
            for (int pass = 0; pass < 8; pass++)
            {
                TeamSide side = pass < 4 ? TeamSide.A : TeamSide.B;
                int count = pass % 4 + 1;
                if (pass == 4)
                {
                    team.Clear(); localSlot = -1;
                    foreach (var entry in SessionScoreboard.Current.Players)
                        if (game.TeamOfPlayer(entry.Id) == side) team.Add(entry);
                    team.Sort((a, b) => a.Id.CompareTo(b.Id));
                    for (int i = 0; i < team.Count; i++) if (team[i].Avatar == local) localSlot = i;
                    cart = game.CartOf(side);
                }
                // Round timer is replicated, so all owners drive the same time window.
                float start = 12f + pass * 15f;
                yield return WaitElapsed(start);
                holding = driving = false;
                if (manager.IsServer)
                {
                    cart.Carry.ResetPose(new Vector3(-20f, 0.03f, side == TeamSide.A ? 4f : -4f), Quaternion.Euler(0f, 90f, 0f));
                    cart.SetHandleCount(count);
                    cart.ChangeWater(game.Config.CartCapacity, WaterLossReason.Filled);
                    for (int i = 0; i < team.Count; i++)
                        team[i].Avatar.RequestTeleport(i < count ? cart.Carry.StationOf(i) : new Vector3(-24f, 0f, 2f + i), Quaternion.Euler(0f, 90f, 0f));
                }
                yield return WaitElapsed(start + 1.5f);
                holding = localSlot >= 0 && localSlot < count;
                if (holding) yield return Tap();
                yield return WaitElapsed(start + 3f);
                Check(cart.Carry.CarrierCount == count, "E grab count=" + count + " actual=" + cart.Carry.CarrierCount);
                CheckPose(count, "stationary " + side);
                Vector3 before = cart.transform.position;
                driveDirection = Vector3.right;
                worstContact = 0f; contactSamples = 0; monitoredCount = count;
                driving = holding;
                yield return WaitElapsed(start + 5.5f);
                driving = false;
                CheckPose(count, "moving " + side);
                Check(cart.transform.position.x - before.x > 1f, "roll count=" + count + " moved=" + (cart.transform.position.x - before.x));
                Check(cart.Carry.CarrierCount == count, "retain count=" + count);
                monitoredCount = 0;
                // Server time trails on clients: separate observations from the next input.
                yield return WaitElapsed(start + 6f);
                if (holding) yield return Tap();
                yield return WaitElapsed(start + 6.7f);
                Check(cart.Carry.CarrierCount == 0, "second E releases count=" + count);
                yield return WaitElapsed(start + 7.2f);
                if (holding) yield return Tap();
                yield return WaitElapsed(start + 8f);
                Check(cart.Carry.CarrierCount == count, "third E regrabs count=" + count);
                monitoredCount = count;
                yield return WaitElapsed(start + 8.5f);
                driveDirection = Vector3.forward; driving = holding;
                yield return WaitElapsed(start + 9.5f);
                driveDirection = Vector3.left;
                yield return WaitElapsed(start + 10.5f);
                driving = false;
                yield return WaitElapsed(start + 11f);
                monitoredCount = 0;
                Check(contactSamples > 30 && worstContact < 0.06f,
                    "continuous grip " + side + " count=" + count + " samples=" + contactSamples + " worst=" + worstContact);
                yield return WaitElapsed(start + 11.5f);
                if (holding && cart.Carry.IsCarriedBy(local)) yield return Tap();
                holding = false;
                yield return WaitElapsed(start + 13f);
                Check(cart.Carry.CarrierCount == 0, "release count=" + count);
                for (int i = 0; i < count; i++)
                {
                    var pose = team[i].Avatar.GetComponent<WaterCartGripPose>();
                    Check(pose != null && pose.Weight == 0f, "relaxed hands " + team[i].Id);
                }
            }
            if (!failed) Debug.Log("CARRY_ROLLING_CHECK PASS id=" + manager.LocalClientId);
            yield return new WaitForSeconds(2f);
            Application.Quit(failed ? 1 : 0);
        }

        private IEnumerator Tap()
        {
            // Shorter than the shared InputAction's Hold threshold: the actual raw E path.
            reader.DriveInteractHold(true);
            yield return new WaitForSeconds(0.08f);
            reader.DriveInteractHold(false);
        }

        private void CheckPose(int count, string phase)
        {
            for (int i = 0; i < count; i++)
            {
                var pose = team[i].Avatar.GetComponent<WaterCartGripPose>();
                Check(pose != null && pose.Weight > 0.99f && pose.MaxPalmError < 0.06f,
                    "grip " + phase + " player=" + team[i].Id + " error=" + (pose != null ? pose.MaxPalmError : -1f));
            }
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
