using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
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
    /// Выбор персонажа под авторитетом сервера.
    ///
    /// <b>Тело игрока создаётся только после выбора.</b> Прежде персонаж
    /// выдавался прямо в одобрении подключения, потому что NGO берёт префаб
    /// именно там, — и выбора у игрока не было вовсе. Теперь одобрение
    /// объект не создаёт (<c>CreatePlayerObject = false</c>), а спавнит его
    /// этот компонент, когда решено, кем играть. Менять модель уже
    /// заспавненного тела было бы вторым путём к тому же результату и лишним
    /// источником рассинхрона.
    ///
    /// Двое одного персонажа взять не могут: занятость держит сервер, клиент
    /// её только читает. Гонку двух кликов в один кадр разрешает порядок
    /// прихода на сервер — второму приходит отказ, и он выбирает заново.
    ///
    /// Кто не выбрал за отведённое время, получает случайного свободного:
    /// иначе один задумавшийся держит всю комнату, ведь хаб ждёт, пока у всех
    /// появятся тела.
    /// </summary>
    public sealed class CharacterSelectionManager : NetworkBehaviour, ICharacterSelection
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

        public bool HasChosen => IsSpawned && IndexOfClient(NetworkManager.LocalClientId) >= 0;

        public float SecondsLeft => Mathf.Max(0f, localDeadline - Time.realtimeSinceStartup);

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
            if (!prefab.TryGetComponent(out NetworkObject _))
            {
                Debug.LogError($"❌ На префабе '{prefab.name}' нет NetworkObject — персонаж не может быть сетевым");
                return;
            }

            GameObject instance = Instantiate(
                prefab,
                ConnectionApprovalManager.GetSpawnPosition(clientId),
                Quaternion.identity);

            instance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);

            Debug.Log($"🎭 Клиент {clientId} играет за '{roster.Characters[characterIndex].DisplayName}'");
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
