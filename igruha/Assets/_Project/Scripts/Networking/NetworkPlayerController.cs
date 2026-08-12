using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Networking
{
    /// <summary>
    /// Сетевая обёртка для PlayerController.
    /// Server-authoritative модель по CLAUDE.md 3:
    /// - Только владелец (IsOwner) обрабатывает ввод и применяет движение локально
    /// - NetworkTransform реплицирует позицию/ротацию всем остальным
    /// - Все толчки (ApplyPush) идут через ServerRpc для валидации на сервере
    /// </summary>
    public sealed class NetworkPlayerController : NetworkBehaviour
    {
        private PlayerController playerController;
        private NetworkTransform networkTransform;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            networkTransform = GetComponent<NetworkTransform>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (playerController == null)
            {
                Debug.LogError($"{name}: NetworkPlayerController не нашел PlayerController!", this);
                return;
            }

            if (networkTransform == null)
            {
                Debug.LogWarning($"{name}: NetworkPlayerController не нашел NetworkTransform!", this);
            }

            if (!IsOwner)
            {
                // Удалённый персонаж — отключаем локальный ввод
                // Движение синхронизируется через NetworkTransform
                playerController.enabled = false;
                Debug.Log($"📡 [{name}] Это удалённый персонаж (владелец другого клиента) — синхронизация через NetworkTransform");
            }
            else
            {
                // Мой персонаж — ввод включен, движение синхронизируется через NetworkTransform
                Debug.Log($"🎮 [{name}] Это МОЙ персонаж — ввод активен, позиция будет реплицирована");
            }
        }

        /// <summary>
        /// Вызвать толчок персонажа через сеть (клиент → сервер).
        /// Клиент просит толчок, сервер применяет и реплицирует всем.
        /// </summary>
        public void NetworkApplyPush(Vector3 direction, float force)
        {
            // Клиент отправляет запрос серверу
            ApplyPushServerRpc(direction, force);
        }

        /// <summary>
        /// ServerRpc: только владелец может вызвать, сервер обрабатывает.
        /// </summary>
        [ServerRpc(RequireOwnershipck = true)]
        private void ApplyPushServerRpc(Vector3 direction, float force)
        {
            if (playerController != null && !playerController.IsKnockedDown)
            {
                // Сервер применяет толчок
                playerController.ApplyPush(direction, force);

                // Логирование для отладки
                Debug.Log($"💥 [{name}] ServerRpc ApplyPush — direction={direction.normalized}, force={force}");

                // Результат толчка (изменение Rigidbody.linearVelocity и .rotation)
                // автоматически реплицируется через NetworkTransform на всех клиентов
            }
        }

        /// <summary>
        /// ServerRpc: толчок произвольного направления (пружины, взрывы).
        /// </summary>
        [ServerRpc(RequireOwnershipck = true)]
        public void ApplyImpulseServerRpc(Vector3 impulse)
        {
            if (playerController != null)
            {
                playerController.ApplyImpulse(impulse);
                Debug.Log($"💫 [{name}] ServerRpc ApplyImpulse — impulse={impulse.magnitude:F2}");
            }
        }
    }
}
