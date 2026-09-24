using Igruha.Core.Audio;
using UnityEngine;

namespace Igruha.Minigames.OneBullet
{
    /// <summary>Presentation only: these events already reach every peer.</summary>
    public sealed class OneBulletAudio : MonoBehaviour
    {
        [SerializeField] private OneBulletMinigame game;
        [SerializeField] private MinigameAudioPlayer audioPlayer;
        private void OnEnable() { game.Shot += Shot; game.PickedUp += Pickup; game.Died += Death; }
        private void OnDisable() { game.Shot -= Shot; game.PickedUp -= Pickup; game.Died -= Death; audioPlayer.StopAll(); }
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
