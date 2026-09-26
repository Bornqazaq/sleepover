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
        private float nextReaction;
        private void OnEnable()
        {
            game.FightStarted += StartFight; game.FightEnded += FinishFight; game.Eliminated += React;
            arena.Warning += Warn; arena.Collapse += Collapse;
        }
        private void OnDisable()
        {
            game.FightStarted -= StartFight; game.FightEnded -= FinishFight; game.Eliminated -= React;
            arena.Warning -= Warn; arena.Collapse -= Collapse; audioPlayer.StopAll();
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
