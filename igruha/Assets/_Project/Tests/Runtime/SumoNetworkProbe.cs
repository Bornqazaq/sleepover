using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.SumoRing;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Opt-in development probe. Uses the real punch, physics, six collapses and scene return.</summary>
    public sealed class SumoNetworkProbe : MonoBehaviour
    {
        private SumoMinigame game;
        private SumoParticipant local;
        private string scenario;
        private bool ready, punched, staged, jumped, departed, final, returned;
        private int warnings, collapses, deaths, punches;
        private float started, nextTrace, nextChoice, hubArrived = -1, quitAt = -1;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--sumo-check", out _)) return;
            var go = new GameObject("SumoNetworkProbe"); DontDestroyOnLoad(go); go.AddComponent<SumoNetworkProbe>();
        }
        private void Awake() { started = Time.realtimeSinceStartup; LaunchArguments.TryGetValue("--sumo-check", out scenario); }
        private void Update()
        {
            var selection = CharacterSelection.Current;
            if (selection != null && !selection.HasChosen && Time.realtimeSinceStartup >= nextChoice &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
            {
                nextChoice = Time.realtimeSinceStartup + 1;
                selection.ReportReady();
                int preferred = Unity.Netcode.NetworkManager.Singleton != null ? (int)Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0;
                for (int n = 0; n < 8; n++)
                {
                    int index = (preferred + n) % 8;
                    if (!selection.IsTaken(index)) { selection.Choose(index); break; }
                }
            }
            var current = MinigameControllerBase.Current as SumoMinigame;
            if (current != null && current != game)
            {
                if (local?.Push != null) local.Push.PunchStarted -= OnPunchStarted;
                game = current; local = null; ready = punched = staged = jumped = departed = false; warnings = collapses = deaths = punches = 0;
                game.Eliminated += (id, position) => { deaths++; Debug.Log("SUMO_CHECK death=" + id); };
                var arena = game.GetComponentInChildren<SumoArena>();
                if (arena == null) arena = Object.FindFirstObjectByType<SumoArena>();
                arena.Warning += ring => { warnings++; Debug.Log("SUMO_CHECK warning=" + ring); };
                arena.Collapse += ring => { collapses++; Debug.Log("SUMO_CHECK collapse=" + ring); };
            }
            if (game != null && local == null)
                foreach (var p in game.Participants)
                    if (p.Input != null && p.Input.LocallyControlled)
                    {
                        local = p; var bot = p.Motor.GetComponent<DebugPlayerBot>(); if (bot != null) bot.enabled = false;
                        p.Input.EngageAutopilot();
                        p.Push.PunchStarted += OnPunchStarted;
                    }
            if (game != null && game.Phase == MinigamePhase.Practice && local != null && !ready)
            { ready = true; game.ToggleTutorialReady(); }
            if (Time.realtimeSinceStartup >= nextTrace)
            {
                nextTrace = Time.realtimeSinceStartup + 10;
                Debug.Log("SUMO_CHECK trace phase=" + (game != null ? game.Phase.ToString() : "none") + " elapsed=" + (game != null ? game.Elapsed.ToString("F2") : "-") + " local=" + (local != null ? local.Player.Id : -1));
            }
            if (game != null && game.Phase == MinigamePhase.Results && !final)
            {
                final = true;
                Debug.Log("SUMO_CHECK FINAL alive=" + game.Round.AliveCount + " warnings=" + warnings + " collapses=" + collapses + " deaths=" + deaths);
                if (game.Round.AliveCount != 1 || warnings != 6 || collapses != 6) Debug.LogError("SUMO_CHECK FAIL final state");
            }
            if (final && !returned && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
            {
                if (hubArrived < 0) hubArrived = Time.realtimeSinceStartup;
                var motor = SessionScoreboard.Current?.LocalPlayer?.Avatar;
                // NGO completes each peer's scene/roster at a different frame.
                if (motor == null && Time.realtimeSinceStartup - hubArrived < 10) return;
                returned = true;
                var push = motor != null ? motor.GetComponent<PlayerPushAbility>() : null;
                bool clean = motor != null && !motor.MovementLocked && !motor.ImpulseImmune && push != null && push.CooldownOverride == 0 && push.ButtonOverride == null;
                Debug.Log("SUMO_CHECK RETURN restored=" + clean + " avatar=" + (motor != null));
                if (!clean) Debug.LogError("SUMO_CHECK FAIL leaked player state");
                if (!clean) Application.Quit(2);
                // Keep the host alive until clients finish loading the hub.
                quitAt = Time.realtimeSinceStartup + (Unity.Netcode.NetworkManager.Singleton.IsServer ? 8 : 1);
            }
            if (quitAt > 0 && Time.realtimeSinceStartup >= quitAt) Application.Quit();
            if (Time.realtimeSinceStartup - started > 180) { Debug.LogError("SUMO_CHECK FAIL timeout"); Application.Quit(2); }
        }
        private void OnPunchStarted() => punches++;
        private void FixedUpdate()
        {
            if (game == null || game.Phase != MinigamePhase.Round || local?.Motor == null || local.Dead || !game.Round.Ready) return;
            double t = game.Elapsed; int id = local.Player.Id;
            local.Input.DriveMove(Vector2.zero);
            // Hold two bodies at arm's length until all peers have their positions.
            if (t < 3.5 && id < 2)
                local.Motor.TeleportTo(new Vector3(0, game.Config.Height + .03f, id * .95f), Quaternion.Euler(0, id == 0 ? 0 : 180, 0));
            if (id == 1 && t > 4 && !punched)
            { punched = true; local.Motor.FacingOverride = Vector3.back; local.Push.RequestPush(); Debug.Log("SUMO_CHECK CLIENT_PUNCH"); }
            if (id == 1 && t > 4.1 && t < 4.4) local.Push.RequestPush();
            if (t > 7 && !staged)
            {
                if (id == 1)
                {
                    Debug.Log("SUMO_CHECK COOLDOWN punches=" + punches);
                    if (punches != 1) Debug.LogError("SUMO_CHECK FAIL cooldown");
                }
                staged = true; local.Motor.FacingOverride = null;
                float angle = id * Mathf.PI * 2 / game.Participants.Count;
                local.Motor.TeleportTo(new Vector3(Mathf.Cos(angle) * 1.45f, game.Config.Height + .04f, Mathf.Sin(angle) * 1.45f), Quaternion.identity);
            }
            if (t > 12 && !jumped) { jumped = true; local.Input.DriveJump(); Debug.Log("SUMO_CHECK JUMP"); }
            if (scenario == "disconnect" && id == game.Participants.Count - 1 && t > 18)
            { Debug.Log("SUMO_CHECK DISCONNECT"); Application.Quit(); return; }
            if (t > 77 && id > 0 && !departed)
            {
                departed = true; float a = id * Mathf.PI * 2 / game.Participants.Count;
                local.Motor.TeleportTo(new Vector3(Mathf.Cos(a) * 3.3f, game.Config.Height + .5f, Mathf.Sin(a) * 3.3f), Quaternion.identity);
                Debug.Log("SUMO_CHECK FALL_FROM_AIR");
            }
        }
    }
}
