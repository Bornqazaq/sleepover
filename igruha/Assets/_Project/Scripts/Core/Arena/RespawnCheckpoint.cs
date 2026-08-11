using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Arena
{
    /// <summary>
    /// Чекпоинт: игрок прошёл — сюда его вернёт респаун. Переиспользуется
    /// любой мини-игрой с прогрессией (Duck Hunt, Рейс на память, Экзамен).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class RespawnCheckpoint : MonoBehaviour
    {
        [Tooltip("Куда возвращать игрока. Пусто — сам объект чекпоинта")]
        [SerializeField] private Transform respawnAnchor;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerRespawner respawner = other.GetComponentInParent<PlayerRespawner>();
            if (respawner != null)
            {
                respawner.SetRespawnPoint(respawnAnchor != null ? respawnAnchor : transform);
            }
        }
    }
}
