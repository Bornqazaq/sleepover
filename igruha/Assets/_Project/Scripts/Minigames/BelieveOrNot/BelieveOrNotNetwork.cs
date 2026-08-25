using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Session;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Положение матча одной структурой: номер кона, кто за столом, кто из
    /// двоих знает, счёт команд.
    ///
    /// Вместе, а не по отдельности, потому что назначаются они одним решением
    /// сервера. Приехавший раньше новый Знающий под старым номером кона
    /// означал бы карточку, показанную не тому.
    ///
    /// <b>Содержимого коробок здесь нет и быть не может.</b> Что в какой
    /// коробке, знает только серверное поле контроллера и не покидает
    /// авторитета до раскрытия крышек (спека 10.3). Решения Решающего здесь
    /// тоже нет: оно едет параметром <c>Rpc</c> раскрытия, вместе с картами, —
    /// иначе порядок доставки развёл бы анимацию обмена с исходом.
    /// </summary>
    public struct BelieveMatchNetState : INetworkSerializable, IEquatable<BelieveMatchNetState>
    {
        public int RoundNumber;
        public int TotalRounds;
        public int Seat0PlayerId;
        public int Seat1PlayerId;
        public int KnowerPlayerId;
        public int DeciderPlayerId;
        public int TeamAWins;
        public int TeamBWins;
        public bool Resolved;
        public bool Cancelled;

        /// <summary>
        /// Матч ещё не начался. Места именно <c>NoPlayer</c>, а не нули:
        /// ноль — законный идентификатор клиента (это хост), и на нулях
        /// хост считал бы себя сидящим ещё до первого кона.
        /// </summary>
        public static BelieveMatchNetState Empty => new BelieveMatchNetState
        {
            Seat0PlayerId = SpecialRoleHistory.NoPlayer,
            Seat1PlayerId = SpecialRoleHistory.NoPlayer,
            KnowerPlayerId = SpecialRoleHistory.NoPlayer,
            DeciderPlayerId = SpecialRoleHistory.NoPlayer
        };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref RoundNumber);
            serializer.SerializeValue(ref TotalRounds);
            serializer.SerializeValue(ref Seat0PlayerId);
            serializer.SerializeValue(ref Seat1PlayerId);
            serializer.SerializeValue(ref KnowerPlayerId);
            serializer.SerializeValue(ref DeciderPlayerId);
            serializer.SerializeValue(ref TeamAWins);
            serializer.SerializeValue(ref TeamBWins);
            serializer.SerializeValue(ref Resolved);
            serializer.SerializeValue(ref Cancelled);
        }

        public bool Equals(BelieveMatchNetState other) =>
            RoundNumber == other.RoundNumber &&
            TotalRounds == other.TotalRounds &&
            Seat0PlayerId == other.Seat0PlayerId &&
            Seat1PlayerId == other.Seat1PlayerId &&
            KnowerPlayerId == other.KnowerPlayerId &&
            DeciderPlayerId == other.DeciderPlayerId &&
            TeamAWins == other.TeamAWins &&
            TeamBWins == other.TeamBWins &&
            Resolved == other.Resolved &&
            Cancelled == other.Cancelled;
    }

    /// <summary>
    /// Стадия кона и момент её конца. Конец — момент на общих часах, а не
    /// остаток: остаток пришлось бы досылать каждый кадр, момент достаточно
    /// объявить один раз. Тот же приём, что у «Экзамена» и «Порядка банок».
    /// </summary>
    public struct BelieveStageNetState : INetworkSerializable, IEquatable<BelieveStageNetState>
    {
        public int Round;
        public byte Stage;
        public double EndTime;
        public float Duration;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Round);
            serializer.SerializeValue(ref Stage);
            serializer.SerializeValue(ref EndTime);
            serializer.SerializeValue(ref Duration);
        }

        public bool Equals(BelieveStageNetState other) =>
            Round == other.Round &&
            Stage == other.Stage &&
            EndTime.Equals(other.EndTime) &&
            Mathf.Approximately(Duration, other.Duration);
    }

    /// <summary>
    /// Строка участника в сети: команда, победы и метки тайбрейка. Ровно то,
    /// из чего <see cref="BelieveRanking"/> строит места, — чтобы клиент мог
    /// показать те же числа, что считает сервер.
    /// </summary>
    public struct BelieveEntryNetState : INetworkSerializable, IEquatable<BelieveEntryNetState>
    {
        public int PlayerId;
        public byte Team;
        public byte RoundsWon;
        public byte DeciderWins;
        public byte RoundsSeated;
        public double LastWonAt;
        public bool Present;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref RoundsWon);
            serializer.SerializeValue(ref DeciderWins);
            serializer.SerializeValue(ref RoundsSeated);
            serializer.SerializeValue(ref LastWonAt);
            serializer.SerializeValue(ref Present);
        }

        public bool Equals(BelieveEntryNetState other) =>
            PlayerId == other.PlayerId &&
            Team == other.Team &&
            RoundsWon == other.RoundsWon &&
            DeciderWins == other.DeciderWins &&
            RoundsSeated == other.RoundsSeated &&
            LastWonAt.Equals(other.LastWonAt) &&
            Present == other.Present;
    }

    /// <summary>
    /// Сетевая половина «Верю / не верю»: вешается на тот же объект, что и
    /// <see cref="BelieveOrNotMinigame"/>. Правила остаются обычным
    /// MonoBehaviour и работают без этого компонента, когда сцену открывают
    /// напрямую, — образец взят у <c>ExamNetwork</c> и <c>CansOrderNetwork</c>.
    ///
    /// Фазу, время раунда и итоговые места везёт <c>NetworkMinigameBridge</c>
    /// рядом на том же объекте.
    ///
    /// <b>Чего здесь нет и не будет:</b>
    /// <list type="bullet">
    /// <item><b>содержимого коробок</b> — это и есть тайна игры. Клиенты узнают
    /// исход из того, какая крышка открылась с галочкой; отдельного пакета
    /// «галочка в левой» не существует;</item>
    /// <item><b>карточки Знающего в NetworkVariable</b> — переменная
    /// синхронизируется при подключении, и её получил бы любой, кто вошёл
    /// в середине кона, включая соперника после переподключения. Карточка едет
    /// адресным <c>Rpc</c> ровно одному клиенту;</item>
    /// <item>перемещений зрителей — их везёт обычный <c>NetworkTransform</c>.</item>
    /// </list>
    /// </summary>
    public sealed class BelieveOrNotNetwork : NetworkBehaviour
    {
        private readonly NetworkVariable<BelieveMatchNetState> match =
            new NetworkVariable<BelieveMatchNetState>(BelieveMatchNetState.Empty);

        private readonly NetworkVariable<BelieveStageNetState> stage =
            new NetworkVariable<BelieveStageNetState>();

        private readonly NetworkList<BelieveEntryNetState> entries = new NetworkList<BelieveEntryNetState>();

        private BelieveOrNotMinigame game;
        private MinigameStageState stageState;

        private bool matchDirty;
        private bool stageDirty;
        private bool entriesDirty;

        /// <summary>
        /// Состав, под который состояние уже разобрано. Порядок спавна этого
        /// объекта и старта мини-игры ничем не связан: состояние вполне может
        /// приехать раньше, чем контроллер получит ростер, — и тогда стадию
        /// пришлось бы разбирать заново, иначе сидящий клиент остался бы
        /// с камерой зрителя на весь кон.
        /// </summary>
        private int appliedRoster = -1;

        /// <summary>Идёт сетевая катка и эта половина живая.</summary>
        public bool IsActive => IsSpawned;

        private void Awake()
        {
            game = GetComponent<BelieveOrNotMinigame>();
            if (game == null)
            {
                Debug.LogError($"{name}: BelieveOrNotNetwork не нашёл BelieveOrNotMinigame на своём объекте", this);
            }

            stageState = GetComponent<MinigameStageState>();
            if (stageState == null)
            {
                Debug.LogError($"{name}: BelieveOrNotNetwork не нашёл MinigameStageState на своём объекте", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            match.OnValueChanged += OnMatchChanged;
            stage.OnValueChanged += OnStageChanged;
            entries.OnListChanged += OnEntriesChanged;

            if (IsServer)
            {
                if (NetworkManager != null)
                {
                    NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                }

                // Стадию публикуем сами, по событию машины стадий: контроллеру
                // не нужно помнить про сеть в каждой точке перехода.
                if (stageState != null)
                {
                    stageState.StageStarted += OnServerStageStarted;
                }
            }

            // Состояние могло приехать до подписки — разбираем то, что уже есть.
            matchDirty = true;
            stageDirty = true;
            entriesDirty = true;
        }

        public override void OnNetworkDespawn()
        {
            match.OnValueChanged -= OnMatchChanged;
            stage.OnValueChanged -= OnStageChanged;
            entries.OnListChanged -= OnEntriesChanged;

            if (IsServer)
            {
                if (NetworkManager != null)
                {
                    NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                }

                if (stageState != null)
                {
                    stageState.StageStarted -= OnServerStageStarted;
                }
            }

            base.OnNetworkDespawn();
        }

        // ========== СЕРВЕР ПУБЛИКУЕТ ==========

        /// <summary>
        /// Опубликовать положение матча. Только сервер.
        ///
        /// Поля перечислены поимённо, а не копируются структурой целиком:
        /// в <see cref="BelieveMatchState"/> завтра может появиться служебное
        /// поле, и поимённый список — то место, где видно, что именно уходит
        /// в трафик.
        /// </summary>
        public void PublishMatch(in BelieveMatchState state)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            match.Value = new BelieveMatchNetState
            {
                RoundNumber = state.RoundNumber,
                TotalRounds = state.TotalRounds,
                Seat0PlayerId = state.Seat0PlayerId,
                Seat1PlayerId = state.Seat1PlayerId,
                KnowerPlayerId = state.KnowerPlayerId,
                DeciderPlayerId = state.DeciderPlayerId,
                TeamAWins = state.TeamAWins,
                TeamBWins = state.TeamBWins,
                Resolved = state.Resolved,
                Cancelled = state.Cancelled
            };
        }

        /// <summary>Разослать состав целиком. Только сервер.</summary>
        public void PublishEntries(IReadOnlyList<BelieveEntry> source)
        {
            if (!IsSpawned || !IsServer || source == null)
            {
                return;
            }

            entries.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                BelieveEntry e = source[i];
                entries.Add(new BelieveEntryNetState
                {
                    PlayerId = e.PlayerId,
                    Team = (byte)e.Team,
                    RoundsWon = (byte)Mathf.Clamp(e.RoundsWon, 0, 255),
                    DeciderWins = (byte)Mathf.Clamp(e.DeciderWins, 0, 255),
                    RoundsSeated = (byte)Mathf.Clamp(e.RoundsSeated, 0, 255),
                    LastWonAt = e.LastWonAt,
                    Present = e.Present
                });
            }
        }

        /// <summary>
        /// Стадия сменилась у сервера — объявляем её всем вместе с моментом
        /// конца. Момент берётся из общих часов, поэтому остаток у клиента
        /// сходится с серверным без поправки на пинг.
        /// </summary>
        private void OnServerStageStarted(byte stageId)
        {
            if (!IsSpawned || !IsServer || stageState == null)
            {
                return;
            }

            stage.Value = new BelieveStageNetState
            {
                Round = stageState.Subround,
                Stage = stageId,
                EndTime = stageState.StageEndTime,
                Duration = stageState.StageDuration
            };
        }

        // ========== КАРТОЧКА ЗНАЮЩЕГО: АДРЕСНО И ОДНОМУ ==========

        /// <summary>
        /// Отдать Знающему его карточку. <b>Ровно ему и больше никому.</b>
        ///
        /// Именно адресный <c>Rpc</c>, а не <c>NetworkVariable</c>: переменная
        /// синхронизируется при подключении, поэтому её содержимое получил бы
        /// и тот, кто вошёл в середине стадии показа, — включая соперника,
        /// переподключившегося после обрыва. <c>Rpc</c> уходит только тому,
        /// кто уже в игре, и только один раз.
        ///
        /// Уходит <b>только его собственная карточка</b>, а не расклад обеих.
        /// То, что он выводит вторую логически, — правило игры; то, что сервер
        /// её не присылает, — правило сети (спека 10.3).
        /// </summary>
        public void SendPeek(int playerId, BelieveCard card)
        {
            if (!IsSpawned || !IsServer || playerId < 0)
            {
                return;
            }

            var clientId = (ulong)playerId;
            if (clientId == NetworkManager.ServerClientId || !NetworkManager.ConnectedClients.ContainsKey(clientId))
            {
                // Свою карточку хост показывает себе на месте, а ушедшему слать некуда.
                return;
            }

            ShowPeekRpc((byte)card, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ShowPeekRpc(byte card, RpcParams rpcParams = default) => game?.ApplyPeek((BelieveCard)card);

        // ========== СОБЫТИЯ КОНА ==========

        /// <summary>Сидящий сказал реплику — пузырь над его головой у всех в зале.</summary>
        public void AnnouncePhrase(int playerId, int phraseIndex)
        {
            if (IsSpawned && IsServer)
            {
                AnnouncePhraseRpc(playerId, phraseIndex);
            }
        }

        [Rpc(SendTo.NotServer)]
        private void AnnouncePhraseRpc(int playerId, int phraseIndex) => game?.ApplyPhrase(playerId, phraseIndex);

        /// <summary>
        /// Объявить раскрытие. <b>Это и есть публикация содержимого коробок</b> —
        /// в тот момент, когда его и так увидит весь зал.
        ///
        /// Решение едет тем же пакетом, а не читается из реплицированного
        /// состояния, намеренно: порядок доставки разных каналов не
        /// гарантирован, и анимация обмена разъехалась бы с исходом —
        /// коробки поменялись бы местами уже после того, как крышки открылись.
        /// </summary>
        public void AnnounceReveal(Decision decision, BelieveCard seat0Card, BelieveCard seat1Card)
        {
            if (IsSpawned && IsServer)
            {
                AnnounceRevealRpc((byte)decision, (byte)seat0Card, (byte)seat1Card);
            }
        }

        [Rpc(SendTo.NotServer)]
        private void AnnounceRevealRpc(byte decision, byte seat0Card, byte seat1Card) =>
            game?.ApplyReveal((Decision)decision, (BelieveCard)seat0Card, (BelieveCard)seat1Card);

        // ========== ДЕЙСТВИЯ ИГРОКА ==========

        /// <summary>
        /// Отправить решение серверу. Зовёт только тот, кто считает себя
        /// Решающим; право на это сервер всё равно перепроверяет сам.
        /// </summary>
        public void SubmitDecision(Decision decision)
        {
            if (IsSpawned)
            {
                SubmitDecisionRpc((byte)decision);
            }
        }

        /// <summary>
        /// Приём решения на сервере. <c>RequireOwnership = false</c> потому,
        /// что объект принадлежит серверу, а шлёт клиент, — право проверяется
        /// не владением, а ролью Решающего.
        ///
        /// Отправитель берётся из <c>RpcParams</c>, а не из аргумента: иначе
        /// клиент решал бы за соседа.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void SubmitDecisionRpc(byte decision, RpcParams rpcParams = default) =>
            game?.HandleDecision((int)rpcParams.Receive.SenderClientId, (Decision)decision);

        /// <summary>Отправить реплику серверу. Индекс проверяет сервер.</summary>
        public void SubmitPhrase(int phraseIndex)
        {
            if (IsSpawned)
            {
                SubmitPhraseRpc(phraseIndex);
            }
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void SubmitPhraseRpc(int phraseIndex, RpcParams rpcParams = default) =>
            game?.HandlePhrase((int)rpcParams.Receive.SenderClientId, phraseIndex);

        // ========== ПРИЁМ НА КЛИЕНТЕ ==========

        private void OnMatchChanged(BelieveMatchNetState previous, BelieveMatchNetState current) => matchDirty = true;

        private void OnStageChanged(BelieveStageNetState previous, BelieveStageNetState current) => stageDirty = true;

        private void OnEntriesChanged(NetworkListEvent<BelieveEntryNetState> change) => entriesDirty = true;

        private void LateUpdate()
        {
            if (!IsSpawned || IsServer || game == null)
            {
                return;
            }

            // Ростер мог собраться уже после того, как состояние разобрали:
            // тогда разбираем заново, иначе сидящий клиент, у которого так
            // совпало, досидит кон с камерой зрителя и без блокировки.
            if (game.RosterCount != appliedRoster)
            {
                appliedRoster = game.RosterCount;
                matchDirty = true;
                stageDirty = true;
                entriesDirty = true;
            }

            // Порядок обязателен: стадия рисует картинку по местам за столом,
            // и разобрать её раньше состава кона значит поставить камеру
            // прошлого кона.
            if (matchDirty)
            {
                matchDirty = false;
                game.ApplyNetworkMatch(match.Value);
            }

            if (stageDirty)
            {
                stageDirty = false;
                BelieveStageNetState s = stage.Value;
                if (s.Stage != MinigameStageState.NoStage && stageState != null)
                {
                    stageState.ApplyState(s.Round, s.Stage, s.EndTime, s.Duration);
                }
            }

            if (entriesDirty)
            {
                entriesDirty = false;
                for (int i = 0; i < entries.Count; i++)
                {
                    game.ApplyNetworkEntry(entries[i]);
                }

                game.ApplyNetworkEntriesEnd();
            }
        }

        // ========== ДИСКОННЕКТ ==========

        /// <summary>
        /// Игрок ушёл. Решение принимает контроллер: судьба кона зависит от
        /// того, кем был ушедший и успел ли Решающий решить (спека 10.4).
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (IsServer)
            {
                game?.HandlePlayerLeft((int)clientId);
            }
        }
    }
}
