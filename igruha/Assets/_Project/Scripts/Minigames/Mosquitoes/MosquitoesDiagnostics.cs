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
        private bool fixtureApplied;
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
            bool authority = NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
            if (game.Phase == MinigamePhase.Round)
            {
                roundElapsed += Time.deltaTime;
                bool leaveGiant = scenario == "giant-leaves" && game.LocalId == game.GiantId;
                bool leaveMosquito = scenario == "mosquitoes-leave" && game.LocalId != game.GiantId;
                float leaveAt = leaveMosquito ? 8 + (game.LocalId - 1) * 8 : 8;
                if (!authority && !fixtureApplied && (leaveGiant || leaveMosquito) && roundElapsed > leaveAt)
                {
                    fixtureApplied = true;
                    Debug.Log($"[MosquitoFixture] graceful leave local={game.LocalId}");
                    NetworkManager.Singleton.Shutdown(); return;
                }
                if (authority && !fixtureApplied && roundElapsed > 8)
                {
                    fixtureApplied = true;
                    // These explicit fixture modes test transport and end-of-round cleanup.
                    // The unmodified hold/impact timings are covered separately by PlayMode tests.
                    if (scenario == "timeout") GetComponent<RoundTimer>().StartTimer(.3f);
                    else if (scenario == "all-dead") { foreach (var body in game.Bodies) if (body != null) body.Kill(); }
                    else if (scenario == "sleep") game.Sleep.ApplySnapshot(GiantPhase.Sleeping, game.Config.SleepTarget, 0, 0, false);
                    Debug.Log($"[MosquitoFixture] scenario={scenario}");
                }
            }
            if (Time.unscaledTime < nextLog) return;
            nextLog = Time.unscaledTime + 2;
            Debug.Log($"[MosquitoCheck] local={game.LocalId} giant={game.GiantId} phase={game.Phase} sleep={game.Sleep.Sleep:F2} state={game.Sleep.Phase} lamp={game.Sleep.LampOn} alive={game.AliveCount} bodies={game.Bodies.Count} countdown={game.Countdown:F2}");
        }
    }
}
