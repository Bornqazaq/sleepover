using System.Collections;
using System.Collections.Generic;
using Igruha.Core.Session;
using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Temporary prerecorded Russian TTS. Called from replicated spill state, never a sound RPC.</summary>
    public sealed class CarryCartVoice : MonoBehaviour
    {
        [SerializeField] private AudioClip disagreement;
        [SerializeField] private AudioClip turn;
        [SerializeField] private AudioClip release;
        [SerializeField] private AudioClip impact;
        [SerializeField] private AudioClip brake;
        [SerializeField] private AudioClip boss;
        [SerializeField] private AudioClip aza;
        [SerializeField] private AudioClip[] numberedPlayers;
        private readonly Queue<AudioClip> queue = new Queue<AudioClip>();
        private AudioSource source;
        private Coroutine playback;

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.75f;
        }

        public void Announce(CartTiltCause cause, int responsible)
        {
            if (queue.Count > 8) return;
            switch (cause)
            {
                case CartTiltCause.Disagreement: Enqueue(disagreement); Enqueue(NameClip(responsible)); break;
                case CartTiltCause.Turn: Enqueue(turn); break;
                case CartTiltCause.Brake: Enqueue(brake); break;
                case CartTiltCause.Release: Enqueue(NameClip(responsible)); Enqueue(release); break;
                default: Enqueue(impact); break;
            }
            if (playback == null && queue.Count > 0) playback = StartCoroutine(Play());
        }

        private AudioClip NameClip(int id)
        {
            string name = WaterCartStability.PlayerName(id).Trim().ToLowerInvariant();
            if (name == "boss" || name == "босс") return boss;
            if (name == "aza" || name == "аза") return aza;
            var players = SessionScoreboard.Current?.Players;
            if (players != null && numberedPlayers != null)
                for (int i = 0; i < players.Count && i < numberedPlayers.Length; i++)
                    if (players[i].Id == id) return numberedPlayers[i];
            return null;
        }

        private void Enqueue(AudioClip clip) { if (clip != null) queue.Enqueue(clip); }
        private IEnumerator Play()
        {
            while (queue.Count > 0)
            {
                source.clip = queue.Dequeue();
                source.Play();
                yield return new WaitForSecondsRealtime(source.clip.length + 0.06f);
            }
            playback = null;
        }

        private void OnDisable()
        {
            if (playback != null) StopCoroutine(playback);
            playback = null;
            queue.Clear();
            if (source != null) source.Stop();
        }
    }
}
