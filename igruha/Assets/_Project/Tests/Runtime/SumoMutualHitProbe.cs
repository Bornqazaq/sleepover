using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Minigames.SumoRing;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Opt-in two-player presentation fixture: two server-confirmed contacts through real RPCs/owner impulses/poses.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class SumoMutualHitProbe : MonoBehaviour
    {
        private static readonly FieldInfo Motion = typeof(SumoCombatPose).GetField("motion", BindingFlags.NonPublic | BindingFlags.Instance);
        private SumoMinigame game;
        private SumoCombatPose[] poses;
        private bool placed, sent, complete, captured;
        private int contacts, seen;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--sumo-mutual-check", out _)) return;
            var go = new GameObject("SumoMutualHitProbe"); DontDestroyOnLoad(go); go.AddComponent<SumoMutualHitProbe>();
            Debug.Log("SUMO_MUTUAL installed");
        }
        private void Update()
        {
            var next = MinigameControllerBase.Current as SumoMinigame;
            if (next == null || next == game || next.Combat == null || next.Combat.Count != 2) return;
            game = next; poses = new SumoCombatPose[2];
            for (int i = 0; i < 2; i++) poses[i] = game.Combat.FighterAt(i).GetComponent<SumoCombatPose>();
            game.Combat.Contact += hit =>
            {
                if (game.Elapsed <= 5) return;
                Debug.Log("SUMO_MUTUAL contact " + hit.Attacker + "->" + hit.Target + " " + hit.Contact + " " + hit.Attack);
                if (hit.Contact == SumoContact.Push && hit.Attack == SumoAttack.Heavy) contacts++;
            };
        }
        private void FixedUpdate()
        {
            if (game == null || !game.Combat.Active || game.Elapsed < 4 || complete) return;
            var local = game.Combat.Local; if (local == null) return;
            if (!placed)
            {
                placed = true; local.ResetCombat();
                local.Motor.TeleportTo(new Vector3(0, game.Config.Height + .025f, local.Id == 0 ? -.65f : .65f), Quaternion.Euler(0, local.Id == 0 ? 0 : 180, 0));
                Debug.Log("SUMO_MUTUAL placed owner=" + local.Id + " at=" + local.Motor.Position);
            }
            if (!NetworkManager.Singleton.IsServer || game.Elapsed < 7 || sent) return;
            sent = true;
            // Isolate delivery order from owner movement and charge timing. The full combat suite
            // separately tests authority/ownership/range. No client can call this fixture in a release.
            var network = game.GetComponent<SumoNetwork>();
            for (int i = 0; i < 2; i++)
                network.BroadcastContact(new SumoCombatHit { Attacker = i, Target = 1 - i, Attack = SumoAttack.Heavy, Contact = SumoContact.Push,
                    Direction = i == 0 ? Vector3.forward : Vector3.back, Point = Vector3.up * (game.Config.Height + 1),
                    Speed = game.Config.HeavyPushSpeed, Slide = game.Config.HeavySlideSeconds });
        }
        private void LateUpdate()
        {
            if (game == null || !game.Combat.Active || game.Elapsed < 7 || complete) return;
            for (int i = 0; i < 2; i++)
                if ((SumoMotion)Motion.GetValue(poses[i]) == SumoMotion.Stumble && game.Combat.FighterAt(i).ContactAsTarget) seen |= 1 << i;
            if (seen == 3 && !captured && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                captured = true;
                var dir = System.IO.Path.GetDirectoryName(Application.consoleLogPath);
                if (!string.IsNullOrEmpty(dir)) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "sumo-mutual-heavy.png"));
            }
            if (game.Elapsed < 10) return;
            complete = true;
            bool ok = contacts == 2 && seen == 3;
            Debug.Log("SUMO_MUTUAL " + (ok ? "PASS" : "FAIL") + " contacts=" + contacts + " stumbleMask=" + seen);
            var local = game.Combat.Local;
            local.Motor.TeleportTo(new Vector3(local.Id == 0 ? -1.3f : 1.3f, game.Config.Height + .025f, 0), Quaternion.identity);
            local.ResetCombat();
        }
    }
}
