using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

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
            networkManager.OnClientStopped += OnClientStopped;

            Debug.Log("✅ DisconnectionHandler: Ready for disconnections");
        }

        private void OnDestroy()
        {
            if (networkManager != null)
            {
                networkManager.OnClientDisconnectCallback -= OnClientDisconnect;
                networkManager.OnServerStopped -= OnServerStopped;
                networkManager.OnClientStopped -= OnClientStopped;
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

            // Выключение сервера рассылает этот колбэк на каждого подключённого.
            // Это не уход игрока, а конец сессии: SpawnManager уже разобран,
            // и обращение к нему падает в NullReference. Выход хоста разбирает
            // OnServerStopped, ему тут делать нечего.
            //
            // Признак именно IsListening: выход из play-режима зовёт
            // ShutdownInternal напрямую, минуя Shutdown(), поэтому
            // ShutdownInProgress на этом колбэке ещё false (замерено 15.08).
            if (!networkManager.IsListening || networkManager.ShutdownInProgress)
                return;

            Debug.LogWarning($"🚪 CLIENT DISCONNECTED: ClientId={clientId}");

            // NGO сам despawn'ит PlayerObject отключившегося клиента,
            // здесь только логируем факт для отладки
            var playerObject = networkManager.SpawnManager != null
                ? networkManager.SpawnManager.GetPlayerNetworkObject(clientId)
                : null;
            if (playerObject != null)
            {
                Debug.Log($"   └─ Player object for ClientId={clientId} will be despawned by NGO");
            }

            // К моменту вызова NGO уже удалил ушедшего из ConnectedClients,
            // поэтому Count — это и есть остаток. Хост тоже входит в число игроков.
            int remainingPlayers = networkManager.ConnectedClients.Count;
            Debug.Log($"   └─ Remaining players: {remainingPlayers}");

            // TODO (future): Если осталось < 2 игроков → вернуться в лобби
            // if (remainingPlayers < 2)
            // {
            //     ReturnToLobbyServerRpc();
            // }
        }

        // ========== HOST DISCONNECT HANDLING (IGR-58) ==========

        /// <summary>
        /// Callback: на ЭТОЙ машине остановился сервер. Вопреки прежнему
        /// комментарию, на чужие машины это событие не приходит: NGO поднимает
        /// его только там, где сервер крутился.
        /// </summary>
        private void OnServerStopped(bool wasHost)
        {
            Debug.LogWarning($"⚠️  SERVER STOPPED (WasHost={wasHost})");

            ReturnToMainMenu(wasHost ? "Host shut down" : "Server shut down");
        }

        /// <summary>
        /// Callback: на этой машине кончилась клиентская сессия — хост вышел,
        /// упал или порвалась связь.
        ///
        /// Для чистого клиента это единственный сигнал о конце матча.
        /// Прежде обработчик ждал <see cref="OnServerStopped"/>, которого
        /// клиенту не видать никогда, и уход хоста проходил мимо: замерено
        /// на стенде 19.08 — хост закрылся, а клиент остался стоять в хабе
        /// с мёртвым соединением, без единой строки в логе.
        /// </summary>
        private void OnClientStopped(bool wasHost)
        {
            // У хоста та же остановка уже разобрана в OnServerStopped:
            // он и сервер, и клиент, и оба события приходят парой.
            if (wasHost)
            {
                return;
            }

            Debug.LogWarning("⚠️  CLIENT STOPPED — хост больше не отвечает");
            ReturnToMainMenu("Host left the game");
        }

        /// <summary>
        /// Внутренний: вернуться в главное меню с сообщением.
        /// </summary>
        private void ReturnToMainMenu(string reason)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Debug.Log($"🔄 Returning to hub: {reason}");

            if (networkManager != null && (networkManager.IsServer || networkManager.IsClient))
            {
                networkManager.Shutdown();
            }

            // NetworkManager живёт в DontDestroyOnLoad — без уничтожения
            // следующая загрузка Boot создаст второй экземпляр.
            if (networkManager != null)
            {
                Destroy(networkManager.gameObject);
            }

            SceneManager.LoadScene("Hub");
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
