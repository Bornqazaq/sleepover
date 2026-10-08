using Igruha.Core.Audio;
using UnityEngine;

namespace Igruha.Minigames.OneBullet
{
    /// <summary>Presentation only: these events already reach every peer.</summary>
    public sealed class OneBulletAudio : MonoBehaviour
    {
        [SerializeField] private OneBulletMinigame game;
        [SerializeField] private MinigameAudioPlayer audioPlayer;
        private OneBulletDecoys decoys;
        private OneBulletStorm storm;
        private void Awake() { decoys = game.GetComponent<OneBulletDecoys>(); storm = game.GetComponent<OneBulletStorm>(); }
        private void CanImpact(Vector3 point, float speed) => audioPlayer.PlayAt("ob_can_hit", point, Mathf.Clamp01(speed / 5f), game.Config.CanSoundRange);
        private void StormChanged() { if (storm.State.Warning && game.GameplayActive) audioPlayer.Play("ob_storm_warning"); }
        private void OnEnable() { game.Shot += Shot; game.PickedUp += Pickup; game.Died += Death; if (decoys != null) decoys.Impact += CanImpact; if (storm != null) storm.Changed += StormChanged; }
        private void OnDisable() { game.Shot -= Shot; game.PickedUp -= Pickup; game.Died -= Death; if (decoys != null) decoys.Impact -= CanImpact; if (storm != null) storm.Changed -= StormChanged; audioPlayer.StopAll(); }
        private void Shot(Vector3 origin, Vector3 end, bool hit)
        {
            audioPlayer.PlayAt("ob_shot", origin);
            if (!hit && Vector3.Distance(origin, end) < game.Config.ShotRange - .1f)
                audioPlayer.PlayAt("ob_stone_hit", end, .55f, 12f);
        }
        private void Pickup(int id) { if (id == game.LocalId) audioPlayer.Play("ob_pickup"); }
        private void Death(int id, Vector3 position) => audioPlayer.PlayAt("ob_death", position);
    }
}
