using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Hub;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Networking
{
    /// <summary>Кто какого персонажа забрал. Реплицируется всем: по этому списку экран гасит занятые слоты.</summary>
    public struct CharacterClaim : INetworkSerializable, IEquatable<CharacterClaim>
    {
        public ulong ClientId;
        public int CharacterIndex;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref CharacterIndex);
        }

        public bool Equals(CharacterClaim other) =>
            ClientId == other.ClientId && CharacterIndex == other.CharacterIndex;
    }

    /// <summary>
    /// Server-owned character reservations, initial timed selection, and profile changes in the party room.
    /// A skin is owned by one client; switching replaces the player object and releases the old reservation.
    /// </summary>
    public sealed class CharacterSelectionManager : NetworkBehaviour, ICharacterSelection, IHubPartyProfiles
    {
        [Tooltip("Ростер персонажей — тот же, что показывает экран выбора")]
        [SerializeField] private CharacterRoster roster;

        [Tooltip("Сколько секунд даётся на выбор, прежде чем сервер выдаст случайного свободного")]
        [SerializeField] private float chooseSeconds = 30f;

        /// <summary>
        /// Во сколько раз дольше ждём того, кто ещё не доложил о готовности.
        /// Он грузит сцену, экрана перед ним нет, и обычный срок ему нечестен;
        /// но и ждать вечно нельзя — хаб стоит, пока у всех нет тел.
        /// </summary>
        private const float UnreadyGrace = 3f;

        private readonly NetworkList<CharacterClaim> claims = new NetworkList<CharacterClaim>();

        /// <summary>Докуда ждём выбор от каждого, по clientId. Только на сервере.</summary>
        private readonly Dictionary<ulong, float> deadlines = new Dictionary<ulong, float>(8);

        /// <summary>Момент, когда экран показан этой машине: от него считается надпись отсчёта.</summary>
        private float localDeadline;

        public event Action Changed;
        public event Action<string> ProfileResult;
        public int CharacterOf(int playerId)
        {
            int index = IndexOfClient((ulong)playerId);
            return index >= 0 ? claims[index].CharacterIndex : -1;
        }
        public void ChangeOwnName(string value)
        {
            if (IsSpawned) RenameRpc(value ?? string.Empty);
        }

        /// <summary>
        /// Отдать серверу имя, введённое на экране входа.
        ///
        /// Отдельно от <see cref="ChangeOwnName"/>, потому что правила разные.
        /// Смена имени разрешена только в открытой комнате участников — иначе
        /// её можно было бы устроить посреди раунда. А первое имя приносит сам
        /// клиент при входе, когда никакой комнаты ещё нет: без этого четверо
        /// друзей заходят «Игроком 2», «Игроком 3» и «Игроком 4», и на табло
        /// не разобрать, кто где. Сервер примет его ровно один раз и только
        /// поверх выданного по счёту.
        /// </summary>
        private void ClaimSavedName()
        {
            string saved = PlayerPrefs.GetString(NetworkConnectScreen.NameKey, string.Empty).Trim();
            if (saved.Length == 0) return;

            ClaimNameRpc(saved);
        }

        [Rpc(SendTo.Server)]
        private void ClaimNameRpc(string value, RpcParams rpcParams = default)
        {
            GetComponent<NetworkSessionManager>()?.ClaimNameOnJoin(rpcParams.Receive.SenderClientId, value);
        }
        public void ChangeOwnCharacter(int index) { if (IsSpawned) ChangeCharacterRpc(index); }

        [Rpc(SendTo.Server)]
        private void RenameRpc(string value, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            bool success = HubPartyProfiles.CanEdit && GetComponent<NetworkSessionManager>().RenamePlayer(sender, value);
            ProfileResultRpc(sender, success ? "Имя сохранено" : "Имя: до 14 русских или 20 латинских букв, без специальных знаков.");
        }

        [Rpc(SendTo.Server)]
        private void ChangeCharacterRpc(int characterIndex, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            int claimIndex = IndexOfClient(sender);
            if (!HubPartyProfiles.CanEdit || claimIndex < 0 || roster == null || characterIndex < 0 ||
                characterIndex >= roster.Characters.Count || !roster.Characters[characterIndex].IsAvailable) return;
            if (claims[claimIndex].CharacterIndex == characterIndex) return;
            if (IsTaken(characterIndex)) { ProfileResultRpc(sender, "Этот облик уже занят. Выбери другой."); return; }
            var prefab = roster.Characters[characterIndex].Prefab;
            if (!prefab.TryGetComponent(out NetworkObject prefabObject) || !IsRegisteredNetworkPrefab(prefabObject.PrefabIdHash)) return;
            if (!NetworkManager.ConnectedClients.TryGetValue(sender, out var client) || client.PlayerObject == null) return;
            var old = client.PlayerObject;
            var position = old.transform.position; var rotation = old.transform.rotation;
            var instance = Instantiate(prefab, position, rotation).GetComponent<NetworkObject>();
            // Remove the old registration before assigning the new PlayerObject to this client.
            old.Despawn();
            instance.SpawnAsPlayerObject(sender);
            claims[claimIndex] = new CharacterClaim { ClientId = sender, CharacterIndex = characterIndex };
            var player = SessionScoreboard.Current?.FindPlayer((int)sender);
            if (player != null) player.Avatar = instance.GetComponent<PlayerController>();
            ProfileResultRpc(sender, "Облик изменён");
            Debug.Log($"PARTY PROFILE {sender}: character={characterIndex}");
        }

        [Rpc(SendTo.Everyone)]
        private void ProfileResultRpc(ulong recipient, string message)
        {
            if (NetworkManager.LocalClientId == recipient) ProfileResult?.Invoke(message);
        }

        public bool HasChosen => IsSpawned && IndexOfClient(NetworkManager.LocalClientId) >= 0;

        public float SecondsLeft => Mathf.Max(0f, localDeadline - Time.realtimeSinceStartup);

        public bool HasCharacter(int playerId) => IndexOfClient((ulong)playerId) >= 0;

        public bool IsTaken(int characterIndex)
        {
            for (int i = 0; i < claims.Count; i++)
            {
                if (claims[i].CharacterIndex == characterIndex)
                {
                    return true;
                }
            }

            return false;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            claims.OnListChanged += OnClaimsChanged;
            CharacterSelection.Register(this);

            // Имя с экрана входа отдаём сразу: комнаты участников ещё нет, а
            // к моменту, когда хост её откроет, на табло уже должны быть люди,
            // а не «Игрок 2» и «Игрок 3».
            ClaimSavedName();

            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

                // Хост подключился до нашего спавна: его собственный колбэк уже
                // прошёл, и без этого срок ему никто бы не завёл.
                foreach (ulong id in NetworkManager.ConnectedClientsIds)
                {
                    StartWaiting(id);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            claims.OnListChanged -= OnClaimsChanged;
            CharacterSelection.Unregister(this);

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            base.OnNetworkDespawn();
        }

        private void OnClaimsChanged(NetworkListEvent<CharacterClaim> changeEvent) => Changed?.Invoke();

        // ========== КЛИЕНТ ==========

        public void ReportReady()
        {
            localDeadline = Time.realtimeSinceStartup + chooseSeconds;

            if (IsSpawned)
            {
                ReadyToChooseRpc();
            }
        }

        public void Choose(int characterIndex)
        {
            if (IsSpawned)
            {
                ChooseRpc(characterIndex);
            }
        }

        // ========== СЕРВЕР ==========

        [Rpc(SendTo.Server)]
        private void ReadyToChooseRpc(RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;

            // Срок отсчитываем от готовности, а не от подключения: до того как
            // сцена хаба загрузилась, экрана перед игроком нет и выбирать ему
            // нечем — это было бы наказанием за долгую загрузку.
            deadlines[clientId] = Time.realtimeSinceStartup + chooseSeconds;
        }

        [Rpc(SendTo.Server)]
        private void ChooseRpc(int characterIndex, RpcParams rpcParams = default)
        {
            TryClaim(rpcParams.Receive.SenderClientId, characterIndex);
        }

        private void OnClientConnected(ulong clientId) => StartWaiting(clientId);

        private void StartWaiting(ulong clientId)
        {
            // Матч уже идёт: подключившийся в середине не игрок, а зритель.
            // Тела ему не даём и срока не заводим — выберет персонажа в хабе,
            // когда раунд доиграют и все туда вернутся. Иначе он появился бы
            // посреди чужой гонки, не имея в ней ни места, ни роли.
            if (MinigameControllerBase.Current != null)
            {
                Debug.Log($"👀 Клиент {clientId} подключился посреди матча — досматривает со стороны");
                return;
            }

            if (!deadlines.ContainsKey(clientId))
            {
                deadlines[clientId] = Time.realtimeSinceStartup + chooseSeconds * UnreadyGrace;
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            deadlines.Remove(clientId);

            // Персонаж ушедшего возвращается в общий котёл: без этого после
            // пары переподключений ростер «кончится».
            int index = IndexOfClient(clientId);
            if (index >= 0)
            {
                claims.RemoveAt(index);
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || deadlines.Count == 0)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            foreach (KeyValuePair<ulong, float> pair in deadlines)
            {
                if (now < pair.Value || IndexOfClient(pair.Key) >= 0)
                {
                    continue;
                }

                int index = PickRandomFree();
                if (index < 0)
                {
                    Debug.LogError("❌ Выбор персонажа: свободных не осталось — выдать нечего");
                    deadlines.Remove(pair.Key);
                    break;
                }

                Debug.Log($"⏳ Клиент {pair.Key} не выбрал за {chooseSeconds:F0} с — выдаю случайного свободного");
                Claim(pair.Key, index);

                // Словарь изменён изнутри обхода: остальных доберём следующим
                // кадром, срок у них всё равно уже вышел.
                break;
            }
        }

        /// <summary>Намерение клиента: проверяем и, если можно, закрепляем.</summary>
        private void TryClaim(ulong clientId, int characterIndex)
        {
            if (IndexOfClient(clientId) >= 0)
            {
                return;
            }

            if (roster == null || characterIndex < 0 || characterIndex >= roster.Characters.Count)
            {
                Debug.LogWarning($"⚠️ Клиент {clientId} прислал персонажа вне ростера: {characterIndex}");
                return;
            }

            if (!roster.Characters[characterIndex].IsAvailable)
            {
                Debug.LogWarning($"⚠️ Клиент {clientId} выбрал слот без префаба: {characterIndex}");
                return;
            }

            if (IsTaken(characterIndex))
            {
                // Двое кликнули в один кадр — этот пришёл вторым. Экран у него
                // обновится списком занятых, и он выберет заново.
                return;
            }

            Claim(clientId, characterIndex);
        }

        private void Claim(ulong clientId, int characterIndex)
        {
            claims.Add(new CharacterClaim { ClientId = clientId, CharacterIndex = characterIndex });
            deadlines.Remove(clientId);
            SpawnPlayer(clientId, characterIndex);
        }

        /// <summary>
        /// Создать тело игрока выбранным префабом. До этого момента у клиента
        /// тела нет вовсе — одобрение подключения его намеренно не создаёт.
        /// </summary>
        private void SpawnPlayer(ulong clientId, int characterIndex)
        {
            GameObject prefab = roster.Characters[characterIndex].Prefab;
            if (!prefab.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError($"❌ На префабе '{prefab.name}' нет NetworkObject — персонаж не может быть сетевым");
                return;
            }

            // Префаб обязан быть в списке сетевых. Незарегистрированный NGO
            // не создаст ни у кого: выбравший остаётся БЕЗ ПЕРСОНАЖА.
            //
            // Так и было (IGR-388): в ростере восемь персонажей, а в
            // DefaultNetworkPrefabs лежали пять — шестой, седьмой и восьмой
            // участники катки оказывались зрителями без тела. На двоих и на
            // четверых это не воспроизводится вовсе — потому и дожило до
            // прогона восьмерых.
            //
            // Список чиним данными, но проверку держим здесь: следующий
            // добавленный персонаж иначе повторит ровно это, и снова молча.
            if (!IsRegisteredNetworkPrefab(networkObject.PrefabIdHash))
            {
                Debug.LogError(
                    $"❌ Префаб персонажа '{roster.Characters[characterIndex].DisplayName}' не зарегистрирован в " +
                    "DefaultNetworkPrefabs — тело не создаётся. Добавь его в список, " +
                    "иначе часть игроков останется без персонажа");
                return;
            }

            GameObject instance = Instantiate(
                prefab,
                ConnectionApprovalManager.GetSpawnPosition(clientId),
                Quaternion.identity);

            instance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);

            Debug.Log($"🎭 Клиент {clientId} играет за '{roster.Characters[characterIndex].DisplayName}'");
        }

        /// <summary>Префаб есть в списке сетевых префабов — NGO сможет его создать.</summary>
        private static bool IsRegisteredNetworkPrefab(uint prefabIdHash)
        {
            NetworkManager network = NetworkManager.Singleton;
            return network != null && network.NetworkConfig != null && network.NetworkConfig.Prefabs != null &&
                   network.NetworkConfig.Prefabs.NetworkPrefabOverrideLinks.ContainsKey(prefabIdHash);
        }

        private int PickRandomFree()
        {
            IReadOnlyList<CharacterDefinition> characters = roster != null
                ? roster.Characters
                : Array.Empty<CharacterDefinition>();

            if (characters.Count == 0)
            {
                return -1;
            }

            // Обход от случайной точки, а не бросок с повторами: свободных
            // может остаться один, и слепой бросок искал бы его долго.
            int offset = UnityEngine.Random.Range(0, characters.Count);
            for (int i = 0; i < characters.Count; i++)
            {
                int index = (offset + i) % characters.Count;
                if (characters[index].IsAvailable && !IsTaken(index))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Выдать тело каждому подключённому, у кого его ещё нет. Только сервер.
        ///
        /// Нужно тем, кто зашёл или переподключился посреди катки:
        /// <see cref="StartWaiting"/> им срока не заводит намеренно — посреди
        /// чужого раунда человеку появляться незачем. Но «Полная игра» едет из
        /// мини-игры сразу в следующую, минуя хаб, и экрана выбора для него
        /// больше не будет вовсе — он оставался зрителем до конца всей катки.
        ///
        /// Поэтому на входе в новую сцену мини-игры сервер выдаёт таким
        /// случайного свободного персонажа сам, не ожидая выбора: раунд для
        /// них начинается прямо сейчас, и ждать нечего.
        ///
        /// Возвращает, скольким выдали, — зовущему это нужно только для лога.
        /// </summary>
        public int ServerGrantMissingBodies()
        {
            if (!IsSpawned || !IsServer)
            {
                return 0;
            }

            int granted = 0;
            IReadOnlyList<ulong> ids = NetworkManager.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++)
            {
                ulong clientId = ids[i];
                if (IndexOfClient(clientId) >= 0)
                {
                    continue;
                }

                int index = PickRandomFree();
                if (index < 0)
                {
                    Debug.LogError("❌ Выбор персонажа: свободных не осталось — выдать нечего");
                    break;
                }

                Claim(clientId, index);
                granted++;
            }

            return granted;
        }

        private int IndexOfClient(ulong clientId)
        {
            for (int i = 0; i < claims.Count; i++)
            {
                if (claims[i].ClientId == clientId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
