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
        /// Метод для толчка персонажа через сеть (future: будет ServerRpc).
        /// Сейчас вызывает локально, позже будет идти через сервер.
        /// </summary>
        public void NetworkApplyPush(Vector3 direction, float force)
        {
            if (playerController != null && !playerController.IsKnockedDown)
            {
                playerController.ApplyPush(direction, force);
                Debug.Log($"💥 [{name}] ApplyPush({direction.normalized}, {force})");
            }
        }

        // TODO (IGR-53): Добавить ServerRpc для толчков
        // [ServerRpc]
        // private void ApplyPushServerRpc(Vector3 direction, float force)
        // {
        //     playerController.ApplyPush(direction, force);
        //     // Сервер реплицирует результат толчка через NetworkTransform
        // }
    }
}
