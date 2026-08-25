using UnityEngine;
using Unity.Netcode;

namespace Igruha.Networking
{
    /// <summary>
    /// Управляет одобрением подключений (Connection Approval) на сервере.
    ///
    /// Server-Authority (CLAUDE.md 3.1):
    /// - Сервер валидирует каждое подключение перед спавном игрока
    /// - Отклоняет неправильные версии протокола, читерские данные, и т.д.
    /// - Защита от неавторизованных клиентов на уровне NGO
    /// </summary>
    public class ConnectionApprovalManager : MonoBehaviour
    {
        private const int PROTOCOL_VERSION = 1;
        private const int MAX_PLAYERS = 8;

        // Awake, а не Start: колбэк должен быть зарегистрирован до того, как
        // AppNetworkManager поднимет хост или клиент в своём Start().
        private void Awake()
        {
            // Singleton выставляется в NetworkManager.Awake, порядок Awake между
            // компонентами не гарантирован — поэтому есть запасной путь
            var networkManager = NetworkManager.Singleton != null
                ? NetworkManager.Singleton
                : GetComponent<NetworkManager>();

            if (networkManager == null)
            {
                Debug.LogError("❌ ConnectionApprovalManager: NetworkManager not found!");
                return;
            }

            // Включить Connection Approval
            networkManager.NetworkConfig.ConnectionApproval = true;

            // Хост тоже проходит одобрение, поэтому payload нужен и ему
            networkManager.NetworkConfig.ConnectionData = GetConnectionPayload();

            // Установить callback для валидации подключений
            networkManager.ConnectionApprovalCallback += HandleConnectionApproval;

            Debug.Log("✅ ConnectionApprovalManager: Connection Approval enabled");
        }

        private void OnDestroy()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                networkManager.ConnectionApprovalCallback -= HandleConnectionApproval;
            }
        }

        /// <summary>
        /// Callback, вызываемый сервером для каждого нового подключения.
        /// Здесь мы валидируем клиента и одобряем или отклоняем подключение.
        /// </summary>
        private void HandleConnectionApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            // По умолчанию — отклонить
            response.Approved = false;
            response.CreatePlayerObject = false;

            // Проверка 1: Версия протокола
            if (!ValidateProtocolVersion(request.ClientNetworkId, request.Payload))
            {
                Debug.LogWarning($"❌ Client {request.ClientNetworkId}: Protocol version mismatch");
                response.Reason = "Protocol version mismatch";
                return;
            }

            // Проверка 2: Максимальное количество игроков
            int connectedPlayers = NetworkManager.Singleton.ConnectedClients.Count;
            if (connectedPlayers >= MAX_PLAYERS)
            {
                Debug.LogWarning($"❌ Client {request.ClientNetworkId}: Server full (max {MAX_PLAYERS})");
                response.Reason = "Server is full";
                return;
            }

            // Проверка 3: Валидация данных подключения (против читов)
            if (!ValidateConnectionData(request.Payload))
            {
                Debug.LogWarning($"❌ Client {request.ClientNetworkId}: Invalid connection data (potential cheat attempt)");
                response.Reason = "Invalid connection data";
                return;
            }

            // ✅ Все проверки пройдены — одобрить подключение
            response.Approved = true;

            // Тело здесь не создаём намеренно. NGO берёт префаб именно в
            // одобрении, то есть до того, как игрок вообще увидел экран
            // выбора, — и раньше персонаж выдавался сервером молча, а выбора
            // не было. Теперь тело спавнит CharacterSelectionManager, когда
            // решено, кем играть.
            response.CreatePlayerObject = false;

            Debug.Log($"✅ Client {request.ClientNetworkId} APPROVED — тело появится после выбора персонажа");
        }


        /// <summary>
        /// Точка спавна по кругу вокруг центра арены — детерминированно от ClientId,
        /// чтобы сервер и клиенты считали одинаково.
        ///
        /// Публичная, потому что тело теперь спавнит
        /// <see cref="CharacterSelectionManager"/> — после выбора персонажа, —
        /// а раскладка по кругу должна остаться одна на всех.
        /// </summary>
        public static Vector3 GetSpawnPosition(ulong clientId)
        {
            const float radius = 3f;
            float angle = clientId * (360f / MAX_PLAYERS) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * radius, 1.5f, Mathf.Sin(angle) * radius);
        }

        /// <summary>
        /// Валидировать версию протокола.
        /// Клиент должен отправить свою версию протокола в connectionData.
        /// </summary>
        private bool ValidateProtocolVersion(ulong clientId, byte[] payload)
        {
            if (payload == null || payload.Length < sizeof(int))
            {
                Debug.LogWarning($"Client {clientId}: Payload too short for protocol version");
                return false;
            }

            int clientProtocolVersion = System.BitConverter.ToInt32(payload, 0);

            if (clientProtocolVersion != PROTOCOL_VERSION)
            {
                Debug.LogWarning($"Client {clientId}: Protocol mismatch (client={clientProtocolVersion}, server={PROTOCOL_VERSION})");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Валидировать данные подключения (защита от读者 и читерских значений).
        /// Сейчас только проверяем что версия есть. Позже добавить:
        /// - Проверка лицензии / баны
        /// - Проверка IP-адреса на blacklist
        /// - Проверка времени рассинхрона
        /// </summary>
        private bool ValidateConnectionData(byte[] payload)
        {
            // Базовая проверка: payload не null и не пуст
            if (payload == null || payload.Length == 0)
            {
                return false;
            }

            // TODO: Добавить более сложные проверки:
            // - Проверить что клиент не в бане
            // - Проверить что IP не в blacklist'е
            // - Проверить регион / ping

            return true;
        }

        /// <summary>
        /// Предоставить версию протокола для отправки клиентом.
        /// Клиент должен вызвать это перед StartClient().
        /// </summary>
        public static byte[] GetConnectionPayload()
        {
            return System.BitConverter.GetBytes(PROTOCOL_VERSION);
        }
    }
}
