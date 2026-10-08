using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Minigames.OneBullet;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.Tests
{
    /// <summary>Opt-in development probe in real processes: inventory, impacts, escape, two closures and hub.</summary>
    public sealed class OneBulletUpgradeProbe : MonoBehaviour
    {
        private OneBulletMinigame game;
        private OneBulletNetwork relay;
        private bool ready, first, second, third, eliminated, forcedGun, escaped, verified, final, returned;
        private int impacts, stageChanges, roster = 4;
        private float started, hubReadyAt = -1;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.HasFlag("--onebullet-upgrade")) return;
            var go = new GameObject("OneBulletUpgradeProbe"); DontDestroyOnLoad(go); go.AddComponent<OneBulletUpgradeProbe>();
        }
        private void Awake()
        {
            started = Time.realtimeSinceStartup;
            if (LaunchArguments.TryGetValue("--upgrade-roster", out var value)) int.TryParse(value, out roster);
        }
        private void Update()
        {
            var current = MinigameControllerBase.Current as OneBulletMinigame;
            if (current != null && current != game)
            {
                game = current; relay = game.GetComponent<OneBulletNetwork>();
                ready = first = second = third = eliminated = forcedGun = escaped = verified = false;
                impacts = stageChanges = 0;
                game.Decoys.Impact += (p, speed) => { impacts++; Debug.Log("OB_UPGRADE impact=" + impacts); };
                game.Storm.Changed += () => { stageChanges++; Debug.Log("OB_UPGRADE stage=" + game.Storm.State.Stage + " target=" + game.Storm.State.Target); };
            }
            if (game != null && game.Phase == MinigamePhase.Practice && game.LocalRosterReady && !ready)
            { ready = true; game.ToggleTutorialReady(); }
            if (game != null && game.Phase == MinigamePhase.Results && !final)
            {
                final = true;
                bool valid = verified && game.Round.Winner == 0 && game.Round.AliveCount == 1 && game.Round.Find(0).Kills == 0 && impacts > 0;
                Debug.Log("OB_UPGRADE FINAL valid=" + valid + " impacts=" + impacts + " stages=" + stageChanges);
                if (!valid) Debug.LogError("OB_UPGRADE FAIL final");
            }
            if (final && !returned && SceneManager.GetActiveScene().name == "Hub")
            {
                var avatar = SessionScoreboard.Current?.LocalPlayer?.Avatar;
                if (avatar != null && !avatar.MovementLocked)
                {
                    if (hubReadyAt < 0) hubReadyAt = Time.realtimeSinceStartup;
                    bool host = Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer;
                    // Hub scene activation precedes avatar spawning. Keep the host alive until clients finish loading.
                    if (Time.realtimeSinceStartup - hubReadyAt >= (host ? 5f : .5f))
                    { returned = true; Debug.Log("OB_UPGRADE RETURN valid=True"); Application.Quit(); }
                }
            }
            if (Time.realtimeSinceStartup - started > 160) { Debug.LogError("OB_UPGRADE FAIL timeout"); Application.Quit(2); }
        }
        private void FixedUpdate()
        {
            if (game == null || game.Phase != MinigamePhase.Round || !game.LocalRosterReady) return;
            var local = game.LocalParticipant; if (local?.Motor == null) return;
            double now = NetworkClock.Now, t = now - game.Round.BeginsAt;
            int id = game.LocalId;
            local.Input.EngageAutopilot(); local.Input.DriveMove(Vector2.zero);
            if (!local.Dead)
            {
                int node = id == 0 ? 42 : id == 1 ? 52 : id == 2 ? 49 : id == 3 ? 61 : id == 4 ? 43 : id == 5 ? 44 : id == 6 ? 50 : 59;
                if (id == 2 && ((t > 23 && t < 26) || t > 29)) node = roster > 4 ? 8 : 47;
                local.Motor.TeleportTo(game.Storm.Layout.StandingPoint(node), Quaternion.identity);
                if (t > 3 && !first) { first = true; relay.RequestCan(Vector3.up); }
                if (t > 4 && !second) { second = true; relay.RequestCan(Vector3.down); }
                if (t > 5 && !third) { third = true; relay.RequestCan(Vector3.forward); }
            }
            if (id == 0 && t > 10 && !eliminated)
            { eliminated = true; for (int victim = 3; victim < roster; victim++) game.Leave(victim); }
            if (id == 0 && t > 12 && !forcedGun && game.Round.Pickup >= 0)
            {
                forcedGun = true;
                // Put the only floor weapon in the closing region, then let the real closure relocate it.
                game.Round.RelocatePickup(roster > 4 ? 0 : 4); relay.Publish();
                Debug.Log("OB_UPGRADE weapon_in_closing=" + !game.Storm.SafeWeapon(game.PickupPosition));
            }
            if (t > 27 && t < 29 && !escaped)
            {
                escaped = true;
                bool valid = game.Round.Find(2).DangerSince < 0 && game.Round.Find(2).Alive;
                Debug.Log("OB_UPGRADE ESCAPE valid=" + valid);
                if (!valid) Debug.LogError("OB_UPGRADE FAIL escape did not reset exposure");
            }
            if (t > (roster > 4 ? 86 : 52) && !verified)
            {
                verified = true; bool valid = game.Storm.State.Stage == 6 && !game.Storm.State.Warning &&
                    game.Round.AliveCount == 2 && !game.Round.Find(2).Alive && game.Round.Find(0).Kills == 0 &&
                    game.Round.Pickup >= 0 && game.Storm.SafeWeapon(game.PickupPosition) && impacts > 0;
                foreach (var r in game.Round.Records) valid &= r.Cans == 0;
                Debug.Log("OB_UPGRADE CHECK valid=" + valid + " stage=" + game.Storm.State.Stage + " alive=" + game.Round.AliveCount + " pickup=" + game.Round.Pickup);
                if (!valid) Debug.LogError("OB_UPGRADE FAIL state");
            }
            if (id == 0 && t > (roster > 4 ? 89 : 55) && game.Round.AliveCount == 2) game.Leave(1);
        }
    }
}
