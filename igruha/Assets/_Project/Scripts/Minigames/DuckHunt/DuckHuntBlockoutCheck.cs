#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Traps;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>Opt-in smoke check. Never enabled in ordinary play or release builds.
    /// Teleports only to arrange the two sabotage cases; traversal uses normal input.
    /// Run development host/client with --duck-hunt-check, or add in Editor Play Mode.
    /// </summary>
    public sealed class DuckHuntBlockoutCheck : MonoBehaviour
    {
        private DuckHuntArena arena;
        private PlayerController actor;
        private PlayerInputReader input;
        private bool failed;
        private bool doorClosed, doorReopened, floorOpened, floorReturned;
        [SerializeField] private bool finalOnly;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--duck-hunt-check") < 0) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "DuckHunt") new GameObject("DuckHuntBlockoutCheck").AddComponent<DuckHuntBlockoutCheck>();
        }

        private IEnumerator Start()
        {
            arena = FindFirstObjectByType<DuckHuntArena>();
            var door = FindFirstObjectByType<DoorTrap>();
            var floor = FindFirstObjectByType<CollapsingFloorTrap>();
            door.ClosedChanged += value => { doorClosed |= value; doorReopened |= !value && doorClosed; Log("door=" + value); };
            floor.OpenChanged += value => { floorOpened |= value; floorReturned |= !value && floorOpened; Log("collapse=" + value); };
            float deadline = Time.realtimeSinceStartup + 90;
            while (actor == null && Time.realtimeSinceStartup < deadline)
            {
                actor = SessionScoreboard.Current?.LocalPlayer?.Avatar;
                yield return null;
            }
            if (actor == null) { Fail("local avatar unavailable"); yield break; }
            input = actor.GetComponent<PlayerInputReader>();
            input.EngageAutopilot();
            // Hunter observes replicated states while the local duck supplies real E input.
            yield return new WaitForSeconds(8);
            if (actor.TryGetComponent(out HunterController hunter) && hunter.Active)
            {
                hunter.SetElevatorAxis(1);
                yield return new WaitForSeconds(2);
                hunter.SetElevatorAxis(0);
                Log("hunter elevator=" + actor.Position.y);
                hunter.TryFire(actor.Position + Vector3.up, Vector3.back);
                yield return new WaitForSeconds(24);
                CheckStates();
                yield break;
            }
            while (actor.MovementLocked) yield return null;
            foreach (var bot in FindObjectsByType<DebugPlayerBot>(FindObjectsSortMode.None))
                if (!SessionScoreboard.IsNetworked) bot.enabled = false;
            input.DriveMove(Vector2.zero);
            if (finalOnly)
            {
                yield return VerifyFinalCourse();
                yield break;
            }
            actor.TeleportTo(arena.GetWorldPoint(2, 24, 7.7f, .05f), Quaternion.identity);
            yield return new WaitForSeconds(1);
            input.DriveInteract(); Log("door E sent");
            yield return new WaitForSeconds(6);
            actor.TeleportTo(arena.GetWorldPoint(3, 24, 7.7f, .05f), Quaternion.identity);
            yield return new WaitForSeconds(1);
            input.DriveInteract(); Log("collapse E sent");
            yield return new WaitForSeconds(.6f);
            // Put this same real capsule over the open section to measure its landing.
            actor.TeleportTo(arena.GetWorldPoint(3, 37.5f, 12, .05f), Quaternion.identity);
            yield return new WaitForSeconds(2);
            if (arena.GetFloorIndex(actor.Position) != 2 || !actor.IsGrounded) Fail("collapse did not land on floor 3: " + actor.Position);
            else Log("collapse landing PASS floor=3");
            yield return new WaitForSeconds(2);
            CheckStates();
            if (failed) yield break;
            actor.TeleportTo(arena.GetWorldPoint(0, 9, 9.5f, .05f), Quaternion.identity);
            yield return new WaitForSeconds(.5f);
            int jumps = 0;
            actor.Jumped += () => jumps++;
            for (int f = 0; f < 4 && !failed; f++)
            {
                if (f >= 2)
                {
                    float jumpStart = f == 2 ? 13 : 10;
                    yield return WalkTo(arena.GetWorldPoint(f, jumpStart - .8f, 12.3f));
                    input.DriveJump();
                    yield return WalkTo(arena.GetWorldPoint(f, jumpStart + 3.8f, 12.3f));
                }
                if (f == 1)
                {
                    yield return WalkTo(arena.GetWorldPoint(f, 33.1f, 9.5f));
                    input.DriveJump();
                    yield return WalkTo(arena.GetWorldPoint(f, 37.6f, 9.5f));
                }
                else
                yield return WalkTo(arena.GetWorldPoint(f, 36, 9.5f));
                yield return WalkTo(arena.GetWorldPoint(f, 38, 12.3f));
                yield return WalkTo(arena.GetWorldPoint(f, 42, 12.3f));
                yield return WalkTo(arena.GetWorldPoint(f, 42, 11));
                yield return WalkTo(arena.GetWorldPoint(f, 42, 1.25f, 4));
                yield return WalkTo(arena.GetWorldPoint(f, 46, 1.25f, 4));
                yield return WalkTo(arena.GetWorldPoint(f, 46, 12.3f, 8));
                yield return WalkTo(arena.GetWorldPoint(f + 1, f == 3 ? 7.2f : 9.5f, 12.3f));
                Log("transition=" + (f + 1) + " position=" + actor.Position + " jumps=" + jumps);
            }
            if (failed) yield break;
            if (jumps != 3) Fail("expected one jump on floors 2, 3, 4; actual=" + jumps);
            else Log("floors 1-4 route PASS, three intended jumps");
            yield return VerifyFinalCourse();
        }

        private IEnumerator VerifyFinalCourse()
        {
            if (failed) yield break;
            // Arrange a deliberate miss from a raised pad, then let normal gravity land it.
            actor.TeleportTo(arena.GetWorldPoint(4, 29.5f, 4.5f, 3.05f), Quaternion.identity);
            yield return WalkTo(arena.GetWorldPoint(4, 29.5f, 1.8f));
            if (arena.GetFloorIndex(actor.Position) != 4 || !actor.IsGrounded)
                Fail("final parkour miss left floor 5");
            else Log("final fall PASS same floor, walking back");
            // Also attempt the lower route to the finish: its trigger must not accept it.
            yield return WalkTo(arena.GetWorldPoint(4, 45.5f, 1.8f));
            yield return WalkTo(arena.GetWorldPoint(4, 45.5f, 11));
            input.DriveJump();
            yield return new WaitForSeconds(1.4f);
            if (actor.GetComponent<DuckProgress>().Finished) Fail("finish accepted from recovery deck");
            else Log("finish from below rejected PASS");
            yield return WalkTo(arena.GetWorldPoint(4, 45.5f, 1.8f));
            yield return WalkTo(arena.GetWorldPoint(4, 8.5f, 1.8f));
            yield return WalkTo(arena.GetWorldPoint(4, 8.1f, 11.5f));
            yield return WalkTo(arena.GetWorldPoint(4, 14.5f, 11.5f, 3));
            float[] p = { 13, 18, 23, 28, 33, 38, 43 };
            float[] z = { 11.5f, 9.5f, 7, 5.5f, 7.5f, 9.5f, 11 };
            for (int i = 0; i < p.Length - 1 && !failed; i++)
            {
                yield return WalkTo(arena.GetWorldPoint(4, p[i] + 2.1f, z[i], 3));
                input.DriveJump();
                yield return WalkTo(arena.GetWorldPoint(4, p[i + 1] + 1.4f, z[i + 1], 3));
                if (!failed) Log("final jump=" + (i + 1) + " reached=" + actor.Position);
            }
            if (failed) yield break;
            if (!actor.GetComponent<DuckProgress>().Finished) Fail("final route did not trigger finish");
            Log("finish reached=" + actor.GetComponent<DuckProgress>().Finished + " failures=" + failed);
        }

        private IEnumerator WalkTo(Vector3 point)
        {
            if (failed) yield break;
            float until = Time.realtimeSinceStartup + 14;
            while (actor != null && Time.realtimeSinceStartup < until)
            {
                // Finish locks the avatar on the trigger edge, before its centre.
                if (actor.TryGetComponent(out DuckProgress progress) && progress.Finished)
                {
                    input.DriveMove(Vector2.zero);
                    yield break;
                }
                Vector3 delta = point - actor.Position; delta.y = 0;
                if (delta.magnitude < .22f && actor.IsGrounded && Mathf.Abs(point.y - actor.Position.y) < .35f)
                { input.DriveMove(Vector2.zero); yield break; }
                input.DriveMove(actor.WorldToMoveInput(delta.normalized));
                yield return new WaitForFixedUpdate();
            }
            input.DriveMove(Vector2.zero);
            Fail("walk timeout target=" + point + " actual=" + actor.Position);
        }

        private void CheckStates()
        {
            if (doorClosed && doorReopened && floorOpened && floorReturned) Log("trap cycle PASS");
            else Fail($"trap cycle door={doorClosed}/{doorReopened}, floor={floorOpened}/{floorReturned}");
        }

        private void Fail(string message) { failed = true; Debug.LogError("DH_CHECK " + message); }
        private static void Log(string message) => Debug.Log("DH_CHECK " + message + " t=" + Time.time.ToString("F2"));
        private void OnDisable() { if (input != null) input.DriveMove(Vector2.zero); }
    }
}
#endif
