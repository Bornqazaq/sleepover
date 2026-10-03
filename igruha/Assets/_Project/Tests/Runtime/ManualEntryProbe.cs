using System;
using System.Collections;
using System.Reflection;
using Igruha.Core.Hub;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.UI;
using Igruha.Networking;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Development-only delayed connection-screen and TV-button regression.</summary>
    public sealed class ManualEntryProbe : MonoBehaviour
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private AppNetworkManager app;
        private string mode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--manual-entry-check", out string mode)) return;
            var app = FindFirstObjectByType<AppNetworkManager>();
            if (app == null) throw new InvalidOperationException("Manual-entry probe requires Boot");
            // Prevent automatic CLI startup. The real connection screen will
            // invoke the real selection handler after every Start has run.
            app.enabled = false;
            var go = new GameObject("ManualEntryProbe");
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<ManualEntryProbe>();
            probe.app = app;
            probe.mode = mode;
        }

        private IEnumerator Start()
        {
            var screen = app.gameObject.AddComponent<NetworkConnectScreen>();
            var handler = typeof(AppNetworkManager).GetMethod("OnConnectionChosen", Private);
            screen.Chosen += (role, address, port) => handler.Invoke(app, new object[] { role, address, port });
            app.GetComponent<BootStatusScreen>()?.Hide();
            LaunchArguments.TryGetValue("--manual-entry-screenshot", out string screenshot);
            if (!string.IsNullOrEmpty(screenshot)) ScreenCapture.CaptureScreenshot(screenshot + "-entry.png");
            yield return new WaitForSecondsRealtime(3);
            if (NetworkManager.Singleton.IsListening) { Fail("network started before screen choice"); yield break; }
            Debug.Log("MANUAL_ENTRY_CHECK connection screen waited before network startup");

            bool client = mode == "client";
            LaunchArguments.TryGetValue("--port", out string portValue);
            typeof(NetworkConnectScreen).GetField("port", Private).SetValue(screen, portValue ?? "7785");
            typeof(NetworkConnectScreen).GetField("address", Private).SetValue(screen, client ? "127.0.0.1" : "");
            string[] keys = { "igruha.net.lastHost", "igruha.net.lastPort", "igruha.player.name" };
            var values = new string[keys.Length];
            var present = new bool[keys.Length];
            for (int i = 0; i < keys.Length; i++) { present[i] = PlayerPrefs.HasKey(keys[i]); values[i] = PlayerPrefs.GetString(keys[i]); }
            try
            {
                typeof(NetworkConnectScreen).GetMethod("Choose", Private).Invoke(screen,
                    new object[] { client ? NetworkStartRole.Client : NetworkStartRole.Host });
            }
            finally
            {
                for (int i = 0; i < keys.Length; i++)
                    if (present[i]) PlayerPrefs.SetString(keys[i], values[i]); else PlayerPrefs.DeleteKey(keys[i]);
                PlayerPrefs.Save();
            }

            float deadline = Time.realtimeSinceStartup + 100;
            bool characterRequested = false;
            while (!RosterReady() && Time.realtimeSinceStartup < deadline)
            {
                var selection = CharacterSelection.Current;
                if (selection != null && !characterRequested &&
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub" &&
                    (client ? NetworkManager.Singleton.IsConnectedClient : app.RoomReady))
                {
                    selection.ReportReady();
                    selection.Choose((int)NetworkManager.Singleton.LocalClientId);
                    characterRequested = true;
                }
                yield return null;
            }
            if (!RosterReady())
            {
                var roster = SessionScoreboard.Current;
                string details = roster == null ? "no scoreboard" : "players=" + roster.Players.Count;
                if (roster != null)
                    foreach (var player in roster.Players) details += " " + player.Id + ":avatar=" + (player.Avatar != null);
                Fail("hub roster did not arrive: " + details);
                yield break;
            }
            if (!client)
            {
                var menu = FindFirstObjectByType<ConsoleMenu>();
                if (menu == null) { Fail("TV menu missing"); yield break; }
                menu.RequestOpen();
                yield return null;
                menu.RequestPage(mode == "full" ? ConsolePage.Party : ConsolePage.Library);
                yield return null;
                if (mode == "full") menu.StartFullGame();
                else { menu.SelectGame(menu.Catalog.NextPlayable(-1, 1)); menu.Launch(); }
                Debug.Log("MANUAL_ENTRY_CHECK TV selected " + mode);
            }

            MinigameControllerBase game = null;
            deadline = Time.realtimeSinceStartup + 50;
            while (Time.realtimeSinceStartup < deadline)
            {
                game = MinigameControllerBase.Current;
                if (game != null && game.AwaitingTutorialReady) break;
                yield return null;
            }
            var tutorial = FindFirstObjectByType<TutorialScreen>();
            if (game == null || !game.AwaitingTutorialReady || tutorial == null || !tutorial.IsVisible)
            { Fail("tutorial did not appear after TV choice"); yield break; }
            Debug.Log("MANUAL_ENTRY_CHECK TUTORIAL_VISIBLE scene=" + game.gameObject.scene.name + " mode=" + mode);
            if (!string.IsNullOrEmpty(screenshot)) ScreenCapture.CaptureScreenshot(screenshot + "-tutorial.png");
            yield return new WaitForSecondsRealtime(3);
            game.ToggleTutorialReady();
            deadline = Time.realtimeSinceStartup + 50;
            while (Time.realtimeSinceStartup < deadline)
            {
                game = MinigameControllerBase.Current;
                if (game != null && game.Phase == MinigamePhase.Round) break;
                yield return null;
            }
            if (game == null || game.Phase != MinigamePhase.Round) { Fail("scored round did not start"); yield break; }
            Debug.Log("MANUAL_ENTRY_CHECK PASS mode=" + mode + " scene=" + game.gameObject.scene.name);
        }

        private static bool RosterReady()
        {
            var scoreboard = SessionScoreboard.Current;
            if (scoreboard == null || scoreboard.Players.Count < Mathf.Max(2, LaunchArguments.WaitPlayers)) return false;
            foreach (var player in scoreboard.Players) if (player.Avatar == null) return false;
            return true;
        }

        private static void Fail(string reason) => Debug.LogError("MANUAL_ENTRY_CHECK FAIL " + reason);
    }
}
