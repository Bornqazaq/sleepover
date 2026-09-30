using Igruha.Core.Audio;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    [RequireComponent(typeof(MinigameAudioPlayer))]
    public sealed class MosquitoesAudio : MonoBehaviour
    {
        [SerializeField] private MinigameSfxLibrary library;
        private MinigameAudioPlayer player;
        private void Awake() { player = GetComponent<MinigameAudioPlayer>(); player.AddLibrary(library); }
        public void Begin() { if (Available("room_ambience")) player.StartLoop("room_ambience"); }
        public void End() => player?.StopAll();
        public void LastTenSeconds() { if (Available("sleep_final_ten")) player.Play("sleep_final_ten"); }
        public void Effect(byte kind, Vector3 position)
        {
            string slot = kind == 0 ? "bite" : kind == 1 ? "lamp_switch" : kind == 2 ? "mosquito_death" : "bed_creak";
            if (Available(slot)) player.PlayAt(slot, position);
        }
        public void ConfigureBuzz(AudioSource source)
        {
            if (source == null || library == null || !library.TryGet("buzz_loop", out var entry)) return;
            source.clip = entry.Clip;
        }
        private bool Available(string slot) => player != null && library != null && library.TryGet(slot, out var entry) &&
            (entry.Clip != null || entry.Variants != null && entry.Variants.Length > 0);
        private void OnDisable() => End();
    }
}
