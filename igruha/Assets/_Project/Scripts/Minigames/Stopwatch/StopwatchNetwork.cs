using System;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>
    /// Задание подраунда: номер, тип, цель, период тика. Одной структурой
    /// потому, что меняются они всегда вместе — четыре отдельных значения
    /// приезжали бы разными пакетами, и клиент успел бы показать новую цель
    /// под старый тип.
    /// </summary>
    public struct StopwatchTaskState : INetworkSerializable, IEquatable<StopwatchTaskState>
    {
        public int Subround;

        /// <summary><see cref="StopwatchSubroundType"/> числом: enum в сеть не кладётся.</summary>
        public byte Type;

        public float Target;

        /// <summary>Период тика, с. Крутит сервер: под разную подсказку мерили бы разное.</summary>
        public float TickPeriod;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Subround);
            serializer.SerializeValue(ref Type);
            serializer.SerializeValue(ref Target);
            serializer.SerializeValue(ref TickPeriod);
        }

        public bool Equals(StopwatchTaskState other) =>
            Subround == other.Subround &&
            Type == other.Type &&
            Mathf.Approximately(Target, other.Target) &&
            Mathf.Approximately(TickPeriod, other.TickPeriod);
    }

    /// <summary>
    /// Стадия подраунда и момент её конца. Конец — момент на общих часах,
    /// а не остаток: остаток пришлось бы досылать каждый кадр, момент
    /// достаточно объявить один раз.
    /// </summary>
    public struct StopwatchStageNetState : INetworkSerializable, IEquatable<StopwatchStageNetState>
    {
        public int Subround;
        public byte Stage;
        public double EndTime;
        public float Duration;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Subround);
            serializer.SerializeValue(ref Stage);
            serializer.SerializeValue(ref EndTime);
            serializer.SerializeValue(ref Duration);
        }

        public bool Equals(StopwatchStageNetState other) =>
            Subround == other.Subround &&
            Stage == other.Stage &&
            EndTime.Equals(other.EndTime) &&
            Mathf.Approximately(Duration, other.Duration);
    }

    /// <summary>
    /// Состояние одной клетки. Замер и признак «успел» лежат здесь же, но
    /// сервер заполняет их **только в стадии показа результатов**: до неё
    /// в сетевом состоянии их нет физически, а не спрятаны в интерфейсе.
    /// Иначе модифицированный клиент читал бы чужой результат прямо из памяти,
    /// и §5.1 спеки держался бы на честном слове.
    /// </summary>
    public struct CageNetState : INetworkSerializable, IEquatable<CageNetState>
    {
        public int PlayerId;
        public byte Errors;

        /// <summary>Ступень клетки: сколько ошибок ещё можно сделать.</summary>
        public byte Level;

        /// <summary>
        /// Кнопку начали держать. Горит от старта и до конца стадии отмера —
        /// момент «стопа» наружу не уходит, иначе на типе «Потолок» сосед
        /// отсчитывал бы по чужой погасшей лампе.
        /// </summary>
        public bool Lit;

        public bool Alive;
        public bool Faulted;

        /// <summary>Дно распахнуто. Объявляет сервер, падение дальше — обычная гравитация у владельца.</summary>
        public bool DoorsOpen;

        /// <summary>Отмеренный интервал. Ноль вне стадии показа результатов.</summary>
        public float Measured;

        /// <summary>Успел закрыть отсчёт. Ложь вне стадии показа результатов.</summary>
        public bool Completed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Errors);
            serializer.SerializeValue(ref Level);
            serializer.SerializeValue(ref Lit);
            serializer.SerializeValue(ref Alive);
            serializer.SerializeValue(ref Faulted);
            serializer.SerializeValue(ref DoorsOpen);
            serializer.SerializeValue(ref Measured);
            serializer.SerializeValue(ref Completed);
        }

        public bool Equals(CageNetState other) =>
            PlayerId == other.PlayerId &&
            Errors == other.Errors &&
            Level == other.Level &&
            Lit == other.Lit &&
            Alive == other.Alive &&
            Faulted == other.Faulted &&
            DoorsOpen == other.DoorsOpen &&
            Completed == other.Completed &&
            Mathf.Approximately(Measured, other.Measured);
    }

    /// <summary>
    /// Сетевая половина «Секундомера»: вешается на тот же объект, что и
    /// <see cref="StopwatchMinigame"/>. Правила остаются обычным MonoBehaviour
    /// и работают без этого компонента, когда сцену открывают напрямую —
    /// образец взят у <c>CryingAngelsNetwork</c>.
    ///
    /// Фазу, время раунда и итоговые места везёт
    /// <see cref="Igruha.Networking.NetworkMinigameBridge"/> рядом на том же
    /// объекте — своего канала под них здесь нет намеренно.
    ///
    /// Что реплицируется здесь: задание подраунда, стадия с моментом конца
    /// и состояние каждой клетки.
    /// </summary>
    public sealed class StopwatchNetwork : NetworkBehaviour
    {
        /// <summary>
        /// На сколько метка клиента может отставать от момента прибытия, с.
        /// Это дорога пакета плюс запас на джиттер: полсекунды покрывают
        /// любой играбельный пинг, а всё, что старше, — уже не «долетело
        /// с задержкой», а подделка или зависший клиент.
        /// </summary>
        private const double StampMaxBehind = 0.5d;

        /// <summary>
        /// На сколько метка может опережать прибытие, с. Ноль поставить нельзя:
        /// часы NGO расходятся на единицы миллисекунд, и честная метка иногда
        /// оказывается чуть впереди.
        /// </summary>
        private const double StampMaxAhead = 0.05d;

        private readonly NetworkVariable<StopwatchTaskState> task = new NetworkVariable<StopwatchTaskState>();

        private readonly NetworkVariable<StopwatchStageNetState> stage = new NetworkVariable<StopwatchStageNetState>();

        private readonly NetworkList<CageNetState> cages = new NetworkList<CageNetState>();

        /// <summary>
        /// Состояние медведя. Позицию везёт серверный NetworkTransform, а вот
        /// рёв и стойка на лапах — решение сервера: иначе на одной машине
        /// медведь дразнит клетку, а на другой молча ходит кругами.
        /// </summary>
        private readonly NetworkVariable<byte> bearState = new NetworkVariable<byte>();

        private StopwatchMinigame game;
        private MinigameStageState stageState;

        /// <summary>Список клеток приехал и ещё не отрисован. Только на клиенте.</summary>
        private bool cagesDirty;

        /// <summary>Ушедшие, которых осталось разобрать. Почему не сразу — см. OnClientDisconnected.</summary>
        private readonly System.Collections.Generic.List<ulong> pendingLeavers = new System.Collections.Generic.List<ulong>(4);

        /// <summary>Идёт сетевая катка и эта половина живая.</summary>
        public bool IsActive => IsSpawned;

        private void Awake()
        {
            game = GetComponent<StopwatchMinigame>();
            if (game == null)
            {
                Debug.LogError($"{name}: StopwatchNetwork не нашёл StopwatchMinigame на своём объекте", this);
            }

            stageState = GetComponent<MinigameStageState>();
            if (stageState == null)
            {
                Debug.LogError($"{name}: StopwatchNetwork не нашёл MinigameStageState на своём объекте", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            task.OnValueChanged += OnTaskChanged;
            stage.OnValueChanged += OnStageChanged;
            cages.OnListChanged += OnCagesChanged;
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

            // Подключились в середине раунда — догоняем то, что уже решено.
            ApplyTask();
            ApplyStage();
            ApplyBearState();
            cagesDirty = true;
        }

        public override void OnNetworkDespawn()
        {
            task.OnValueChanged -= OnTaskChanged;
            stage.OnValueChanged -= OnStageChanged;
            cages.OnListChanged -= OnCagesChanged;
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

        // ========== ЗАДАНИЕ ПОДРАУНДА ==========

        /// <summary>Сервер объявил новый подраунд. Весь рандом уже отыгран у него.</summary>
        public void PublishTask(int subround, StopwatchSubroundType type, float target, float tickPeriod)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            task.Value = new StopwatchTaskState
            {
                Subround = subround,
                Type = (byte)type,
                Target = target,
                TickPeriod = tickPeriod
            };
        }

        private void OnTaskChanged(StopwatchTaskState previous, StopwatchTaskState current)
        {
            if (!IsServer)
            {
                ApplyTask();
            }
        }

        private void ApplyTask()
        {
            StopwatchTaskState value = task.Value;
            if (value.Subround <= 0)
            {
                return;
            }

            game?.ApplyNetworkTask(value.Subround, (StopwatchSubroundType)value.Type, value.Target, value.TickPeriod);
        }

        // ========== СТАДИЯ ==========

        private void OnServerStageStarted(byte started)
        {
            if (!IsSpawned || !IsServer || stageState == null)
            {
                return;
            }

            stage.Value = new StopwatchStageNetState
            {
                Subround = stageState.Subround,
                Stage = started,
                EndTime = stageState.StageEndTime,
                Duration = stageState.StageDuration
            };
        }

        private void OnStageChanged(StopwatchStageNetState previous, StopwatchStageNetState current)
        {
            if (!IsServer)
            {
                ApplyStage();
            }
        }

        private void ApplyStage()
        {
            StopwatchStageNetState value = stage.Value;
            if (value.Stage == MinigameStageState.NoStage || stageState == null)
            {
                return;
            }

            stageState.ApplyState(value.Subround, value.Stage, value.EndTime, value.Duration);
        }

        // ========== УХОД ИГРОКА ==========

        /// <summary>
        /// Разбираем уход не здесь, а на ближайшем тике — тем же приёмом, что
        /// у «Ангелов». Этот колбэк приходит и когда выключается сам сервер,
        /// а отличить два случая по состоянию NetworkManager нельзя. При обычном
        /// выходе игрока тик будет, при выключении сервера тиков больше нет —
        /// и заканчивать матч некому и незачем.
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

        private void ApplyBearState()
        {
            game?.ApplyNetworkBearState(bearState.Value);
        }

        /// <summary>
        /// Медведь достал игрока. Это событие, а не состояние, поэтому уходит
        /// RPC: направление отлёта нужно один раз и в момент удара, держать его
        /// в реплицируемом поле незачем.
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

        // ========== НАЖАТИЕ ==========

        /// <summary>
        /// Владелец кнопки отправляет намерение с меткой момента нажатия.
        /// Зовётся только на клиенте: у сервера исход считается на месте.
        /// </summary>
        public void SubmitHold(bool held, double stamp)
        {
            if (!IsSpawned || IsServer)
            {
                return;
            }

            SubmitHoldRpc(held, stamp);
        }

        /// <summary>
        /// Сервер принимает намерение и метку. Отправителя берём из RpcParams,
        /// а не из аргумента: иначе клиент нажимал бы за соседа.
        ///
        /// Читерство метки здесь не останавливается технически — рядом
        /// с монитором можно положить настоящий секундомер, и сервер этого
        /// не увидит. Окно нужно не против чита, а против пинга: замер
        /// не должен зависеть от канала.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void SubmitHoldRpc(bool held, double stamp, RpcParams rpcParams = default)
        {
            if (game == null)
            {
                return;
            }

            int playerId = (int)rpcParams.Receive.SenderClientId;
            double arrival = NetworkManager.ServerTime.Time;

            game.ServerApplyHold(playerId, held, ValidateStamp(stamp, arrival, playerId));
        }

        /// <summary>
        /// Метка вне окна зажимается к его границе. Отбрасывать нажатие целиком
        /// нельзя: игрок нажал по-настоящему, и потерянное нажатие стоило бы ему
        /// ошибки подраунда за чужую беду с каналом.
        /// </summary>
        private double ValidateStamp(double stamp, double arrival, int playerId)
        {
            double earliest = arrival - StampMaxBehind;
            double latest = arrival + StampMaxAhead;

            if (stamp < earliest)
            {
                Debug.LogWarning($"{name}: метка игрока {playerId} отстала на " +
                                 $"{(arrival - stamp):F3} с — зажата к границе окна", this);
                return earliest;
            }

            if (stamp > latest)
            {
                Debug.LogWarning($"{name}: метка игрока {playerId} опередила прибытие на " +
                                 $"{(stamp - arrival):F3} с — зажата к границе окна", this);
                return latest;
            }

            return stamp;
        }

        // ========== КЛЕТКИ ==========

        /// <summary>
        /// Сервер сверяет своё состояние с разосланным и досылает разницу.
        /// Опросом, а не толчком из каждой точки изменения: точек много
        /// (ошибка, спуск, вылет, нажатие кнопки), и забытый вызов дал бы
        /// молчаливый рассинхрон вместо ошибки. Запись идёт только при
        /// реальном расхождении, поэтому трафика в покое нет.
        /// </summary>
        private void Update()
        {
            if (!IsSpawned || game == null)
            {
                return;
            }

            if (!IsServer)
            {
                // Табло перерисовывается один раз за кадр, а не на каждой
                // дельте списка: восемь изменившихся клеток дали бы восемь
                // полных перерисовок с построением строк.
                if (cagesDirty)
                {
                    cagesDirty = false;
                    ApplyCages();
                }

                return;
            }

            if (pendingLeavers.Count > 0)
            {
                ApplyPendingLeavers();
            }

            int count = game.ContestantCount;

            while (cages.Count > count)
            {
                cages.RemoveAt(cages.Count - 1);
            }

            for (int i = 0; i < count; i++)
            {
                if (!game.TryGetCageState(i, out CageNetState state))
                {
                    continue;
                }

                if (i >= cages.Count)
                {
                    cages.Add(state);
                    continue;
                }

                if (!cages[i].Equals(state))
                {
                    cages[i] = state;
                }
            }
        }

        private void OnCagesChanged(NetworkListEvent<CageNetState> change)
        {
            if (!IsServer)
            {
                cagesDirty = true;
            }
        }

        private void ApplyCages()
        {
            if (game == null)
            {
                return;
            }

            for (int i = 0; i < cages.Count; i++)
            {
                CageNetState state = cages[i];
                game.ApplyNetworkCage(state.PlayerId, state.Errors, state.Level, state.Lit,
                    state.Alive, state.Faulted, state.DoorsOpen, state.Measured, state.Completed);
            }

            game.ApplyNetworkCagesCommitted();
        }
    }
}
