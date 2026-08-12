using Unity.Netcode;
using UnityEngine;

namespace Igruha.Networking
{
    /// <summary>
    /// Обрабатывает отключения клиентов и выходы хоста.
    ///
    /// IGR-57: Обработка дисконнекта игрока — матч не должен крашиться
    /// IGR-58: Обработка выхода хоста — все возвращаются в меню
    ///
    /// По CLAUDE.md 3.4: обработка выхода должна быть graceful.
    /// </summary>
    public class DisconnectionHandler : MonoBehaviour
    {
        private NetworkManager networkManager;

        private void Start()
        {
            networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogError("❌ DisconnectionHandler: NetworkManager not found!");
                return;
            }

            // Подписаться на события отключения
            networkManager.OnClientDisconnectCallback += OnClientDisconnect;
            networkManager.OnServerStopped += OnServerStopped;

            Debug.Log("✅ DisconnectionHandler: Ready for disconnections");
        }

        private void OnDestroy()
        {
            if (networkManager != null)
            {
                networkManager.OnClientDisconnectCallback -= OnClientDisconnect;
                networkManager.OnServerStopped -= OnServerStopped;
            }
        }

        // ========== CLIENT DISCONNECT HANDLING (IGR-57) ==========

        /// <summary>
        /// Callback: клиент отключился.
        /// Вызывается на сервере когда клиент выходит/разорвал соединение.
        /// </summary>
        private void OnClientDisconnect(ulong clientId)
        {
            if (!networkManager.IsServer)
                return;

            Debug.LogWarning($"🚪 CLIENT DISCONNECTED: ClientId={clientId}");

            // NGO сам despawn'ит PlayerObject отключившегося клиента,
            // здесь только логируем факт для отладки
            var playerObject = networkManager.SpawnManager.GetPlayerNetworkObject(clientId);
            if (playerObject != null)
            {
                Debug.Log($"   └─ Player object for ClientId={clientId} will be despawned by NGO");
            }

            // Проверить достаточно ли игроков осталось
            int remainingPlayers = networkManager.ConnectedClients.Count - 1; // -1 для self
            Debug.Log($"   └─ Remaining players: {remainingPlayers}");

            // TODO (future): Если осталось < 2 игроков → вернуться в лобби
            // if (remainingPlayers < 2)
            // {
            //     ReturnToLobbyServerRpc();
            // }
        }

        // ========== HOST DISCONNECT HANDLING (IGR-58) ==========

        /// <summary>
        /// Callback: хост остановился (выходит из игры или крашится).
        /// Вызывается на всех клиентах когда сервер прекращает работу.
        /// </summary>
        private void OnServerStopped(bool wasHost)
        {
            Debug.LogWarning($"⚠️  SERVER STOPPED (WasHost={wasHost})");

            if (wasHost)
            {
                // Хост остановил сервер — все должны вернуться в меню
                ReturnToMainMenu("Host left the game");
            }
            else
            {
                // Я был клиентом и потерял соединение с сервером
                ReturnToMainMenu("Connection lost to host");
            }
        }

        /// <summary>
        /// Внутренний: вернуться в главное меню с сообщением.
        /// </summary>
        private void ReturnToMainMenu(string reason)
        {
            Debug.Log($"🔄 Returning to main menu: {reason}");

            // TODO: Загрузить главное меню сцену
            // UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");

            // Убедиться что NetworkManager остановлен
            if (networkManager != null && (networkManager.IsServer || networkManager.IsClient))
            {
                networkManager.Shutdown();
            }

            // Показать сообщение пользователю
            Debug.Log($"   └─ Reason: {reason}");
            // TODO: UIManager.Instance.ShowDisconnectMessage(reason);
        }

        // ========== GRACEFUL SHUTDOWN ==========

        /// <summary>
        /// Корректный выход хоста (когда хост сам инициирует выход).
        /// </summary>
        public void GracefulHostShutdown()
        {
            if (!networkManager.IsHost)
            {
                Debug.LogWarning("Not host, cannot shutdown");
                return;
            }

            Debug.Log("🛑 Host: Initiating graceful shutdown...");

            // Клиенты узнают о выходе хоста через OnServerStopped,
            // поэтому отдельное оповещение не нужно
            Invoke(nameof(ActuallyShutdown), 0.5f);
        }

        private void ActuallyShutdown()
        {
            if (networkManager.IsListening)
            {
                networkManager.Shutdown();
                Debug.Log("✅ Network shut down");
            }

            ReturnToMainMenu("Host shutdown");
        }

        /// <summary>
        /// Корректный выход клиента (когда клиент сам инициирует выход).
        /// </summary>
        public void GracefulClientDisconnect()
        {
            if (!networkManager.IsClient || networkManager.IsHost)
            {
                Debug.LogWarning("Not a pure client, cannot disconnect gracefully");
                return;
            }

            Debug.Log("🛑 Client: Initiating graceful disconnect...");

            if (networkManager.IsListening)
            {
                networkManager.Shutdown();
                Debug.Log("✅ Disconnected from host");
            }

            ReturnToMainMenu("Disconnected by player");
        }
    }
}
