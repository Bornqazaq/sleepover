using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Minigames.SumoRing;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Real owner physics and UDP transport; no contact or state is injected.</summary>
    public sealed class SumoDashProbe : MonoBehaviour
    {
        private static readonly string[] Cases = { "client-hit", "host-blocked", "client-parried", "miss-lock-cooldown", "host-rear-hit", "edge-commit" };
        private readonly int[,] contacts = new int[6, 6];
        private readonly bool[] wrong = new bool[6];
        private SumoMinigame game;
        private SumoFighter local;
        private int current = -1, passed;
        private bool down, guard, checkedCase, finished, capture, captured, activeCaptured, windupSeen, dashSeen;
        private Vector3 start;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--sumo-check", out string scenario) || scenario != "dash") return;
            var go = new GameObject("SumoDashProbe"); DontDestroyOnLoad(go); go.AddComponent<SumoDashProbe>();
        }
        private void Awake()
        {
            capture = LaunchArguments.TryGetValue("--sumo-capture", out _);
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        }
        private void Update()
        {
            var next = MinigameControllerBase.Current as SumoMinigame;
            if (next == null || next == game || next.Combat == null) return;
            game = next; game.Combat.Contact += Observe;
        }
        private static int Attacker(int c) => c == 1 || c == 4 ? 0 : 1;
        private void Observe(SumoCombatHit hit)
        {
            if (game.Phase != MinigamePhase.Round) return;
            int c = (int)System.Math.Floor((game.Elapsed - 4) / 5);
            if (c < 0 || c >= Cases.Length) return;
            contacts[c, (int)hit.Contact]++;
            var a = game.Combat.FighterAt(game.Combat.IndexOf(Attacker(c)));
            var d = game.Combat.FighterAt(game.Combat.IndexOf(1 - Attacker(c)));
            Debug.Log($"SUMO_DASH geometry from={a.Motor.Position:F3} to={d.Motor.Position:F3} gap={SumoCombat.BodyGap(a.Capsule,d.Capsule):F3} dashAt={a.State.DashAt:F3} now={NetworkClock.Now:F3} parryUntil={d.State.ParryUntil:F3}");
            if (hit.Attack != SumoAttack.Dash || hit.Attacker != Attacker(c) || (hit.Target >= 0 && hit.Target != 1 - hit.Attacker)) wrong[c] = true;
            Debug.Log($"SUMO_DASH contact case={Cases[c]} kind={hit.Contact} attacker={hit.Attacker} target={hit.Target} at={game.Elapsed:F3}");
        }
        private void FixedUpdate()
        {
            if (finished || game == null || game.Combat == null) return;
            if (local == null) local = game.Combat.Local;
            if (local == null || game.Elapsed < 0 || game.Phase == MinigamePhase.Practice) return;
            int c = (int)System.Math.Floor((game.Elapsed - 4) / 5);
            if (c < 0) { Place(0); return; }
            if (c >= Cases.Length)
            {
                if (!finished) { finished = true; Debug.Log($"SUMO_DASH COMPLETE passed={passed}/6"); }
                return;
            }
            double t = game.Elapsed - 4 - c * 5;
            if (c != current)
            {
                current = c; down = guard = checkedCase = captured = activeCaptured = windupSeen = dashSeen = false;
                game.Combat.Submit(local.Id, SumoCommand.Cancel, 0);
            }
            if (t < .5 && !local.Participant.Dead) Place(c);
            var attacker = game.Combat.FighterAt(game.Combat.IndexOf(Attacker(c)));
            var state = attacker.VisualState;
            windupSeen |= state.Attack == SumoAttack.Dash && state.Phase == SumoCombatPhase.Windup;
            dashSeen |= state.Phase == SumoCombatPhase.Dash;
            if (capture && !captured && state.Attack == SumoAttack.Dash && state.Phase == SumoCombatPhase.Windup && attacker.VisualNow - state.Since > game.Config.DashWindup * .85f && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                captured = true;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.consoleLogPath), "dash-" + Cases[c] + ".png"));
            }
            if (capture && !activeCaptured && c == 3 && state.Phase == SumoCombatPhase.Dash && attacker.VisualNow - state.Since > .05 && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                activeCaptured = true;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.consoleLogPath), "dash-active.png"));
            }
            bool attacking = local.Id == Attacker(c), defending = local.Id == 1 - Attacker(c);
            if (defending && !guard && (c == 1 || c == 4) && t > .5)
            { guard = true; game.Combat.Submit(local.Id, SumoCommand.GuardDown, c == 4 ? 0 : 180); }
            if (defending && !guard && c == 2)
            {
                var confirmed = attacker.State;
                if (confirmed.Attack == SumoAttack.Dash && confirmed.Phase == SumoCombatPhase.Windup && NetworkClock.Now >= confirmed.DashAt - .07)
                { guard = true; game.Combat.Submit(local.Id, SumoCommand.GuardDown, 180); }
            }
            double launch = c == 1 || c == 4 ? 1.1 : .7;
            if (attacking && !down && t > launch)
            { down = true; start = local.Motor.Position; game.Combat.Submit(local.Id, SumoCommand.Dash, c == 5 ? 90 : 0); }
            if (attacking && c == 3 && down && t < 3)
            {
                game.Combat.Submit(local.Id, SumoCommand.Aim, 180);
                game.Combat.Submit(local.Id, SumoCommand.Dash, 180);
                game.Combat.Submit(local.Id, SumoCommand.Cancel, 180);
            }
            if (t > (c == 5 ? 2.1 : 3.8) && !checkedCase)
            {
                checkedCase = true;
                SumoContact expected = c == 1 ? SumoContact.Block : c == 2 ? SumoContact.Parry : c == 3 || c == 5 ? SumoContact.Miss : SumoContact.Push;
                int total = 0; for (int n = 0; n < 6; n++) total += contacts[c, n];
                bool ok = !wrong[c] && contacts[c, (int)expected] == 1 && total == 1 && windupSeen;
                if ((c == 0 || c == 4) && local.Id < 2)
                { Vector3 delta = local.Motor.Position - start; ok &= delta.z >= -.1f && delta.z < 2.65f && Mathf.Abs(delta.x) < .2f; }
                if (c == 2 && defending) ok &= (local.Motor.Position - start).magnitude < .2f;
                if (c == 3 && attacking)
                { Vector3 delta = local.Motor.Position - start; ok &= delta.z > 1.5f && delta.z < 2.65f && Mathf.Abs(delta.x) < .12f; }
                if (c == 1 && defending) ok &= (local.Motor.Position - start).magnitude < .2f;
                if (c == 5) ok &= attacker.Participant.Dead;
                if (ok) passed++;
                if (c == 5) { finished = true; Debug.Log($"SUMO_DASH COMPLETE passed={passed}/6"); }
                Debug.Log($"SUMO_DASH {(ok ? "PASS" : "FAIL")} case={Cases[c]} expected={expected} count={total} windup={windupSeen} dash={dashSeen} pos={local.Motor.Position:F3} start={start:F3} dead={attacker.Participant.Dead}");
            }
        }
        private void Place(int c)
        {
            Vector3 position;
            if (local.Id < 2)
            {
                bool attacking = local.Id == Attacker(c);
                position = new Vector3(0, game.Config.Height + .03f, attacking ? -1.4f : c == 2 ? -.6f : -.25f);
                if (c == 3 && !attacking) position.x = -1.8f;
                if (c == 5) position = new Vector3(attacking ? game.Config.SupportRadius(Vector3.right, 32) - .65f : -1.5f, game.Config.Height + .03f, .15f);
            }
            else
            {
                float angle = (local.Id - 2) * Mathf.PI * 2 / Mathf.Max(1, game.Participants.Count - 2);
                float radius = game.Config.OuterRadius(game.Config.RingCount - 1) - .35f;
                position = new Vector3(Mathf.Cos(angle) * radius, game.Config.Height + .03f, Mathf.Sin(angle) * radius);
            }
            local.Motor.TeleportTo(position, Quaternion.identity); start = position;
        }
    }
}
