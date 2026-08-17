using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Igruha.Core.Player;

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

        [Tooltip("Ростер персонажей: каждому подключившемуся достаётся свой, пока они не кончатся")]
        [SerializeField] private CharacterRoster roster;

        /// <summary>
        /// Кому какой персонаж выдан. Раздаёт только сервер и только в момент
        /// одобрения — до спавна, потому что NGO выбирает префаб именно здесь.
        /// </summary>
        private readonly Dictionary<ulong, int> assignedCharacters = new Dictionary<ulong, int>(MAX_PLAYERS);

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

            // Освобождать персонажа при выходе: иначе после пары переподключений
            // ростер «кончится» и все начнут получать одну и ту же модель.
            networkManager.OnClientDisconnectCallback += ReleaseCharacter;

            Debug.Log("✅ ConnectionApprovalManager: Connection Approval enabled");
        }

        private void OnDestroy()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                networkManager.ConnectionApprovalCallback -= HandleConnectionApproval;
                networkManager.OnClientDisconnectCallback -= ReleaseCharacter;
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
            response.CreatePlayerObject = true;

            // Свой персонаж каждому. Без этого NGO спавнит всем один и тот же
            // NetworkConfig.PlayerPrefab, и вся комната состоит из одинаковых близнецов.
            AssignCharacter(request.ClientNetworkId, response);

            // Развести игроков по спавну: без этого все появляются в одной точке
            // и выталкивают друг друга физикой
            response.Position = GetSpawnPosition(request.ClientNetworkId);
            response.Rotation = Quaternion.identity;

            Debug.Log($"✅ Client {request.ClientNetworkId} APPROVED — creating player object at {response.Position}");
        }

        /// <summary>
        /// Выдать подключающемуся свободного персонажа и подставить его префаб в ответ.
        ///
        /// Решает сервер: выбор модели — часть состояния катки, клиенту его доверять
        /// нельзя (иначе двое пришлют один и тот же и снова станут близнецами).
        /// Персонажей в ростере меньше, чем мест (5 против 8), поэтому при переполнении
        /// идём по кругу — повтор лучше, чем отказ в подключении.
        /// </summary>
        private void AssignCharacter(ulong clientId, NetworkManager.ConnectionApprovalResponse response)
        {
            if (roster == null)
            {
                Debug.LogWarning("⚠️ ConnectionApprovalManager: не назначен ростер — все получат префаб по умолчанию");
                return;
            }

            IReadOnlyList<CharacterDefinition> characters = roster.Characters;
            int chosen = -1;

            // Первый проход — только незанятые, второй — по кругу от clientId.
            for (int i = 0; i < characters.Count && chosen < 0; i++)
            {
                if (characters[i].IsAvailable && !IsTaken(i))
                {
                    chosen = i;
                }
            }

            if (chosen < 0)
            {
                for (int i = 0; i < characters.Count; i++)
                {
                    int candidate = (int)((clientId + (ulong)i) % (ulong)characters.Count);
                    if (characters[candidate].IsAvailable)
                    {
                        chosen = candidate;
                        break;
                    }
                }
            }

            if (chosen < 0)
            {
                Debug.LogError("❌ ConnectionApprovalManager: в ростере нет ни одного персонажа с префабом");
                return;
            }

            GameObject prefab = characters[chosen].Prefab;
            if (!prefab.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError($"❌ На префабе '{prefab.name}' нет NetworkObject — персонаж не может быть сетевым");
                return;
            }

            assignedCharacters[clientId] = chosen;
            response.PlayerPrefabHash = networkObject.PrefabIdHash;

            Debug.Log($"🎭 Client {clientId} получает персонажа '{characters[chosen].DisplayName}'");
        }

        private bool IsTaken(int characterIndex)
        {
            foreach (int taken in assignedCharacters.Values)
            {
                if (taken == characterIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private void ReleaseCharacter(ulong clientId) => assignedCharacters.Remove(clientId);

        /// <summary>
        /// Точка спавна по кругу вокруг центра арены — детерминированно от ClientId,
        /// чтобы сервер и клиенты считали одинаково.
        /// </summary>
        private static Vector3 GetSpawnPosition(ulong clientId)
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
