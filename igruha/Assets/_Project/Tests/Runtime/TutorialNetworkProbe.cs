using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Только development-стенд с --tutorial-check. Обычные запуски не затрагивает.</summary>
    public sealed class TutorialNetworkProbe : MonoBehaviour
    {
        private const float HostReadyDelay = 2f;
        private const float WaitingCheckDelay = 7f;
        private const float ClientReadyDelay = 12f;
        private const float Timeout = 55f;
        private MinigameControllerBase game;
        private float seenAt;
        private bool sent;
        private bool checkedWaiting;
        private bool completed;
        private bool cancelSent;
        private bool endedPractice;
        private int historyBefore;
        private float roundStartedAt;
        private bool endedRound;
        private string scenario;
        private float startedAt;
        private string completedScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--tutorial-check", out string mode)) return;
            var go = new GameObject("TutorialNetworkProbe");
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<TutorialNetworkProbe>();
            probe.scenario = mode;
            probe.startedAt = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (completed)
            {
                var next = MinigameControllerBase.Current;
                if (next != null && next != game && next.gameObject.scene.name != completedScene)
                {
                    completed = sent = checkedWaiting = cancelSent = endedPractice = endedRound = false;
                    seenAt = 0f;
                    startedAt = Time.realtimeSinceStartup;
                }
                else
                {
                    if (!endedRound && game != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer &&
                        Time.realtimeSinceStartup - roundStartedAt > 3f)
                    { endedRound = true; game.EndMinigame(); }
                    return;
                }
            }
            if (Time.realtimeSinceStartup - startedAt > Timeout + 90f) { Fail("no tutorial / timeout"); return; }
            var current = MinigameControllerBase.Current;
            if (current == null) return;
            if (game != current)
            {
                game = current;
                // A fresh controller in the same scene is the arena reset; preserve the check.
            }
            if (seenAt <= 0f)
            {
                if (!game.AwaitingTutorialReady || game.TutorialParticipants.Count < 2) return;
                seenAt = Time.realtimeSinceStartup;
                historyBefore = SessionScoreboard.Current?.History.Count ?? 0;
                Debug.Log("TUTORIAL_CHECK seen participants=" + game.TutorialParticipants.Count);
            }
            float elapsed = Time.realtimeSinceStartup - seenAt;
            var network = NetworkManager.Singleton;
            if (network == null || !network.IsListening) return;
            bool host = network.IsServer;
            if (game.Phase == MinigamePhase.Round)
            {
                if (!checkedWaiting) { Fail("round started before waiting check"); return; }
                completed = true;
                roundStartedAt = Time.realtimeSinceStartup;
                completedScene = game.gameObject.scene.name;
                Debug.Log("TUTORIAL_CHECK PASS scenario=" + scenario + " role=" + (host ? "host" : "client") + " scene=" + completedScene + " after=" + elapsed);
                return;
            }
            if (elapsed >= WaitingCheckDelay && !checkedWaiting)
            {
                checkedWaiting = true;
                if (!game.AwaitingTutorialReady) { Fail("tutorial closed early"); return; }
                if (SessionScoreboard.Current != null && SessionScoreboard.Current.History.Count != historyBefore) { Fail("practice reported results"); return; }
                Debug.Log("TUTORIAL_CHECK practice waiting verified");
            }
            if (scenario == "disconnect" && !host && network.LocalClientId > 1)
            {
                if (elapsed > ClientReadyDelay) { Debug.Log("TUTORIAL_CHECK disconnecting unready client"); Application.Quit(); }
                return;
            }
            if (scenario == "finish" && host && !endedPractice && elapsed > 3f)
            {
                endedPractice = true;
                game.EndMinigame();
                Debug.Log("TUTORIAL_CHECK ended practice without results");
            }
            float delay = host ? HostReadyDelay : ClientReadyDelay;
            if (scenario == "cancel") delay = host ? 6f : 2f;
            if (!sent && elapsed >= delay)
            {
                sent = true;
                game.ToggleTutorialReady();
                Debug.Log("TUTORIAL_CHECK ready intent");
            }
            if (scenario == "cancel" && !host && sent && !cancelSent && elapsed > 4f)
            {
                cancelSent = true;
                game.ToggleTutorialReady();
                Debug.Log("TUTORIAL_CHECK cancelled");
            }
            if (scenario == "cancel" && !host && cancelSent && elapsed > ClientReadyDelay)
            {
                // Читаем подтверждённое сервером состояние, а не шлём toggle каждый кадр.
                int local = SessionScoreboard.Current.LocalPlayer.Id;
                for (int i=0; i<game.TutorialParticipants.Count; i++)
                    if (game.TutorialParticipants[i].PlayerId == local && !game.TutorialParticipants[i].Ready)
                    {
                        game.ToggleTutorialReady();
                        cancelSent = false;
                        sent = true;
                        scenario = "cancel-complete";
                        break;
                    }
            }
            if (elapsed > Timeout) Fail("last ready did not start round");
        }

        private void Fail(string reason)
        {
            completed = true;
            Debug.LogError("TUTORIAL_CHECK FAIL " + reason);
        }
    }
}
