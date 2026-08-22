using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Задание раунда и положение круга — одной структурой.
    ///
    /// Вместе, а не по отдельности, потому что меняются они всегда вместе:
    /// номер раунда, число банок и квота выставляются одним решением сервера,
    /// и приехавшая раньше новая квота под старым номером раунда означала бы
    /// клетки, поехавшие по чужому знаменателю.
    ///
    /// <b>Скрытой расстановки здесь нет и быть не может.</b> Это и есть ответ:
    /// он живёт только в серверном поле контроллера и не реплицируется ни в
    /// каком виде до конца раунда (спека 10.1).
    /// </summary>
    public struct CansOrderRoundNetState : INetworkSerializable, IEquatable<CansOrderRoundNetState>
    {
        public int Round;
        public int Circle;
        public byte CanCount;
        public byte Quota;
        public byte AliveAtStart;
        public byte SolvedCount;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Round);
            serializer.SerializeValue(ref Circle);
            serializer.SerializeValue(ref CanCount);
            serializer.SerializeValue(ref Quota);
            serializer.SerializeValue(ref AliveAtStart);
            serializer.SerializeValue(ref SolvedCount);
        }

        public bool Equals(CansOrderRoundNetState other) =>
            Round == other.Round &&
            Circle == other.Circle &&
            CanCount == other.CanCount &&
            Quota == other.Quota &&
            AliveAtStart == other.AliveAtStart &&
            SolvedCount == other.SolvedCount;
    }

    /// <summary>
    /// Стадия круга и момент её конца. Конец — момент на общих часах, а не
    /// остаток: остаток пришлось бы досылать каждый кадр, момент достаточно
    /// объявить один раз. Тот же приём, что у «Секундомера».
    /// </summary>
    public struct CansOrderStageNetState : INetworkSerializable, IEquatable<CansOrderStageNetState>
    {
        public int Circle;
        public byte Stage;
        public double EndTime;
        public float Duration;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Circle);
            serializer.SerializeValue(ref Stage);
            serializer.SerializeValue(ref EndTime);
            serializer.SerializeValue(ref Duration);
        }

        public bool Equals(CansOrderStageNetState other) =>
            Circle == other.Circle &&
            Stage == other.Stage &&
            EndTime.Equals(other.EndTime) &&
            Mathf.Approximately(Duration, other.Duration);
    }

    /// <summary>
    /// Состояние одного участника в сети.
    ///
    /// <b>Совпадения, факт подтверждения и «собрал в этом круге» сервер
    /// заполняет только со стадии показа результатов.</b> До неё их в сетевом
    /// состоянии нет физически, а не спрятаны в интерфейсе: клиент, читающий
    /// состояние напрямую, не должен найти там свой счёт раньше остальных —
    /// на этом держится вся честность игры (спека 10.1 и 4).
    ///
    /// Признак <see cref="Revealed"/> едет рядом с ними намеренно. Флаг стадии
    /// и этот список — два разных канала, и порядок их прибытия не гарантирован:
    /// без явного признака клиент мог бы применить нули показа как настоящий
    /// счёт круга.
    /// </summary>
    public struct CansOrderEntryNetState : INetworkSerializable, IEquatable<CansOrderEntryNetState>
    {
        public int PlayerId;
        public bool Alive;

        /// <summary>Собрал расстановку и до конца раунда не участвует.</summary>
        public bool Solved;

        /// <summary>Дно распахнуто. Объявляет сервер, падение дальше — обычная гравитация у владельца.</summary>
        public bool DoorsOpen;

        public byte Attempts;

        /// <summary>Доля высоты клетки 0…1. Сама анимация считается локально от начала стадии.</summary>
        public float HeightFraction;

        /// <summary>Данные круга открыты: идёт стадия показа результатов.</summary>
        public bool Revealed;

        public bool Confirmed;
        public bool SolvedThisCircle;
        public byte Matches;

        /// <summary>
        /// Подтверждённая расстановка, по байту на банку: младший байт — слот 0.
        ///
        /// Табло показывает три лучшие расстановки целиком, и вывести их
        /// из чисел нельзя — значит, они обязаны приехать. Едут упакованными
        /// в одно число, потому что <c>NetworkList</c> держит только
        /// unmanaged-структуры, а массив внутрь такой не положишь.
        ///
        /// <b>Расстановка собравшего сюда не попадает никогда</b> — ни в стадии
        /// показа, ни после. Ответ раунда складывается ровно из неё, и клиент
        /// не должен найти её в сетевом состоянии (спека 5.3 и 12).
        /// </summary>
        public ulong Arrangement;

        /// <summary>Сколько банок в <see cref="Arrangement"/>. Ноль — расстановки в пакете нет.</summary>
        public byte ArrangementCount;

        /// <summary>Сколько банок влезает в упаковку: восемь байт числа — восемь банок.</summary>
        public const int MaxPackedCans = 8;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Alive);
            serializer.SerializeValue(ref Solved);
            serializer.SerializeValue(ref DoorsOpen);
            serializer.SerializeValue(ref Attempts);
            serializer.SerializeValue(ref HeightFraction);
            serializer.SerializeValue(ref Revealed);
            serializer.SerializeValue(ref Confirmed);
            serializer.SerializeValue(ref SolvedThisCircle);
            serializer.SerializeValue(ref Matches);
            serializer.SerializeValue(ref Arrangement);
            serializer.SerializeValue(ref ArrangementCount);
        }

        public bool Equals(CansOrderEntryNetState other) =>
            PlayerId == other.PlayerId &&
            Alive == other.Alive &&
            Solved == other.Solved &&
            DoorsOpen == other.DoorsOpen &&
            Attempts == other.Attempts &&
            Revealed == other.Revealed &&
            Confirmed == other.Confirmed &&
            SolvedThisCircle == other.SolvedThisCircle &&
            Matches == other.Matches &&
            Arrangement == other.Arrangement &&
            ArrangementCount == other.ArrangementCount &&
            Mathf.Approximately(HeightFraction, other.HeightFraction);

        /// <summary>
        /// Упаковать расстановку в одно число. Длиннее
        /// <see cref="MaxPackedCans"/> не упаковывается — тогда табло покажет
        /// счёт без расстановки, а не соврёт обрезанной.
        /// </summary>
        public static bool TryPack(IReadOnlyList<int> arrangement, out ulong packed, out byte count)
        {
            packed = 0ul;
            count = 0;

            if (arrangement == null || arrangement.Count == 0 || arrangement.Count > MaxPackedCans)
            {
                return false;
            }

            for (int i = 0; i < arrangement.Count; i++)
            {
                int id = arrangement[i];
                if (id < 0 || id > byte.MaxValue)
                {
                    packed = 0ul;
                    return false;
                }

                packed |= (ulong)id << (i * 8);
            }

            count = (byte)arrangement.Count;
            return true;
        }

        /// <summary>Развернуть упакованную расстановку обратно в список слотов.</summary>
        public static void Unpack(ulong packed, byte count, List<int> into)
        {
            into.Clear();
            int length = Mathf.Min(count, MaxPackedCans);
            for (int i = 0; i < length; i++)
            {
                into.Add((int)((packed >> (i * 8)) & 0xFFul));
            }
        }
    }

    /// <summary>
    /// Сетевая половина «Порядка банок»: вешается на тот же объект, что и
    /// <see cref="CansOrderMinigame"/>. Правила остаются обычным MonoBehaviour
    /// и работают без этого компонента, когда сцену открывают напрямую, —
    /// образец взят у <c>StopwatchNetwork</c>.
    ///
    /// Фазу, время раунда и итоговые места везёт
    /// <see cref="Igruha.Networking.NetworkMinigameBridge"/> рядом на том же
    /// объекте — своего канала под них здесь нет намеренно.
    ///
    /// Что реплицируется здесь: задание раунда, стадия с моментом конца,
    /// состояние каждого участника и состояние медведя.
    ///
    /// <b>Чего здесь нет:</b>
    /// <list type="bullet">
    /// <item>скрытой расстановки — это ответ, он не покидает сервер (10.1);</item>
    /// <item>перестановок банок в окне выставления — полка локальна, за круг
    /// уходит один пакет на игрока, подтверждённая расстановка (10.2);</item>
    /// <item>содержимого табло — оно считается локально из состояния выше
    /// по детерминированному правилу сортировки, тройка сходится у всех сама.</item>
    /// </list>
    /// </summary>
    public sealed class CansOrderNetwork : NetworkBehaviour
    {
        private readonly NetworkVariable<CansOrderRoundNetState> round =
            new NetworkVariable<CansOrderRoundNetState>();

        private readonly NetworkVariable<CansOrderStageNetState> stage =
            new NetworkVariable<CansOrderStageNetState>();

        private readonly NetworkList<CansOrderEntryNetState> entries = new NetworkList<CansOrderEntryNetState>();

        /// <summary>
        /// Состояние медведя. Позицию везёт серверный NetworkTransform, а вот
        /// рёв и стойка на лапах — решение сервера: иначе на одной машине
        /// медведь дразнит клетку, а на другой молча ходит кругами.
        /// </summary>
        private readonly NetworkVariable<byte> bearState = new NetworkVariable<byte>();

        /// <summary>Буфер под расстановку, приехавшую по сети. Поле, а не локальная переменная: круг за кругом одно и то же.</summary>
        private readonly List<int> incoming = new List<int>(8);

        /// <summary>Ушедшие, которых осталось разобрать. Почему не сразу — см. OnClientDisconnected.</summary>
        private readonly List<ulong> pendingLeavers = new List<ulong>(4);

        private CansOrderMinigame game;
        private MinigameStageState stageState;

        /// <summary>Список участников приехал и ещё не разобран. Только на клиенте.</summary>
        private bool entriesDirty;

        /// <summary>Задание раунда приехало и ещё не применено. Только на клиенте.</summary>
        private bool roundDirty;

        /// <summary>Стадия приехала и ещё не применена. Только на клиенте.</summary>
        private bool stageDirty;

        /// <summary>
        /// Состав, под который состояние уже разобрано.
        ///
        /// Порядок спавна этого объекта и старта мини-игры ничем не связан:
        /// сетевое состояние вполне может приехать раньше, чем контроллер
        /// соберёт клетки, и тогда разбирать его было не по кому. Как только
        /// состав меняется, разбираем заново — иначе клиент, у которого так
        /// совпало, остался бы без полок до конца раунда.
        /// </summary>
        private int appliedContestants = -1;

        /// <summary>Идёт сетевая катка и эта половина живая.</summary>
        public bool IsActive => IsSpawned;

        private void Awake()
        {
            game = GetComponent<CansOrderMinigame>();
            if (game == null)
            {
                Debug.LogError($"{name}: CansOrderNetwork не нашёл CansOrderMinigame на своём объекте", this);
            }

            stageState = GetComponent<MinigameStageState>();
            if (stageState == null)
            {
                Debug.LogError($"{name}: CansOrderNetwork не нашёл MinigameStageState на своём объекте", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            round.OnValueChanged += OnRoundChanged;
            stage.OnValueChanged += OnStageChanged;
            entries.OnListChanged += OnEntriesChanged;
            bearState.OnValueChanged += OnBearStateChanged;

            if (IsServer)
            {
                // Стадию объявляет тот же, кто её начал: событие приходит уже
                // после того, как момент конца проставлен.
                if (stageState != null)
                {
                    stageState.StageStarted += OnServerStageStarted;
                }

                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                return;
            }

            // Подключились в середине матча — догоняем то, что уже решено.
            ApplyBearState();
            roundDirty = true;
            stageDirty = true;
            entriesDirty = true;
        }

        public override void OnNetworkDespawn()
        {
            round.OnValueChanged -= OnRoundChanged;
            stage.OnValueChanged -= OnStageChanged;
            entries.OnListChanged -= OnEntriesChanged;
            bearState.OnValueChanged -= OnBearStateChanged;

            if (IsServer)
            {
                if (stageState != null)
                {
                    stageState.StageStarted -= OnServerStageStarted;
                }

                if (NetworkManager != null)
                {
                    NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                }
            }

            base.OnNetworkDespawn();
        }

        // ========== ЗАДАНИЕ РАУНДА И КРУГ ==========

        private void OnRoundChanged(CansOrderRoundNetState previous, CansOrderRoundNetState current)
        {
            if (!IsServer)
            {
                roundDirty = true;
            }
        }

        private void ApplyRound()
        {
            CansOrderRoundNetState value = round.Value;
            if (value.Round <= 0)
            {
                return;
            }

            game?.ApplyNetworkRound(value.Round, value.Circle, value.CanCount,
                value.Quota, value.AliveAtStart, value.SolvedCount);
        }

        // ========== СТАДИЯ ==========

        private void OnServerStageStarted(byte started)
        {
            if (!IsSpawned || !IsServer || stageState == null)
            {
                return;
            }

            stage.Value = new CansOrderStageNetState
            {
                Circle = stageState.Subround,
                Stage = started,
                EndTime = stageState.StageEndTime,
                Duration = stageState.StageDuration
            };
        }

        private void OnStageChanged(CansOrderStageNetState previous, CansOrderStageNetState current)
        {
            if (!IsServer)
            {
                stageDirty = true;
            }
        }

        private void ApplyStage()
        {
            CansOrderStageNetState value = stage.Value;
            if (value.Stage == MinigameStageState.NoStage || stageState == null)
            {
                return;
            }

            stageState.ApplyState(value.Circle, value.Stage, value.EndTime, value.Duration);
        }

        // ========== СТАРТОВАЯ РАССТАНОВКА ПОЛКИ ==========

        /// <summary>
        /// Отдать игроку его стартовую расстановку. Адресно и только ему:
        /// у каждого она своя, и чужая никого не касается — одинаковая на всех
        /// дала бы за первый круг один отклик вместо восьми (спека 8.2).
        ///
        /// Рандом при этом остаётся серверным целиком: клиент получает готовый
        /// результат, а не сид, по которому его можно было бы повторить.
        /// </summary>
        public void SendShelf(int playerId, IReadOnlyList<int> arrangement)
        {
            if (!IsSpawned || !IsServer || arrangement == null)
            {
                return;
            }

            var clientId = (ulong)playerId;
            if (clientId == NetworkManager.ServerClientId || !NetworkManager.ConnectedClients.ContainsKey(clientId))
            {
                // Своя полка у хоста уже разложена на месте, а ушедшему слать некуда.
                return;
            }

            AssignShelfRpc(ToBytes(arrangement), RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void AssignShelfRpc(byte[] arrangement, RpcParams rpcParams = default)
        {
            ToIntList(arrangement, incoming);
            game?.ApplyNetworkShelf(incoming);
        }

        // ========== ПОДТВЕРЖДЕНИЕ РАССТАНОВКИ ==========

        /// <summary>
        /// Владелец полки отправляет подтверждённую расстановку. Зовётся только
        /// на клиенте: у сервера исход считается на месте.
        ///
        /// <b>Один пакет на игрока за круг</b> — не полтора десятка перестановок,
        /// а одна расстановка (спека 10.2). Метка — момент нажатия на общих
        /// часах: она решает, попал ли игрок в окно, когда пакет опоздал из-за
        /// пинга.
        /// </summary>
        public void SubmitArrangement(IReadOnlyList<int> arrangement, double stamp)
        {
            if (!IsSpawned || IsServer || arrangement == null)
            {
                return;
            }

            SubmitArrangementRpc(ToBytes(arrangement), stamp);
        }

        /// <summary>
        /// Сервер принимает намерение. Отправителя берём из <c>RpcParams</c>,
        /// а не из аргумента: иначе клиент подтверждал бы за соседа.
        ///
        /// Проверяет всё пять правил 10.3 сам контроллер — он один знает стадию,
        /// палитру раунда и состав живых. Сюда возвращается только ответ,
        /// и уходит он адресно тому же игроку: лампа обязана загораться
        /// от решения сервера, а не от факта нажатия.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void SubmitArrangementRpc(byte[] arrangement, double stamp, RpcParams rpcParams = default)
        {
            if (game == null)
            {
                return;
            }

            ulong senderId = rpcParams.Receive.SenderClientId;
            ToIntList(arrangement, incoming);

            bool accepted = game.ServerApplyArrangement((int)senderId, incoming, stamp, NetworkManager.ServerTime.Time);
            ConfirmAnswerRpc(accepted, RpcTarget.Single(senderId, RpcTargetUse.Temp));
        }

        /// <summary>
        /// Ответ сервера тому, кто подтверждал, и никому больше.
        ///
        /// Отдельным адресным пакетом, а не полем в общем состоянии: факт
        /// «этот уже подтвердил» до стадии показа не должен знать никто, кроме
        /// него самого. В общем списке он появится вместе со всеми остальными
        /// числами круга — то есть на стадии показа.
        /// </summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void ConfirmAnswerRpc(bool accepted, RpcParams rpcParams = default)
        {
            game?.ApplyNetworkConfirmAnswer(accepted);
        }

        // ========== СОБЫТИЯ КРУГА ==========

        /// <summary>
        /// Игрок собрал расстановку — фанфара над его клеткой.
        ///
        /// Событие, а не состояние: конфетти нужны один раз и в свою секунду,
        /// держать их в реплицируемом поле незачем.
        /// </summary>
        public void AnnounceSolved(int playerId)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            AnnounceSolvedRpc(playerId);
        }

        /// <summary>Сервер уже отыграл фанфару у себя, поэтому себе не шлём.</summary>
        [Rpc(SendTo.NotServer)]
        private void AnnounceSolvedRpc(int playerId)
        {
            game?.ApplyNetworkSolvedFanfare(playerId);
        }

        /// <summary>
        /// Медведь достал игрока. Тоже событие: направление отлёта нужно один
        /// раз и в момент удара, и приезжает оно готовым — тогда клип падения
        /// выбирается одинаково у всех.
        /// </summary>
        public void AnnounceCaught(int playerId, Vector3 hitPoint, Vector3 impulse)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            AnnounceCaughtRpc(playerId, hitPoint, impulse);
        }

        /// <summary>Сервер уже применил гибель у себя, поэтому себе не шлём.</summary>
        [Rpc(SendTo.NotServer)]
        private void AnnounceCaughtRpc(int playerId, Vector3 hitPoint, Vector3 impulse)
        {
            game?.ApplyNetworkCaught(playerId, hitPoint, impulse);
        }

        // ========== МЕДВЕДЬ ==========

        /// <summary>Сервер объявил, в каком состоянии медведь. Двигают его серверный ИИ и NetworkTransform.</summary>
        public void PublishBearState(byte state)
        {
            if (!IsSpawned || !IsServer || bearState.Value == state)
            {
                return;
            }

            bearState.Value = state;
        }

        private void OnBearStateChanged(byte previous, byte current)
        {
            if (!IsServer)
            {
                ApplyBearState();
            }
        }

        private void ApplyBearState() => game?.ApplyNetworkBearState(bearState.Value);

        // ========== УХОД ИГРОКА ==========

        /// <summary>
        /// Разбираем уход не здесь, а на ближайшем тике — тем же приёмом, что
        /// у «Секундомера» и «Ангелов». Этот колбэк приходит и когда выключается
        /// сам сервер, а отличить два случая по состоянию NetworkManager нельзя.
        /// При обычном выходе игрока тик будет, при выключении сервера тиков
        /// больше нет — и заканчивать матч некому и незачем.
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (IsServer)
            {
                pendingLeavers.Add(clientId);
            }
        }

        private void ApplyPendingLeavers()
        {
            if (NetworkManager == null || NetworkManager.ShutdownInProgress || !NetworkManager.IsListening)
            {
                return;
            }

            for (int i = 0; i < pendingLeavers.Count; i++)
            {
                game?.HandlePlayerLeft((int)pendingLeavers[i]);
            }

            pendingLeavers.Clear();
        }

        // ========== СОСТОЯНИЕ УЧАСТНИКОВ ==========

        /// <summary>
        /// Сервер сверяет своё состояние с разосланным и досылает разницу.
        ///
        /// Опросом, а не толчком из каждой точки изменения: точек много
        /// (подтверждение, разбор круга, спуск клеток, створки, уход игрока),
        /// и забытый вызов дал бы молчаливый рассинхрон вместо ошибки. Запись
        /// идёт только при реальном расхождении, поэтому трафика в покое нет.
        ///
        /// Задание раунда сверяется здесь же и по той же причине: круг и число
        /// собравших меняются в четырёх разных местах правил.
        /// </summary>
        private void Update()
        {
            if (!IsSpawned || game == null)
            {
                return;
            }

            if (!IsServer)
            {
                // Состав собрался (или ушедшего убрали) — разбираем приехавшее
                // заново: до этого момента применять его было не к кому.
                int count = game.ContestantCount;
                if (count != appliedContestants)
                {
                    // Стадию переигрываем только в одном случае — когда состав
                    // появился впервые. Посреди раунда это открывало бы окно
                    // выставления заново, и полка подтвердившего оживала бы
                    // от чужого дисконнекта: ровно та поломка, из-за которой
                    // на плейтесте 22.08 полку и научили замирать.
                    bool rosterAppeared = appliedContestants <= 0 && count > 0;

                    appliedContestants = count;
                    roundDirty = true;
                    entriesDirty = true;
                    stageDirty |= rosterAppeared;
                }

                if (roundDirty)
                {
                    roundDirty = false;
                    ApplyRound();
                }

                if (stageDirty)
                {
                    stageDirty = false;
                    ApplyStage();
                }

                // Разбираем список раз за кадр, а не на каждой дельте: восемь
                // изменившихся участников дали бы восемь полных перерисовок
                // табло с построением строк.
                if (entriesDirty)
                {
                    entriesDirty = false;
                    ApplyEntries();
                }

                return;
            }

            if (pendingLeavers.Count > 0)
            {
                ApplyPendingLeavers();
            }

            PublishRound();
            PublishEntries();
        }

        private void PublishRound()
        {
            CansOrderRoundState current = game.Round;
            var next = new CansOrderRoundNetState
            {
                Round = current.Round,
                Circle = current.Circle,
                CanCount = (byte)Mathf.Clamp(current.CanCount, 0, byte.MaxValue),
                Quota = (byte)Mathf.Clamp(current.Quota, 0, byte.MaxValue),
                AliveAtStart = (byte)Mathf.Clamp(current.AliveAtStart, 0, byte.MaxValue),
                SolvedCount = (byte)Mathf.Clamp(current.SolvedCount, 0, byte.MaxValue)
            };

            if (!round.Value.Equals(next))
            {
                round.Value = next;
            }
        }

        private void PublishEntries()
        {
            int count = game.ContestantCount;

            while (entries.Count > count)
            {
                entries.RemoveAt(entries.Count - 1);
            }

            for (int i = 0; i < count; i++)
            {
                if (!game.TryGetEntryNetState(i, out CansOrderEntryNetState state))
                {
                    continue;
                }

                if (i >= entries.Count)
                {
                    entries.Add(state);
                    continue;
                }

                if (!entries[i].Equals(state))
                {
                    entries[i] = state;
                }
            }
        }

        private void OnEntriesChanged(NetworkListEvent<CansOrderEntryNetState> change)
        {
            if (!IsServer)
            {
                entriesDirty = true;
            }
        }

        private void ApplyEntries()
        {
            if (game == null)
            {
                return;
            }

            game.ApplyNetworkEntriesBegin();

            for (int i = 0; i < entries.Count; i++)
            {
                game.ApplyNetworkEntry(entries[i]);
            }

            game.ApplyNetworkEntriesCommitted();
        }

        // ========== ПЕРЕВОД РАССТАНОВКИ ==========

        /// <summary>
        /// Расстановка едет байтами: идентификатор банки — это индекс в палитре
        /// раунда, а палитра заведомо короче 256 позиций. Массив под неё живёт
        /// один пакет и один раз за круг, поэтому аллокация здесь не в цикле
        /// кадра и ничего не стоит.
        /// </summary>
        private static byte[] ToBytes(IReadOnlyList<int> arrangement)
        {
            var payload = new byte[arrangement.Count];
            for (int i = 0; i < arrangement.Count; i++)
            {
                payload[i] = (byte)Mathf.Clamp(arrangement[i], 0, byte.MaxValue);
            }

            return payload;
        }

        private static void ToIntList(byte[] payload, List<int> into)
        {
            into.Clear();
            if (payload == null)
            {
                return;
            }

            for (int i = 0; i < payload.Length; i++)
            {
                into.Add(payload[i]);
            }
        }
    }
}
