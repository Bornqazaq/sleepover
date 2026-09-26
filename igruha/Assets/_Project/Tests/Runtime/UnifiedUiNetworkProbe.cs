using System.Collections;
using System.Reflection;
using System.Text;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Core.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Tests
{
    /// <summary>Opt-in development smoke test: real network phases, pause and rendered result columns.</summary>
    public sealed class UnifiedUiNetworkProbe : MonoBehaviour
    {
        private const float SampleRoundSeconds = 8;
        private static readonly MethodInfo OpenPause = typeof(PauseScreen).GetMethod("Pause", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ExitPause = typeof(PauseScreen).GetMethod("RequestExit", BindingFlags.Instance | BindingFlags.NonPublic);
        private MinigameControllerBase game;
        private RoundHud hud;
        private RoundResultsView results;
        private float roundStarted;
        private bool pauseChecked, ending, sampled, standingsSampled;
        private string screenshots;
        private bool render;
        private float nextTrace;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.HasFlag("--ui-check")) return;
            var root = new GameObject("UnifiedUiNetworkProbe"); DontDestroyOnLoad(root); root.AddComponent<UnifiedUiNetworkProbe>();
        }
        private void Awake()
        {
            LaunchArguments.TryGetValue("--ui-screenshots", out screenshots);
            render = !string.IsNullOrEmpty(screenshots) && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (render) System.IO.Directory.CreateDirectory(screenshots);
        }
        private void Update()
        {
            var current = MinigameControllerBase.Current;
            if (Time.unscaledTime >= nextTrace)
            {
                nextTrace = Time.unscaledTime + 15;
                Debug.Log("UI_CHECK TRACE " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " phase=" + (current != null ? current.Phase.ToString() : "none"));
            }
            if (current == null) return;
            if (current != game)
            {
                StopAllCoroutines(); game = current; roundStarted = 0;
                pauseChecked = ending = sampled = standingsSampled = false;
                foreach (var root in game.gameObject.scene.GetRootGameObjects())
                {
                    if (hud == null) hud = root.GetComponentInChildren<RoundHud>(true);
                    if (results == null) results = root.GetComponentInChildren<RoundResultsView>(true);
                }
                game.FinalStandingsReported += OnStandings;
            }
            if (game.Phase == MinigamePhase.Round)
            {
                if (roundStarted <= 0) roundStarted = Time.unscaledTime;
                if (!pauseChecked && Time.unscaledTime - roundStarted > 1)
                {
                    pauseChecked = true; StartCoroutine(CheckPause());
                }
                if (!ending && Time.unscaledTime - roundStarted > SampleRoundSeconds && NetworkManager.Singleton.IsServer)
                {
                    ending = true; game.EndMinigame();
                }
            }
            if (game.Phase == MinigamePhase.Results && results != null && results.gameObject.activeInHierarchy && !sampled)
            {
                sampled = true; StartCoroutine(CaptureResults());
            }
        }
        private IEnumerator CheckPause()
        {
            var pause = PauseScreen.Current;
            if (pause == null) { Debug.LogError("UI_CHECK FAIL no pause in " + game.name); yield break; }
            float before = Time.time;
            if (render) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(screenshots, game.gameObject.scene.name + "-gameplay.png"));
            yield return new WaitForSecondsRealtime(.25f);
            OpenPause.Invoke(pause, null);
            if (!pause.IsPaused || Time.timeScale != 1) Debug.LogError("UI_CHECK FAIL network pause stopped time");
            yield return new WaitForSecondsRealtime(.35f);
            if (Time.time <= before) Debug.LogError("UI_CHECK FAIL network time frozen");
            if (render)
            {
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(screenshots, game.gameObject.scene.name + "-pause.png"));
                yield return new WaitForSecondsRealtime(.25f);
            }
            pause.Resume();
            Debug.Log("UI_CHECK PAUSE " + game.gameObject.scene.name + " ok=" + !pause.IsPaused);
            var duel = game as Igruha.Minigames.OneBullet.OneBulletMinigame;
            if (LaunchArguments.HasFlag("--ui-leave") && duel != null && !NetworkManager.Singleton.IsServer && game.CanLeaveRound)
            {
                OpenPause.Invoke(pause, null);
                ExitPause.Invoke(pause, null);
                if (render)
                {
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(screenshots, game.gameObject.scene.name + "-confirm-exit.png"));
                    yield return new WaitForSecondsRealtime(.25f);
                }
                ExitPause.Invoke(pause, null);
                yield return new WaitForSecondsRealtime(.5f);
                if (pause.IsPaused || !NetworkManager.Singleton.IsConnectedClient || duel.LocalParticipant == null || !duel.LocalParticipant.Dead)
                    Debug.LogError("UI_CHECK FAIL leave round lost connection or did not leave");
                else Debug.Log("UI_CHECK LEAVE " + game.gameObject.scene.name + " left round, connection retained");
            }
        }
        private IEnumerator CaptureResults()
        {
            yield return new WaitForSecondsRealtime(.5f);
            Snapshot("ROUND");
            if (!render) yield break;
            foreach (var size in new[] { new Vector2Int(1280,720), new Vector2Int(1920,1080), new Vector2Int(2560,1440), new Vector2Int(2520,1080) })
            {
                Screen.SetResolution(size.x, size.y, false);
                yield return new WaitForSecondsRealtime(1);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(screenshots, game.gameObject.scene.name + "-" + size.x + "x" + size.y + ".png"));
                yield return new WaitForSecondsRealtime(.5f);
            }
        }
        private void OnStandings(SessionStandings standings)
        {
            if (!standingsSampled) { standingsSampled = true; StartCoroutine(CaptureStandings()); }
        }
        private IEnumerator CaptureStandings()
        {
            yield return new WaitForSecondsRealtime(.5f); Snapshot("SERIES");
            if (render) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(screenshots, "series-final.png"));
        }
        private void Snapshot(string kind)
        {
            var line = new StringBuilder("UI_CHECK " + kind + " " + game.gameObject.scene.name + ":");
            foreach (var row in results.GetComponentsInChildren<ResultRow>(true))
            {
                if (!row.gameObject.activeSelf) continue;
                foreach (string field in new[] { "Place", "PlayerName", "Value", "Points", "Total" })
                    line.Append('|').Append(row.transform.Find(field).GetComponent<TMP_Text>().text);
            }
            Debug.Log(line.ToString());
        }
    }
}
