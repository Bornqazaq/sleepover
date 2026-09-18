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
    /// Одна строка журнала катки в сетевом виде: что участник получил за
    /// раунд. Ключ игры — имя сцены: оно короткое и латиницей, поэтому
    /// помещается в FixedString32Bytes без риска обрезки.
    /// </summary>
    public struct SessionRoundState : INetworkSerializable, IEquatable<SessionRoundState>
    {
        public int Round;
        public FixedString32Bytes Game;
        public ulong ClientId;
        public int Place;
        public int Points;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Round);
            serializer.SerializeValue(ref Game);
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Place);
            serializer.SerializeValue(ref Points);
        }

        public bool Equals(SessionRoundState other) =>
            Round == other.Round &&
            Game.Equals(other.Game) &&
            ClientId == other.ClientId &&
            Place == other.Place &&
            Points == other.Points;
    }

    /// <summary>
    /// Сетевое табло катки: ростер, счёт, журнал раундов и чемпионы живут в
    /// NetworkList, менять их вправе только сервер. Клиенты читают то же самое через ISessionScoreboard и не
    /// знают, что данные пришли по сети.
    ///
    /// Спавнится сервером один раз (AppNetworkManager) и переживает смену сцен,
    /// чтобы счёт не сбрасывался между мини-играми.
    /// </summary>
    public sealed class NetworkSessionManager : NetworkBehaviour, ISessionScoreboard, ISessionScoreReset
    {
        private readonly NetworkList<SessionPlayerState> roster = new NetworkList<SessionPlayerState>();

        /// <summary>Журнал катки: по записи на участника за каждый засчитанный раунд.</summary>
        private readonly NetworkList<SessionRoundState> history = new NetworkList<SessionRoundState>();

        /// <summary>Чемпионы последней доигранной серии. Несколько — при равенстве сумм.</summary>
        private readonly NetworkList<ulong> champions = new NetworkList<ulong>();

        private readonly List<SessionRoundRecord> historyMirror = new List<SessionRoundRecord>(64);
        private readonly List<int> championMirror = new List<int>(8);
        private readonly SessionStandings standings = new SessionStandings();
        private int roundsPlayed;

        /// <summary>Зеркало ростера в виде Core-объектов: переиспользуем экземпляры,
        /// иначе мини-игра осталась бы со ссылками на устаревших участников.</summary>
        private readonly List<SessionPlayer> mirror = new List<SessionPlayer>(8);
        private readonly Dictionary<int, SessionPlayer> byId = new Dictionary<int, SessionPlayer>(8);

        /// <summary>Кто уже был в особой роли. Живёт на сервере всю катку и переживает смену сцен.</summary>
        private readonly SpecialRoleHistory specialRoles = new SpecialRoleHistory();

        /// <summary>Ушедшие, которых осталось вычеркнуть. Разбирается на ближайшем тике — см. OnClientDisconnected.</summary>
        private readonly List<ulong> pendingRemovals = new List<ulong>(8);

        public event Action ScoresChanged;

        public IReadOnlyList<SessionPlayer> Players => mirror;

        public IReadOnlyList<SessionRoundRecord> History => historyMirror;

        public int RoundsPlayed => roundsPlayed;

        public IReadOnlyList<int> Champions => championMirror;

        public bool IsChampion(int playerId) => championMirror.Contains(playerId);

        public SessionPlayer LocalPlayer => IsSpawned ? FindPlayer((int)NetworkManager.LocalClientId) : null;

        /// <summary>До спавна сети нет — решает локальная машина.</summary>
        public bool HasAuthority => !IsSpawned || IsServer;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            DontDestroyOnLoad(gameObject);

            roster.OnListChanged += OnRosterChanged;
            history.OnListChanged += OnHistoryChanged;
            champions.OnListChanged += OnChampionsChanged;
            SessionScoreboard.RegisterNetworked(this);

            if (IsServer)
            {
                BuildInitialRoster();
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }

            RebuildHistoryMirror();
            RebuildChampionMirror();
            RebuildMirror();
            Debug.Log($"🏆 NetworkSessionManager готов ({(IsServer ? "СЕРВЕР" : "КЛИЕНТ")}), участников: {roster.Count}, " +
                      $"раундов в журнале: {roundsPlayed}");
        }

        public override void OnNetworkDespawn()
        {
            roster.OnListChanged -= OnRosterChanged;
            history.OnListChanged -= OnHistoryChanged;
            champions.OnListChanged -= OnChampionsChanged;
            SessionScoreboard.Unregister(this);
            if (IsServer) PartySeries.Reset();

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsServer)
            {
                ApplyPendingRemovals();
            }

            ResolveAvatars();
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

            // Ростер не трогаем прямо здесь. Этот же колбэк приходит и когда
            // выключается сам сервер: NGO к тому моменту уже разобрал часть себя,
            // и запись в NetworkList падает в NullReference.
            //
            // Отличить два случая по состоянию NetworkManager нельзя — замерено
            // 15.08, на обоих ShutdownInProgress=False, IsListening=True,
            // SpawnManager и SceneManager живые. Поэтому вычёркиваем на ближайшем
            // тике: при обычном выходе игрока тик будет, при выключении сервера
            // тиков больше нет и вычёркивать уже нечего и некому.
            pendingRemovals.Add(clientId);
        }

        /// <summary>Сервер: вычеркнуть тех, кто вышел, — на живом кадре, а не в колбэке дисконнекта.</summary>
        private void ApplyPendingRemovals()
        {
            if (pendingRemovals.Count == 0)
            {
                return;
            }

            if (NetworkManager == null || NetworkManager.ShutdownInProgress || !NetworkManager.IsListening)
            {
                return;
            }

            for (int i = 0; i < pendingRemovals.Count; i++)
            {
                int clientId = (int)pendingRemovals[i];

                int index = IndexOf(clientId);
                if (index >= 0)
                {
                    roster.RemoveAt(index);
                }

                // Ушедший вычищается из истории вместе с ростером, иначе он вечно
                // числится «уже побывавшим» и сдвигает выбор следующих ролей.
                specialRoles.Forget(clientId);
            }

            pendingRemovals.Clear();
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

        private void OnHistoryChanged(NetworkListEvent<SessionRoundState> changeEvent)
        {
            RebuildHistoryMirror();
            ScoresChanged?.Invoke();
        }

        private void OnChampionsChanged(NetworkListEvent<ulong> changeEvent)
        {
            RebuildChampionMirror();
            ScoresChanged?.Invoke();
        }

        private void RebuildHistoryMirror()
        {
            historyMirror.Clear();
            roundsPlayed = 0;
            for (int i = 0; i < history.Count; i++)
            {
                SessionRoundState state = history[i];
                historyMirror.Add(new SessionRoundRecord(state.Round, state.Game.ToString(),
                    (int)state.ClientId, state.Place, state.Points));
                if (state.Round > roundsPlayed)
                {
                    roundsPlayed = state.Round;
                }
            }
        }

        private void RebuildChampionMirror()
        {
            championMirror.Clear();
            for (int i = 0; i < champions.Count; i++)
            {
                championMirror.Add((int)champions[i]);
            }
        }

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

                player.DisplayName = state.DisplayName.ToString();
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
        /// Начисление по общей формуле <see cref="SessionScoring"/>: число
        /// игроков берётся по составу на старте раунда, а не по ростеру —
        /// ушедшие не обесценивают победу (IGR-372). Только сервер: клиенты
        /// получают очки вместе с местами по RPC, а ростер и журнал —
        /// репликацией NetworkList.
        /// </summary>
        public void ReportResults(MinigameResults results)
        {
            if (!IsServer)
            {
                Debug.LogWarning($"{name}: ReportResults вызван не на сервере — проигнорирован", this);
                return;
            }

            if (results == null)
            {
                return;
            }

            int playerCount = SessionScoring.PlayerCountFor(results, roster.Count);
            int round = roundsPlayed + 1;
            bool anyAwarded = false;
            var game = new FixedString32Bytes(FitKey(results.GameKey));
            IReadOnlyList<MinigameResults.PlayerResult> entries = results.Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                int place = entries[i].Place;
                int index = IndexOf(entries[i].PlayerId);
                if (index < 0)
                {
                    // Вышел из катки до подсчёта: место посчитано, очки некому.
                    Debug.Log($"⭐ [СЕРВЕР] игрок {entries[i].PlayerId}: место {place}, но его уже нет в катке — без очков");
                    continue;
                }

                int points = SessionScoring.PointsFor(place, playerCount);
                SessionPlayerState state = roster[index];
                state.Score += points;
                roster[index] = state;
                results.SetAward(i, points, state.Score);

                history.Add(new SessionRoundState
                {
                    Round = round,
                    Game = game,
                    ClientId = (ulong)entries[i].PlayerId,
                    Place = place,
                    Points = points
                });
                anyAwarded = true;

                Debug.Log($"⭐ [СЕРВЕР] {state.DisplayName}: место {place} из {playerCount}, +{points}, всего {state.Score}");
            }

            if (anyAwarded)
            {
                roundsPlayed = round;
                Debug.Log($"📒 [СЕРВЕР] раунд {round} ({results.GameKey}) записан в журнал катки");
            }
        }

        /// <summary>Ключ игры под вместимость FixedString32Bytes: 29 байт UTF-8.</summary>
        private static string FitKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "?";
            }

            int maxBytes = FixedString32Bytes.UTF8MaxLengthInBytes;
            if (System.Text.Encoding.UTF8.GetByteCount(key) <= maxBytes)
            {
                return key;
            }

            // Редкий путь: ключ длиннее вместимости. Режем по символам, пока
            // не влезет, — на латинице это ровно 29 знаков.
            char[] chars = key.ToCharArray();
            for (int length = chars.Length - 1; length > 0; length--)
            {
                if (System.Text.Encoding.UTF8.GetByteCount(chars, 0, length) <= maxBytes)
                {
                    return new string(chars, 0, length);
                }
            }

            return "?";
        }

        /// <summary>Серия доиграна: чемпионы — все, кто делит первое место по сумме.</summary>
        public void CompleteSeries()
        {
            if (!IsServer)
            {
                Debug.LogWarning($"{name}: CompleteSeries вызван не на сервере — проигнорирован", this);
                return;
            }

            standings.Rebuild(mirror, historyMirror);
            champions.Clear();
            IReadOnlyList<SessionStandings.Entry> table = standings.Entries;
            for (int i = 0; i < table.Count; i++)
            {
                if (standings.IsLeader(table[i].PlayerId))
                {
                    champions.Add((ulong)table[i].PlayerId);
                    Debug.Log($"👑 [СЕРВЕР] чемпион катки: {FindPlayer(table[i].PlayerId)?.DisplayName} " +
                              $"({table[i].Score} очк., побед {table[i].Wins}) за {standings.RoundsPlayed} игр");
                }
            }

            if (champions.Count == 0)
            {
                Debug.Log("👑 [СЕРВЕР] серия окончена без чемпиона: очков ни у кого нет");
            }
        }

        public bool RenamePlayer(ulong clientId, string name)
        {
            if (!IsServer || !PartyDisplayName.TryNormalize(name, out var valid)) return false;
            int index = IndexOf((int)clientId); if (index < 0) return false;
            var state = roster[index]; state.DisplayName = new FixedString32Bytes(valid); roster[index] = state;
            return true;
        }
        /// <summary>Новая серия: счёт, журнал и чемпионы — с чистого листа.</summary>
        public void ResetScores()
        {
            if (!IsServer) return;
            for (int i = 0; i < roster.Count; i++) { var state = roster[i]; state.Score = 0; roster[i] = state; }
            history.Clear();
            champions.Clear();
            roundsPlayed = 0;
        }

        // ========== ОСОБЫЕ РОЛИ ==========

        public bool HasPlayedSpecialRole(int playerId, string roleKey) =>
            specialRoles.HasPlayed(roleKey, playerId);

        public void MarkSpecialRole(int playerId, string roleKey)
        {
            if (!HasAuthority)
            {
                Debug.LogWarning($"{name}: MarkSpecialRole вызван не на сервере — проигнорирован", this);
                return;
            }

            specialRoles.Mark(roleKey, playerId);
        }

        /// <summary>
        /// Выбор роли — серверный: клиент не может назначить её себе (CLAUDE.md 3.1).
        /// </summary>
        public int PickSpecialRole(string roleKey)
        {
            if (!HasAuthority)
            {
                Debug.LogWarning($"{name}: PickSpecialRole вызван не на сервере — роль не выдана", this);
                return SpecialRoleHistory.NoPlayer;
            }

            int picked = specialRoles.Pick(roleKey, mirror);
            Debug.Log($"🎭 [СЕРВЕР] роль «{roleKey}» досталась игроку {picked}");
            return picked;
        }
    }
}
