using UnityEngine;
using UnityEngine.Events;
using Igruha.Core.Player;

namespace Igruha.Core.Arena
{
    /// <summary>
    /// Зона смерти/падения. Игрок попал → комичное падение уже произошло
    /// (он летел), дальше по режиму: респаун или только событие (выбывание
    /// решают правила мини-игры через onPlayerEntered).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class KillZone : MonoBehaviour
    {
        public enum ZoneMode
        {
            /// <summary>Вернуть игрока в его точку респауна.</summary>
            Respawn,
            /// <summary>Только событие — правила мини-игры решают сами (выбывание и т.п.).</summary>
            EventOnly
        }

        [SerializeField] private ZoneMode mode = ZoneMode.Respawn;
        [Tooltip("Событие для правил мини-игры: кто попал в зону")]
        [SerializeField] private UnityEvent<PlayerController> onPlayerEntered;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null)
            {
                return;
            }

            // Зона срабатывает на каждой машине матча, а факт смерти — исход,
            // который решает сервер. Без этой проверки правила мини-игры получат
            // событие столько раз, сколько в матче игроков.
            if (!player.HasWorldAuthority)
            {
                return;
            }

            onPlayerEntered?.Invoke(player);

            if (mode == ZoneMode.Respawn &&
                player.TryGetComponent(out PlayerRespawner respawner))
            {
                respawner.Respawn();
            }
        }
    }
}
