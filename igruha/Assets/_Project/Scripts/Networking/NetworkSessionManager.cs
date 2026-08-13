using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Networking
{
    /// <summary>
    /// Один участник катки в сетевом виде. Отдельная структура нужна, потому что
    /// NetworkList умеет только unmanaged-типы: обычный SessionPlayer (класс)
    /// реплицировать нельзя.
    /// </summary>
    public struct SessionPlayerState : INetworkSerializable, IEquatable<SessionPlayerState>
    {
        public ulong ClientId;
        public int Score;
        public FixedString32Bytes DisplayName;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Score);
            serializer.SerializeValue(ref DisplayName);
        }

        public bool Equals(SessionPlayerState other) =>
            ClientId == other.ClientId &&
            Score == other.Score &&
            DisplayName.Equals(other.DisplayName);
    }

    /// <summary>
    /// Сетевое табло катки: ростер и счёт живут в NetworkList, менять их вправе
    /// только сервер. Клиенты читают то же самое через ISessionScoreboard и не
    /// знают, что данные пришли по сети.
    ///
    /// Спавнится сервером один раз (AppNetworkManager) и переживает смену сцен,
    /// чтобы счёт не сбрасывался между мини-играми.
    /// </summary>
    public sealed class NetworkSessionManager : NetworkBehaviour, ISessionScoreboard
    {
        private readonly NetworkList<SessionPlayerState> roster = new NetworkList<SessionPlayerState>();

        /// <summary>Зеркало ростера в виде Core-объектов: переиспользуем экземпляры,
        /// иначе мини-игра осталась бы со ссылками на устаревших участников.</summary>
        private readonly List<SessionPlayer> mirror = new List<SessionPlayer>(8);
        private readonly Dictionary<int, SessionPlayer> byId = new Dictionary<int, SessionPlayer>(8);

        public event Action ScoresChanged;

        public IReadOnlyList<SessionPlayer> Players => mirror;

        public SessionPlayer LocalPlayer => IsSpawned ? FindPlayer((int)NetworkManager.LocalClientId) : null;

        /// <summary>До спавна сети нет — решает локальная машина.</summary>
        public bool HasAuthority => !IsSpawned || IsServer;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            roster.OnListChanged += OnRosterChanged;
            SessionScoreboard.RegisterNetworked(this);

            if (IsServer)
            {
                BuildInitialRoster();
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }

            RebuildMirror();
            Debug.Log($"🏆 NetworkSessionManager готов ({(IsServer ? "СЕРВЕР" : "КЛИЕНТ")}), участников: {roster.Count}");
        }

        public override void OnNetworkDespawn()
        {
            roster.OnListChanged -= OnRosterChanged;
            SessionScoreboard.Unregister(this);

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (IsSpawned)
            {
                ResolveAvatars();
            }
        }

        // ========== РОСТЕР (сервер) ==========

        private void BuildInitialRoster()
        {
            roster.Clear();
            IReadOnlyList<ulong> ids = NetworkManager.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++)
            {
                AddToRoster(ids[i]);
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (IsServer)
            {
                AddToRoster(clientId);
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            int index = IndexOf((int)clientId);
            if (index >= 0)
            {
                roster.RemoveAt(index);
            }
        }

        private void AddToRoster(ulong clientId)
        {
            if (IndexOf((int)clientId) >= 0)
            {
                return;
            }

            roster.Add(new SessionPlayerState
            {
                ClientId = clientId,
                Score = 0,
                DisplayName = new FixedString32Bytes($"Игрок {roster.Count + 1}")
            });
        }

        private int IndexOf(int playerId)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                if ((int)roster[i].ClientId == playerId)
                {
                    return i;
                }
            }

            return -1;
        }

        // ========== ЗЕРКАЛО ДЛЯ CORE ==========

        private void OnRosterChanged(NetworkListEvent<SessionPlayerState> changeEvent) => RebuildMirror();

        private void RebuildMirror()
        {
            mirror.Clear();

            for (int i = 0; i < roster.Count; i++)
            {
                SessionPlayerState state = roster[i];
                int id = (int)state.ClientId;

                if (!byId.TryGetValue(id, out SessionPlayer player))
                {
                    player = new SessionPlayer(id, state.DisplayName.ToString());
                    byId[id] = player;
                }

                player.Score = state.Score;
                mirror.Add(player);
            }

            ScoresChanged?.Invoke();
        }

        /// <summary>
        /// Досыпаем аватары по мере спавна персонажей: ростер приходит раньше,
        /// чем NGO создаёт объекты игроков. ConnectedClients есть только на
        /// сервере, поэтому ищем среди заспавненных объектов — так работает у всех.
        /// </summary>
        private void ResolveAvatars()
        {
            for (int i = 0; i < mirror.Count; i++)
            {
                SessionPlayer player = mirror[i];
                if (player.Avatar != null)
                {
                    continue;
                }

                NetworkObject playerObject = FindPlayerObject((ulong)player.Id);
                if (playerObject != null)
                {
                    player.Avatar = playerObject.GetComponent<PlayerController>();
                }
            }
        }

        private NetworkObject FindPlayerObject(ulong clientId)
        {
            foreach (NetworkObject spawned in NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned != null && spawned.IsPlayerObject && spawned.OwnerClientId == clientId)
                {
                    return spawned;
                }
            }

            return null;
        }

        // ========== ISessionScoreboard ==========

        public SessionPlayer FindPlayer(int playerId)
        {
            for (int i = 0; i < mirror.Count; i++)
            {
                if (mirror[i].Id == playerId)
                {
                    return mirror[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Начисление по формуле «очки = число_игроков − место» (GDD 3.1).
        /// Только сервер: клиенты получат новый счёт репликацией NetworkList.
        /// </summary>
        public void ReportResults(MinigameResults results)
        {
            if (!IsServer)
            {
                Debug.LogWarning($"{name}: ReportResults вызван не на сервере — проигнорирован", this);
                return;
            }

            int playerCount = roster.Count;
            IReadOnlyList<MinigameResults.PlayerResult> entries = results.Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                int index = IndexOf(entries[i].PlayerId);
                if (index < 0)
                {
                    continue;
                }

                SessionPlayerState state = roster[index];
                state.Score += Mathf.Max(0, playerCount - entries[i].Place);
                roster[index] = state;

                Debug.Log($"⭐ [СЕРВЕР] {state.DisplayName}: место {entries[i].Place}, всего очков {state.Score}");
            }
        }
    }
}
