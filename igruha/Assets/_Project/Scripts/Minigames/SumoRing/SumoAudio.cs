using Igruha.Core.Audio;
using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    /// <summary>Audio follows already replicated events; no extra network messages.</summary>
    public sealed class SumoAudio : MonoBehaviour
    {
        [SerializeField] private SumoMinigame game;
        [SerializeField] private SumoArena arena;
        [SerializeField] private MinigameAudioPlayer audioPlayer;
        private SumoCombat combat;
        private float nextReaction;
        private double[] lastDash = System.Array.Empty<double>();
        private void OnEnable()
        {
            game.FightStarted += StartFight; game.FightEnded += FinishFight; game.Eliminated += React;
            arena.Warning += Warn; arena.Collapse += Collapse;
            BindCombat();
        }
        private void Start() => BindCombat();
        private void BindCombat()
        {
            if (combat != null || game.Combat == null) return;
            combat = game.Combat; combat.Contact += OnContact; combat.StateObserved += OnState;
        }
        private void OnDisable()
        {
            game.FightStarted -= StartFight; game.FightEnded -= FinishFight; game.Eliminated -= React;
            arena.Warning -= Warn; arena.Collapse -= Collapse; audioPlayer.StopAll();
            if (combat != null) { combat.Contact -= OnContact; combat.StateObserved -= OnState; }
            combat = null;
        }
        public static string ContactSlot(in SumoCombatHit hit)
        {
            if (hit.Contact == SumoContact.Miss) return null;
            if (hit.Contact == SumoContact.Parry) return "sumo_parry";
            if (hit.Contact == SumoContact.GuardBreak) return "sumo_break";
            if (hit.Contact == SumoContact.Block) return "sumo_block";
            return hit.Attack == SumoAttack.Quick ? "sumo_hit" : "sumo_heavy";
        }
        private void OnState(SumoCombatState state)
        {
            if (state.Attack != SumoAttack.Dash || state.Phase != SumoCombatPhase.Windup) return;
            if (lastDash.Length != combat.Count) lastDash = new double[combat.Count];
            int index = combat.IndexOf(state.Id);
            if (index < 0 || state.DashAt <= lastDash[index]) return;
            lastDash[index] = state.DashAt;
            audioPlayer.PlayAt("sumo_dash", combat.FighterAt(index).Motor.Position);
        }
        private void OnContact(SumoCombatHit hit)
        {
            string slot = ContactSlot(hit);
            if (slot != null) audioPlayer.PlayAt(slot, hit.Point);
        }
        private void StartFight() { audioPlayer.Play("sumo_start"); audioPlayer.StartLoop("sumo_crowd"); }
        private void FinishFight() { audioPlayer.StopAll(); audioPlayer.Play("sumo_final"); }
        private void Warn(int ring) => audioPlayer.Play("sumo_warning");
        private void Collapse(int ring) => audioPlayer.Play("sumo_collapse");
        private void React(int id, Vector3 position)
        {
            if (Time.unscaledTime < nextReaction) return;
            nextReaction = Time.unscaledTime + 1.2f; audioPlayer.PlayAt("sumo_react", position);
        }
    }
}
