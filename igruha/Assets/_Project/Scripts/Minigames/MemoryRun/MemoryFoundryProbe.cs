#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Igruha.Core.Minigame;
using Igruha.Core.Session;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>Opt-in evidence capture. Observes public events, never the secret route.</summary>
    public sealed class MemoryFoundryProbe : MonoBehaviour
    {
        private MemoryRunMinigame game;
        private ParticleSystem[] blasts;
        private string folder, role;
        private int lastWalker = -2, detonations, safeLandings, frames;
        private MinigamePhase lastPhase;
        private float captureAt = -1, frameSeconds, reportAt;
        private bool openingCaptured;
        private Transform conveyorCargo;
        private Vector3 previousCargoPosition;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (LaunchArguments.TryGetValue("--memory-foundry-check", out _)) SceneManager.sceneLoaded += Loaded;
        }
        private static void Loaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "MemoryRun" && LaunchArguments.TryGetValue("--memory-foundry-check", out var path))
                new GameObject("MemoryFoundryProbe").AddComponent<MemoryFoundryProbe>().Initialize(path);
        }
        public void Initialize(string path)
        {
            folder = path; Directory.CreateDirectory(folder);
            role = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
                ? (NetworkManager.Singleton.IsHost ? "host" : "client") : "editor";
            game = FindFirstObjectByType<MemoryRunMinigame>();
            blasts = GameObject.Find("_Effects").GetComponentsInChildren<ParticleSystem>();
            game.MineDetonated += Detonated; game.SafePlateProved += Safe;
            Write("BEGIN original foundry; scene=" + game.gameObject.scene.name);
            var lines = FindObjectsByType<MemoryFoundryConveyor>(FindObjectsSortMode.None);
            if (lines.Length > 0)
            {
                conveyorCargo = lines[0].transform.Find("MF_DynamiteBundle");
                previousCargoPosition = conveyorCargo.position;
            }
            var marker = FindFirstObjectByType<Igruha.Core.UI.ActivePlayerMarker>(FindObjectsInactive.Include);
            var signal = marker == null ? null : marker.GetComponentInChildren<MeshFilter>(true);
            Write("PRESENTATION conveyors=" + lines.Length + " signal=" + (signal == null ? "missing" : signal.sharedMesh.name));
            reportAt = Time.unscaledTime + 15;
        }
        private void Safe(int row, int lane) { safeLandings++; }
        private void Detonated(Vector3 center)
        {
            detonations++; Write("MINE " + detonations + " at=" + center.ToString("F2"));
            captureAt = Time.unscaledTime + .16f;
        }
        private void Update()
        {
            if (game == null || folder == null) return;
            frames++; frameSeconds += Time.unscaledDeltaTime;
            if (game.CurrentWalkerId != lastWalker)
            {
                lastWalker = game.CurrentWalkerId;
                Write("TURN " + lastWalker + " roster=" + game.RosterCount);
            }
            if (game.Phase != lastPhase)
            {
                lastPhase = game.Phase; Write("PHASE " + lastPhase);
                if (lastPhase == MinigamePhase.Results) Capture("results");
            }
            if (!openingCaptured && game.TurnArmed)
            {
                openingCaptured = true; Capture("gameplay");
            }
            if (captureAt >= 0 && Time.unscaledTime >= captureAt)
            {
                captureAt = -1; int particles = 0;
                foreach (var p in blasts) particles += p.particleCount;
                Write("BLAST_VISIBLE particles=" + particles);
                if (detonations <= 3) Capture("blast-" + detonations);
            }
            if (Time.unscaledTime >= reportAt)
            {
                reportAt = Time.unscaledTime + 15;
                Write("SAMPLE frames=" + frames + " meanMs=" + (frameSeconds / Mathf.Max(1, frames) * 1000).ToString("F2")
                    + " mines=" + detonations + " safeLandings=" + safeLandings + " phase=" + game.Phase);
                if (conveyorCargo != null)
                {
                    Write("CONVEYOR_MOVED distance=" + Vector3.Distance(previousCargoPosition, conveyorCargo.position).ToString("F2"));
                    previousCargoPosition = conveyorCargo.position;
                }
                frames = 0; frameSeconds = 0;
            }
        }
        private void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, role + "-" + name + ".png"));
        }
        private void Write(string message)
        {
            File.AppendAllText(Path.Combine(folder, role + "-probe.txt"), message + "\n");
            Debug.Log("[MemoryFoundryProbe] " + message);
        }
        private void OnDestroy()
        {
            if (game != null) { game.MineDetonated -= Detonated; game.SafePlateProved -= Safe; }
            if (folder != null) Write("END scene unloaded; mines=" + detonations + "; safe=" + safeLandings);
        }
    }
}
#endif
