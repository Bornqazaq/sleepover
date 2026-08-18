using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Возврат персонажа в назначенную точку. Точку задаёт сцена/мини-игра
    /// (спавн-система), не хардкод. Вызывается KillZone или правилами игры.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerRespawner : MonoBehaviour
    {
        [Tooltip("Куда возвращать. Назначается сценой или спавн-системой")]
        [SerializeField] private Transform respawnPoint;

        private PlayerController motor;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
        }

        /// <summary>
        /// Текущая точка. Нужна тем, кто её временно переопределяет: мини-игра
        /// обязана вернуть прежнюю в конце раунда, иначе персонаж уедет в хаб
        /// с точкой респавна внутри уже выгруженной сцены.
        /// </summary>
        public Transform RespawnPoint => respawnPoint;

        public void SetRespawnPoint(Transform point) => respawnPoint = point;

        public void Respawn()
        {
            if (respawnPoint == null)
            {
                Debug.LogWarning($"{name}: точка респауна не назначена — остаюсь на месте.", this);
                return;
            }

            motor.RequestTeleport(respawnPoint.position, respawnPoint.rotation);
        }
    }
}
