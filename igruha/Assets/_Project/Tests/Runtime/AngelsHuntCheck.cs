using System;
using System.Collections;
using System.Reflection;
using Igruha.Core.Audio;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.CryingAngels;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Opt-in real transport/motor/audio regression, run with --playtest-check angels.</summary>
    internal static class AngelsHuntCheck
    {
        public static IEnumerator Run(CryingAngelsMinigame game, Action<string> fail)
        {
            foreach (var bot in UnityEngine.Object.FindObjectsByType<DebugPlayerBot>(FindObjectsSortMode.None))
            { bot.Stop(); bot.enabled = false; }
            var board = SessionScoreboard.Current;
            var local = board.LocalPlayer.Avatar;
            var input = local.GetComponent<PlayerInputReader>();
            input.EngageAutopilot(); input.DriveMove(Vector2.zero);
            while (game.StartCountdownActive || game.Keeper == null) yield return null;
            bool host = NetworkManager.Singleton.IsServer;
            bool keeper = local == game.Keeper;
            PlayerController runner = null;
            foreach (var player in board.Players)
                if (player.Avatar != game.Keeper) { runner = player.Avatar; break; }
            var state = runner.GetComponent<RunnerState>();
            var rig = UnityEngine.Object.FindFirstObjectByType<FirstPersonCameraRig>();
            if (keeper) rig.SetViewDrivenExternally(true);
            var sound = UnityEngine.Object.FindFirstObjectByType<CryingAngelsAudio>();
            var audio = (MinigameAudioPlayer)typeof(CryingAngelsAudio).GetField("audioPlayer",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sound);
            var voices = audio.GetComponentsInChildren<AudioSource>(true);
            var previousClips = new AudioClip[voices.Length];
            int stings = 0;
            // Nearest low exhibit: torso hidden, head visible. Do not depend on old cover coordinates.
            var covers = GameObject.Find("_Arena/Covers").transform;
            Transform low = null;
            foreach (Transform cover in covers)
                if (cover.name.EndsWith("Low") && (low == null || cover.position.sqrMagnitude < low.position.sqrMagnitude)) low = cover;
            if (low == null) { fail("angels low cover missing"); yield break; }
            Vector3 direction = low.position; direction.y = 0; direction.Normalize();
            Vector3 partialPoint = low.position + direction * 2.5f; partialPoint.y = .1f;
            float partialYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            int stage = -1;
            bool swept = false, partial = false, held = false, returned = false;
            bool crouched = false, crouchFrozen = false, crouchReturned = false;
            var crouchInput = typeof(PlayerInputReader).GetProperty("CrouchHeld");
            float frozenAt = -1f, respawnSeconds = -1f;
            var timer = UnityEngine.Object.FindFirstObjectByType<RoundTimer>();
            var reference = new GameObject("Angels route test movement reference");
            if (local == runner) local.SetCameraReference(reference.transform);
            float deadline = Time.realtimeSinceStartup + 60f;
            while (game != null && game.Phase == MinigamePhase.Round && Time.realtimeSinceStartup < deadline)
            {
                float elapsed = timer.Duration - timer.Remaining;
                int next = elapsed < 5 ? 0 : elapsed < 8 ? 1 : elapsed < 11 ? 2 :
                    elapsed < 14 ? 3 : elapsed < 18 ? 4 : elapsed < 23 ? 5 : elapsed < 26 ? 6 : elapsed < 30 ? 7 : 8;
                if (next != stage)
                {
                    if (keeper && (stage == 1 || stage == 3) && stings != 0)
                        fail("angels sonar sting during " + (stage == 1 ? "sweep" : "partial cover"));
                    if (keeper && stage == 5 && stings != 1)
                        fail("angels fully visible target must produce exactly one sting: " + stings);
                    stage = next;
                    if (keeper)
                    {
                        audio.StopAll();
                        foreach (var voice in voices) voice.clip = null;
                        Array.Clear(previousClips, 0, previousClips.Length);
                        stings = 0;
                    }
                    if (host && stage < 7)
                    {
                        int i = 0;
                        foreach (var player in board.Players)
                        {
                            if (player.Avatar == game.Keeper) continue;
                            Vector3 point = new Vector3(-2 + i++ * 2, .1f, -4);
                            if (player.Avatar == runner)
                            {
                                if (stage == 1 || stage == 5) point = new Vector3(0, .1f, 4);
                                if (stage == 3) point = partialPoint;
                                if (stage == 6) point = new Vector3(0, .1f, 10f);
                            }
                            player.Avatar.RequestTeleport(point, Quaternion.identity);
                        }
                    }
                    Debug.Log($"PLAYTEST_CHECK angels stage={stage} keeper={keeper} runner={runner.name}");
                }
                if (keeper)
                {
                    // Fast alternating turns cross the runner in the swept gameplay sector,
                    // while neither endpoint actually shows the runner in the current light.
                    float yaw = stage == 1 ? (Mathf.FloorToInt((elapsed - 5f) / .35f) % 2 == 0 ? -70f : 70f) :
                        stage == 3 ? partialYaw : stage == 5 || stage == 7 ? 0f : 120f;
                    rig.SetView(yaw, stage == 5 || stage == 7 ? 8f : 0f);
                    for (int i = 0; i < voices.Length; i++)
                    {
                        var clip = voices[i].clip;
                        if (clip != null && clip != previousClips[i] && clip.name.Contains("Spotted_Sting")) stings++;
                        previousClips[i] = clip;
                    }
                }
                if (stage == 1 && state.Current == RunnerState.Phase.Frozen) swept = true;
                if (stage == 3 && state.Current == RunnerState.Phase.Frozen)
                {
                    partial = true;
                    if (game.KeeperLight.ClearlySees(runner.GetComponent<Collider>()))
                        fail("angels covered body counted as fully visible");
                }
                if (stage == 5)
                {
                    if (!held && state.Current == RunnerState.Phase.Frozen)
                    { held = true; frozenAt = Time.time; }
                    if (held && !returned && runner.Position.z > 20f)
                    { returned = true; respawnSeconds = Time.time - frozenAt; }
                }
                if (local == runner)
                {
                    crouchInput.SetValue(input, stage == 6 || stage == 7);
                    input.DriveMove(stage == 7 ? Vector2.down : Vector2.zero);
                }
                if (stage == 6 && runner.IsCrouched) crouched = true;
                if (stage == 7)
                {
                    if (state.Current == RunnerState.Phase.Frozen && runner.IsCrouched) crouchFrozen = true;
                    if (crouchFrozen && runner.Position.z > 20f) crouchReturned = true;
                }
                if (stage == 8)
                {
                    if (!swept || !partial || !held || !returned)
                        fail($"angels missing reactions sweep={swept} partial={partial} held={held} respawn={returned}");
                    if (respawnSeconds < 1f || respawnSeconds > 1.55f)
                        fail("angels respawn duration=" + respawnSeconds);
                    if (!crouched || !crouchFrozen || !crouchReturned)
                        fail($"angels exposed crouch must be caught and respawn: crouched={crouched} frozen={crouchFrozen} returned={crouchReturned}");
                    Debug.Log($"PLAYTEST_CHECK angels hunt keeper={keeper} respawn={respawnSeconds:F3}s sweep={swept} cover={partial} exposedCrouchCaught={crouchFrozen && crouchReturned}");
                    UnityEngine.Object.Destroy(reference);
                    yield break;
                }
                yield return null;
            }
            fail("angels hunt timed out or ended early");
        }

    }
}
