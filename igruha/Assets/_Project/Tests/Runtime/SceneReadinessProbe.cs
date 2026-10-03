using System.Collections;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Igruha.Tests
{
    // Opt-in development-only integration checks; absent from release builds.
    public sealed class SceneReadinessProbe : MonoBehaviour
    {
        private MinigameControllerBase observed;
        private string mode;
        private bool inputChecked;
        private bool inputRunning;
        private bool readySent;
        private bool roundChecked;
        private float seenAt;
        private float roundAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--scene-ready-check", out string mode)) return;
            var go = new GameObject("SceneReadinessProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<SceneReadinessProbe>().mode = mode;
        }

        private void Update()
        {
            var game = MinigameControllerBase.Current;
            if (game == null) return;
            if (observed != game)
            {
                observed = game;
                inputChecked = inputRunning = readySent = roundChecked = false;
                seenAt = Time.realtimeSinceStartup;
                roundAt = 0;
            }
            var readiness = game.GetComponent<IMinigameSceneReadiness>();
            if (!game.GameplayActive && !game.AwaitingTutorialReady) return;
            if (readiness == null || !readiness.CanStart)
            { Debug.LogError("SCENE_READY_CHECK FAIL phase before barrier " + game.Phase); enabled = false; return; }
            var local = SessionScoreboard.Current?.LocalPlayer;
            if (local?.Avatar == null)
            { Debug.LogError("SCENE_READY_CHECK FAIL missing local avatar"); enabled = false; return; }
            var reader = local.Avatar.GetComponent<PlayerInputReader>();
            if (mode == "input" && !inputChecked && !inputRunning && game.GameplayActive &&
                !local.Avatar.MovementLocked && !reader.Suspended && Time.realtimeSinceStartup - seenAt > 5)
                StartCoroutine(CheckKeyboard(game, local.Avatar, reader));
            if (mode == "input" && inputChecked && game.AwaitingTutorialReady && !readySent)
            { readySent = true; game.ToggleTutorialReady(); }
            if (game.Phase != MinigamePhase.Round) return;
            if (roundAt == 0) roundAt = Time.realtimeSinceStartup;
            if (!roundChecked && Time.realtimeSinceStartup - roundAt > 5)
            {
                roundChecked = true;
                string scene = game.gameObject.scene.name;
                bool walkingRole = scene == "MemoryRun" || scene == "Infection" || scene == "Stopwatch" || scene == "SumoRing";
                bool controlReady = reader.LocallyControlled && (!walkingRole || reader.enabled);
                var mosquitoes = game as Igruha.Minigames.Mosquitoes.MosquitoesMinigame;
                if (mosquitoes != null && !mosquitoes.IsGiant(local.Avatar))
                {
                    // Mosquitoes park the human avatar and use an owned flight body.
                    var body = mosquitoes.FindBody(local.Id);
                    controlReady = body != null && body.IsSpawned && body.IsLocal && body.enabled;
                }
                if (!controlReady)
                    Debug.LogError("SCENE_READY_CHECK FAIL local input disabled in round");
                else Debug.Log("SCENE_READY_CHECK PASS scene=" + game.gameObject.scene.name +
                    " participants=" + readiness.ParticipantIds.Count + " local=" + local.Id + " reader=" + reader.enabled);
            }
            if (mode == "flow" && roundChecked && Time.realtimeSinceStartup - roundAt > 10 &&
                NetworkManager.Singleton.IsServer) game.EndMinigame();
        }

        private IEnumerator CheckKeyboard(MinigameControllerBase game, PlayerController avatar, PlayerInputReader reader)
        {
            inputRunning = true;
            if (!reader.enabled || !reader.LocallyControlled || reader.Autopilot)
            { Debug.LogError("SCENE_READY_INPUT FAIL reader flags"); yield break; }
            var keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            Vector3 start = avatar.transform.position;
            bool readMove = false;
            float until = Time.realtimeSinceStartup + 0.45f;
            while (avatar != null && Time.realtimeSinceStartup < until)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                yield return null;
                readMove |= reader.MoveInput.sqrMagnitude > 0.1f;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            float distance = avatar != null ? Vector3.Distance(start, avatar.transform.position) : 0;
            if (!readMove || distance < 0.08f) Debug.LogError("SCENE_READY_INPUT FAIL move=" + readMove + " distance=" + distance);
            else Debug.Log("SCENE_READY_INPUT PASS phase=" + game.Phase + " distance=" + distance + " autopilot=false");
            inputChecked = true;
            inputRunning = false;
        }
    }
}
