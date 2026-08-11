using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Networking
{
    /// <summary>
    /// Сетевая обёртка для PlayerController.
    /// Управляет синхронизацией и проверяет права на управление.
    /// </summary>
    public sealed class NetworkPlayerController : NetworkBehaviour
    {
        private PlayerController playerController;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (playerController == null)
            {
                Debug.LogError($"{name}: NetworkPlayerController не нашел PlayerController!", this);
                return;
            }

            if (!IsOwner)
            {
                playerController.enabled = false;
                Debug.Log($"{name}: это не мой персонаж, отключаю ввод");
            }
            else
            {
                Debug.Log($"{name}: это МОЙ персонаж, ввод включен!");
            }
        }
    }
}
