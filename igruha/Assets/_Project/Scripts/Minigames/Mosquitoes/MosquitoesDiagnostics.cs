using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    /// <summary>Opt-in development build probes, never enabled in ordinary play.</summary>
    public sealed class MosquitoesDiagnostics : MonoBehaviour
    {
        private MosquitoesMinigame game;
        private string scenario;
        private float nextLog, roundElapsed;
        private bool fixtureApplied, looksReported, rejectedRoleProbe;
        private int boundaryProbe;
        private float boundaryProbeAt;
        private bool boundaryProbePending;
        private void Awake()
        {
            enabled = Debug.isDebugBuild && LaunchArguments.TryGetValue("--mosquito-check", out scenario);
            game = GetComponent<MosquitoesMinigame>();
            if (enabled) game.ResultsReported += ReportResults;
        }
        private void ReportResults(MinigameResults results)
        {
            var places = new System.Text.StringBuilder();
            foreach (var entry in results.Entries) places.Append(entry.PlayerId).Append(':').Append(entry.Place).Append(' ');
            int restored = 0;
            foreach (var player in SessionScoreboard.Current.Players)
                if (player.Avatar != null && player.Avatar.gameObject.activeSelf) restored++;
            Debug.Log($"[MosquitoResults] local={game.LocalId} places={places.ToString().Trim()} restored={restored} bodies={game.Bodies.Count}");
        }
        private void OnDestroy() { if (game != null) game.ResultsReported -= ReportResults; }
        private void Update()
        {
            if (game == null || game.Sleep == null) return;
            if (!looksReported && game.Bodies.Count > 0)
            {
                bool ready = true;
                foreach (var body in game.Bodies) if (body.VisualCharacterIndex < 0) ready = false;
                if (ready)
                {
                    looksReported = true; var looks = new System.Text.StringBuilder();
                    var selection = CharacterSelection.Current as Igruha.Networking.CharacterSelectionManager;
                    foreach (var body in game.Bodies) looks.Append(body.Id).Append(':').Append(body.VisualCharacterIndex).Append('/').Append(selection != null ? selection.CharacterOf(body.Id) : body.VisualCharacterIndex).Append(' ');
                    Debug.Log($"[MosquitoLooks] local={game.LocalId} actual/selected={looks}");
                }
            }
            bool authority = NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
            if (game.Phase == MinigamePhase.Round)
            {
                roundElapsed += Time.deltaTime;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (scenario == "flight-boundaries") TickBoundaryProbe();
#endif
                if (!authority && !rejectedRoleProbe && scenario == "round-timeout" && game.LocalId != game.GiantId && roundElapsed > 10)
                {
                    rejectedRoleProbe = true;
                    var transport = GetComponent<MosquitoesNetwork>();
                    transport.RequestBed(); transport.RequestSwat();
                    Debug.Log($"[MosquitoAuthority] local={game.LocalId} requested giant bed and swat from mosquito role; server must retain awake state");
                }
                if (authority && !fixtureApplied && scenario == "round-timeout" && roundElapsed > 8 && game.ControlsAvailable &&
                    (game.Sleep.Phase == GiantPhase.Awake || game.Sleep.Phase == GiantPhase.Sleeping && game.TryBed(game.GiantId)))
                {
                    fixtureApplied = true;
                    Debug.Log("[MosquitoFixture] scenario=round-timeout; giant wakes through validated bed action, original round timer retained");
                }
                bool leaveGiant = scenario == "giant-leaves" && game.LocalId == game.GiantId;
                bool leaveMosquito = scenario == "mosquitoes-leave" && game.LocalId != game.GiantId;
                float leaveAt = leaveMosquito ? 8 + (game.LocalId - 1) * 8 : 8;
                if (!authority && !fixtureApplied && (leaveGiant || leaveMosquito) && roundElapsed > leaveAt)
                {
                    fixtureApplied = true;
                    Debug.Log($"[MosquitoFixture] graceful leave local={game.LocalId}");
                    NetworkManager.Singleton.Shutdown(); return;
                }
                if (authority && !fixtureApplied && scenario != "round-timeout" && roundElapsed >
                    (scenario == "entry-timeout" ? game.Config.CountdownSeconds + MosquitoWindowEntry.Deadline + 2 : 8))
                {
                    fixtureApplied = true;
                    // These explicit fixture modes test transport and end-of-round cleanup.
                    // The unmodified hold/impact timings are covered separately by PlayMode tests.
                    if (scenario == "timeout" || scenario == "entry-timeout") GetComponent<RoundTimer>().StartTimer(.3f);
                    else if (scenario == "all-dead") { foreach (var body in game.Bodies) if (body != null) body.Kill(); }
                    else if (scenario == "sleep") game.Sleep.ApplySnapshot(GiantPhase.Sleeping, game.Config.SleepTarget, 0, 0, false);
                    Debug.Log($"[MosquitoFixture] scenario={scenario}");
                }
            }
            if (Time.unscaledTime < nextLog) return;
            nextLog = Time.unscaledTime + 2;
            double networkTime = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalTime.Time : 0;
            Debug.Log($"[MosquitoCheck] local={game.LocalId} giant={game.GiantId} phase={game.Phase} sleep={game.Sleep.Sleep:F2} state={game.Sleep.Phase} lamp={game.Sleep.LampOn} alive={game.AliveCount} bodies={game.Bodies.Count} countdown={game.Countdown:F2} elapsed={roundElapsed:F2} netTime={networkTime:F2}");
            foreach (var body in game.Bodies)
                if (body != null)
                    Debug.Log($"[MosquitoFlight] local={game.LocalId} id={body.Id} owner={body.IsOwner} position={body.Position:F2} speed={body.Velocity.magnitude:F2} buzz={body.BuzzVolume:F2} interpolation={body.FlightInterpolation} kinematic={body.FlightKinematic} visible={body.VisibleRendererCount} entered={body.HasEntered} attached={body.IsAttached} cooldown={body.BiteCooldown:F2}");
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void TickBoundaryProbe()
        {
            var own = game.FindBody(game.LocalId);
            if (own == null || !own.IsOwner || !own.IsAlive || !own.HasEntered) return;
            if (boundaryProbePending)
            {
                if (Vector3.Distance(own.Position, game.FlightRecovery(own.Id)) < .05f)
                {
                    Debug.Log($"[MosquitoBoundary] local={game.LocalId} probe={boundaryProbe} corrected={own.Position:F2}");
                    own.enabled = true;
                    boundaryProbePending = false;
                    boundaryProbe++;
                    boundaryProbeAt = roundElapsed + 1;
                }
                else if (roundElapsed > boundaryProbeAt + 3)
                {
                    Debug.LogError($"[MosquitoBoundary] local={game.LocalId} probe={boundaryProbe} server failed to correct {own.Position:F2}");
                    own.enabled = true;
                    boundaryProbePending = false;
                    boundaryProbe = 3;
                }
                return;
            }
            if (boundaryProbe >= 3 || roundElapsed < Mathf.Max(7, boundaryProbeAt)) return;
            Vector3 invalid = boundaryProbe == 0 ? new Vector3(-5, 1.8f, 0) :
                boundaryProbe == 1 ? new Vector3(0, game.Config.FlightCeiling + .6f, 0) : new Vector3(0, 1.8f, 5);
            own.MoveForBoundaryProbe(invalid);
            boundaryProbePending = true;
            boundaryProbeAt = roundElapsed;
            Debug.Log($"[MosquitoBoundary] local={game.LocalId} probe={boundaryProbe} injected={invalid:F2}");
        }
#endif
    }
}
