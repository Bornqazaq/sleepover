using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.SumoRing;
using UnityEngine;
using Igruha.Core.Audio;

namespace Igruha.Tests
{
    /// <summary>Opt-in real transport test. Both host and client attack/defend; observations include owner physics.</summary>
    public sealed class SumoCombatProbe : MonoBehaviour
    {
        private static readonly string[] Cases = { "quick", "front-block", "rear-bypass", "parry", "heavy-break", "heavy-parry", "counter", "expired-counter", "miss-recovery", "cancel-and-forged-owner", "client-counter" };
        private const double FirstCase = 4, CaseSeconds = 4;
        private SumoMinigame game;
        private SumoFighter local;
        private int current = -1, passed;
        private bool prepared, guarded, down, up, answered, retried, checkedCase, cancelled, finished;
        private readonly int[,] counts = new int[11, 7];
        private Vector3 targetStart;
        private double counterExpiry;
        private bool screenshot;
        private AudioSource[] voices;
        private MinigameSfxLibrary sounds;
        private string pendingImpactCapture;
        private float impactCaptureAt;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--sumo-check", out string scenario) || scenario != "combat" || LaunchArguments.TryGetValue("--sumo-mutual-check", out _)) return;
            var go = new GameObject("SumoCombatProbe"); DontDestroyOnLoad(go); go.AddComponent<SumoCombatProbe>();
        }
        private void Update()
        {
            var next = MinigameControllerBase.Current as SumoMinigame;
            if (next == null || next == game || next.Combat == null) return;
            if (game != null) game.Combat.Contact -= Observe;
            game = next; local = null; current = -1; passed = 0; finished = false;
            var audio = game.GetComponent<MinigameAudioPlayer>();
            voices = audio.GetComponentsInChildren<AudioSource>();
            sounds = (MinigameSfxLibrary)typeof(MinigameAudioPlayer).GetField("library", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(audio);
            System.Array.Clear(counts, 0, counts.Length); game.Combat.Contact += Observe;
        }
        private void Observe(SumoCombatHit hit)
        {
            if (game == null || game.Phase != MinigamePhase.Round) return;
            int c = (int)((game.Elapsed - FirstCase) / CaseSeconds);
            if (c < 0 || c >= Cases.Length) return;
            counts[c, (int)hit.Contact]++;
            string slot = SumoAudio.ContactSlot(hit);
            if (slot != null)
            {
                bool played = false;
                if (sounds.TryGet(slot, out var entry))
                    foreach (var voice in voices)
                        if (voice.clip != null && (voice.clip == entry.Clip || (entry.Variants != null && System.Array.IndexOf(entry.Variants, voice.clip) >= 0))
                            && (voice.transform.position - hit.Point).sqrMagnitude < .001f) played = true;
                Debug.Log("SUMO_FEEDBACK " + (played ? "PASS" : "FAIL") + " sound=" + slot + " case=" + Cases[c]);
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                { pendingImpactCapture = slot; impactCaptureAt = Time.time + .06f; }
            }
            Debug.Log("SUMO_COMBAT contact case=" + Cases[c] + " kind=" + hit.Contact + " attacker=" + hit.Attacker + " target=" + hit.Target + " at=" + game.Elapsed.ToString("F3"));
        }
        private void LateUpdate()
        {
            if (pendingImpactCapture == null || Time.time < impactCaptureAt) return;
            var dir = System.IO.Path.GetDirectoryName(Application.consoleLogPath);
            if (!string.IsNullOrEmpty(dir)) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "sumo-feedback-" + pendingImpactCapture + ".png"));
            pendingImpactCapture = null;
        }
        private void FixedUpdate()
        {
            if (game == null || game.Phase != MinigamePhase.Round || !game.Combat.Active) return;
            if (local == null) local = game.Combat.Local;
            if (local == null || local.Participant.Dead) return;
            local.Input.DriveMove(Vector2.zero);
            double time = game.Elapsed;
            int c = (int)System.Math.Floor((time - FirstCase) / CaseSeconds);
            if (c < 0) { Place(local.Id, false, false); return; }
            if (c >= Cases.Length)
            {
                if (!finished) { finished = true; Place(local.Id, local.Id == 1, false); local.ResetCombat(); Debug.Log("SUMO_COMBAT COMPLETE passed=" + passed + "/" + Cases.Length); }
                return;
            }
            double t = time - FirstCase - c * CaseSeconds;
            int attacker = c == 10 ? 0 : (c % 2 == 0 ? 1 : 0), defender = 1 - attacker;
            bool isAttacker = local.Id == attacker, isDefender = local.Id == defender;
            if (c != current)
            {
                counterExpiry = 0; screenshot = false; current = c; prepared = guarded = down = up = answered = retried = checkedCase = cancelled = false;
            }
            if (t < .35) { Place(local.Id, isAttacker, c == 8 && isDefender); local.ResetCombat(); return; }
            if (!prepared)
            {
                prepared = true; Send(SumoCommand.GuardUp, isAttacker ? 0 : 180);
                if (isDefender) targetStart = local.Motor.Position;
            }
            bool heavy = c == 4 || c == 5;
            bool parry = c == 3 || c == 5 || c == 6 || c == 7 || c == 10;
            double guardAt = .43;
            int otherIndex = game.Combat.IndexOf(attacker);
            // LocalTime estimates when our input reaches the server; ServerTime is buffered
            // presentation time. Guard late in the windup, not before the .2 s window can cover it.
            double arrival = Unity.Netcode.NetworkManager.Singleton.LocalTime.Time;
            bool seenWindup = otherIndex >= 0 && game.Combat.StateAt(otherIndex).Phase == SumoCombatPhase.Windup
                && game.Combat.StateAt(otherIndex).Until - arrival <= .12;
            // Under latency, the fast host shove can arrive before its windup is rendered.
            // Scripted defender anticipates the scheduled attack, just as a player can read
            // an opponent's approach. The host still validates the actual arrival window.
            double inputTime = arrival - game.Round.BeginsAt - FirstCase - c * CaseSeconds;
            bool parryReady = attacker == 0 ? inputTime >= (heavy ? 1.40 : .84) : seenWindup;
            if (isDefender && c != 0 && c != 8 && c != 9 && (parry ? parryReady : t >= guardAt) && !guarded)
            { guarded = true; Send(SumoCommand.GuardDown, c == 2 ? 0 : 180); Debug.Log("SUMO_COMBAT guard case=" + Cases[c] + " at=" + game.Elapsed.ToString("F3")); }
            if (isAttacker && t >= (heavy || c == 9 ? .45 : .65) && !down)
            { down = true; Send(SumoCommand.AttackDown, 0); }
            if (isAttacker && c == 9 && t >= 1.2 && !cancelled)
            { cancelled = true; Send(SumoCommand.Cancel, 0); }
            if (isAttacker && t >= (heavy ? 1.15 : c == 9 ? 1.3 : .66) && !up)
            { up = true; Send(SumoCommand.AttackUp, 0); }
            if (local.State.HasCounter(NetworkClock.Now)) counterExpiry = local.State.CounterUntil;
            bool answerReady = c == 7 ? counterExpiry > 0 && NetworkClock.Now > counterExpiry + .15 : local.State.HasCounter(NetworkClock.Now);
            if (isDefender && (c == 6 || c == 7 || c == 10) && answerReady && !answered)
            { answered = true; Send(SumoCommand.AttackDown, 180); Send(SumoCommand.AttackUp, 180); }
            if (isAttacker && c == 8 && t >= 1.02 && !retried)
            { retried = true; Send(SumoCommand.AttackDown, 0); Send(SumoCommand.AttackUp, 0); }
            if (isAttacker && c == 9 && t >= 1.5 && !retried)
            { retried = true; game.Combat.Submit(defender, SumoCommand.AttackDown, 180); game.Combat.Submit(defender, SumoCommand.AttackUp, 180); }
            if (!screenshot && local.Id == 0 && t >= .62)
            { screenshot = true; var dir = System.IO.Path.GetDirectoryName(Application.consoleLogPath); if (!string.IsNullOrEmpty(dir)) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "sumo-" + Cases[c] + ".png")); }
            if (t >= 2.9 && !checkedCase)
            {
                checkedCase = true; bool ok = Expected(c);
                float distance = isDefender ? Vector3.Distance(local.Motor.Position, targetStart) : 0;
                if (isDefender && (c == 0 || c == 2)) ok &= distance > .45f && !local.Motor.IsKnockedDown;
                if (isDefender && c == 1) ok &= distance < .7f;
                if (isDefender && c == 4) ok &= distance > 1.1f;
                if (ok) passed++;
                Debug.Log((ok ? "SUMO_COMBAT PASS " : "SUMO_COMBAT FAIL ") + Cases[c] + " owner=" + local.Id + " displacement=" + distance.ToString("F2"));
                Send(SumoCommand.GuardUp, isAttacker ? 0 : 180);
            }
        }
        private void Send(SumoCommand command, float yaw) => game.Combat.Submit(local.Id, command, yaw);
        private void Place(int id, bool attacker, bool far)
        {
            var p = id >= 2 ? new Vector3(1.45f * Mathf.Cos(id), 0, 1.45f * Mathf.Sin(id)) : new Vector3(0, 0, attacker ? -.65f : far ? 2.8f : .35f);
            p.y = game.Config.Height + .025f;
            local.Motor.TeleportTo(p, Quaternion.Euler(0, attacker ? 0 : 180, 0));
        }
        private bool Expected(int c)
        {
            int total = 0; for (int i = 0; i < 7; i++) total += counts[c, i];
            if (c == 9) return total == 0;
            if (c == 6 || c == 10) return total == 2 && counts[c, (int)SumoContact.Parry] == 1 && counts[c, (int)SumoContact.Counter] == 1;
            if (c == 7) return total == 2 && counts[c, (int)SumoContact.Parry] == 1 && counts[c, (int)SumoContact.Push] == 1;
            var expected = c == 1 ? SumoContact.Block : c == 3 || c == 5 ? SumoContact.Parry : c == 4 ? SumoContact.GuardBreak : c == 8 ? SumoContact.Miss : SumoContact.Push;
            return total == 1 && counts[c, (int)expected] == 1;
        }
    }
}
