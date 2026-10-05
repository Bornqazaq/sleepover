using System;
using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.SumoRing;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Opt-in presentation regression after the combat cases. No component is installed in ordinary play.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class SumoPresentationProbe : MonoBehaviour
    {
        private static readonly FieldInfo MotionField = typeof(SumoCombatPose).GetField("motion", BindingFlags.Instance | BindingFlags.NonPublic);
        private SumoMinigame game;
        private SumoFighter local;
        private SumoCombatPose[] poses;
        private CharacterFootGrounding[] grounding;
        private int[] seen;
        private int stage = -1, passed;
        private bool checkedStage, jumped, sawAir, finished;
        private float lowestSkin = float.PositiveInfinity;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--sumo-visual-check", out _)) return;
            var go = new GameObject("SumoPresentationProbe"); DontDestroyOnLoad(go); go.AddComponent<SumoPresentationProbe>();
        }
        private void Update()
        {
            var next = MinigameControllerBase.Current as SumoMinigame;
            if (next == null || next.Combat == null || next.Combat.Count == 0 || next == game) return;
            game = next; local = game.Combat.Local;
            poses = new SumoCombatPose[game.Combat.Count]; grounding = new CharacterFootGrounding[poses.Length]; seen = new int[poses.Length];
            for (int i = 0; i < poses.Length; i++)
            {
                var f = game.Combat.FighterAt(i); poses[i] = f.GetComponent<SumoCombatPose>(); grounding[i] = f.GetComponentInChildren<CharacterFootGrounding>();
            }
        }
        private void FixedUpdate()
        {
            if (game == null || local == null || !game.Combat.Active || game.Elapsed < 49 || finished) return;
            double t = game.Elapsed;
            int next = t < 51 ? 0 : t < 54 ? 1 : t < 56 ? 2 : t < 59 ? 3 : t < 62 ? 4 : t < 64 ? 5 : t < 65 ? 6 : t < 66.5 ? 7 : t < 68 ? 8 : t < 70 ? 9 : 10;
            if (next != stage)
            {
                stage = next; checkedStage = false;
                float angle = (local.Id + .5f) * Mathf.PI * 2 / game.Combat.Count;
                var outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                float radius = next == 1 ? game.Config.SupportRadius(outward, t) - .14f : next == 4 ? game.Config.OuterRadius(5) - .14f : 1.45f;
                local.Motor.TeleportTo(outward * radius + Vector3.up * (game.Config.Height + .025f), Quaternion.LookRotation(outward));
                if (next == 6) Send(SumoCommand.AttackDown, angle);
                if (next == 7) Send(SumoCommand.AttackUp, angle);
                if (next == 8) Send(SumoCommand.GuardDown, angle);
                if (next == 9) { Send(SumoCommand.GuardUp, angle); Send(SumoCommand.AttackDown, angle); Send(SumoCommand.AttackUp, angle); }
            }
            if (stage == 3 && !jumped && t > 56.5) { jumped = true; local.Input.DriveJump(); }
        }
        private void Send(SumoCommand command, float angle) => game.Combat.Submit(local.Id, command, 90 - angle * Mathf.Rad2Deg);
        private void LateUpdate()
        {
            if (game == null || local == null || !game.Combat.Active || finished) return;
            double t = game.Elapsed;
            for (int i = 0; i < poses.Length; i++)
            {
                seen[i] |= 1 << (int)(SumoMotion)MotionField.GetValue(poses[i]);
                var f = game.Combat.FighterAt(i);
                if (grounding[i] != null && f.IsGrounded && new Vector2(f.Motor.Position.x, f.Motor.Position.z).magnitude < 1.9f && !float.IsNaN(grounding[i].LowestSkinHeight))
                    lowestSkin = Mathf.Min(lowestSkin, grounding[i].LowestSkinHeight - game.Config.Height);
            }
            if (stage == 3 && jumped && !local.IsGrounded && local.Motor.Position.y > game.Config.Height + .4f)
                sawAir |= local.BalanceWeight < .01f;
            if (!checkedStage)
            {
                if (stage == 0 && t > 50) Check("centre", local.BalanceWeight < .01f);
                if (stage == 1 && t > 52.5) { Check("edge", local.BalanceWeight > .8f); Capture("edge"); }
                if (stage == 2 && t > 55) Check("leave-edge", local.BalanceWeight < .01f);
                if (stage == 3 && t > 58) Check("jump", sawAir);
                if (stage == 4 && t > 61) { Check("collapsed-edge", local.BalanceWeight > .8f); Capture("collapsed-edge"); }
                if (stage == 6 && t > 64.8) { Capture("charge"); checkedStage = true; }
            }
            if (t < 70) return;
            finished = true;
            int required = (1 << (int)SumoMotion.Heavy) | (1 << (int)SumoMotion.Quick) | (1 << (int)SumoMotion.Guard) | (1 << (int)SumoMotion.Balance);
            for (int i = 0; i < poses.Length; i++)
                Debug.Log("SUMO_VISUAL " + ((seen[i] & required) == required ? "PASS" : "FAIL") + " avatar=" + game.Combat.FighterAt(i).name + " motions=" + seen[i]);
            Debug.Log("SUMO_VISUAL skinMin=" + lowestSkin.ToString("F4") + " avatars=" + poses.Length);
            Debug.Log("SUMO_VISUAL COMPLETE passed=" + passed + "/5");
        }
        private void Check(string label, bool ok)
        {
            checkedStage = true; if (ok) passed++;
            float expected = SumoEdgeBalance.Evaluate(game.Config, local.Motor.Position, game.Elapsed, out _);
            Debug.Log($"SUMO_VISUAL_SAMPLE case={label} at={game.Elapsed:F4} pos={local.Motor.Position:F4} ground={local.IsGrounded} crouch={local.Motor.IsCrouched} knocked={local.Motor.IsKnockedDown} dead={local.Participant.Dead} phase={local.State.Phase} expected={expected:F4}");
            Debug.Log("SUMO_VISUAL " + (ok ? "PASS " : "FAIL ") + label + " owner=" + local.Id + " balance=" + local.BalanceWeight.ToString("F3"));
        }
        private void Capture(string name)
        {
            if (!LaunchArguments.TryGetValue("--sumo-capture", out _) || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var dir = System.IO.Path.GetDirectoryName(Application.consoleLogPath);
            if (!string.IsNullOrEmpty(dir)) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "sumo-visual-" + name + ".png"));
        }
    }
}
