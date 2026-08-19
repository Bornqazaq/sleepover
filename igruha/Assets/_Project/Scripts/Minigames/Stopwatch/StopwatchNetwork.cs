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
        private readonly NetworkVariable<StopwatchTaskState> task = new NetworkVariable<StopwatchTaskState>();

        private readonly NetworkVariable<StopwatchStageNetState> stage = new NetworkVariable<StopwatchStageNetState>();

        private readonly NetworkList<CageNetState> cages = new NetworkList<CageNetState>();

        private StopwatchMinigame game;
        private MinigameStageState stageState;

        /// <summary>Список клеток приехал и ещё не отрисован. Только на клиенте.</summary>
        private bool cagesDirty;

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

            if (IsServer)
            {
                // Стадию объявляет тот же, кто её начал: событие приходит уже
                // после того, как момент конца проставлен.
                if (stageState != null)
                {
                    stageState.StageStarted += OnServerStageStarted;
                }

                return;
            }

            // Подключились в середине раунда — догоняем то, что уже решено.
            ApplyTask();
            ApplyStage();
            cagesDirty = true;
        }

        public override void OnNetworkDespawn()
        {
            task.OnValueChanged -= OnTaskChanged;
            stage.OnValueChanged -= OnStageChanged;
            cages.OnListChanged -= OnCagesChanged;

            if (IsServer && stageState != null)
            {
                stageState.StageStarted -= OnServerStageStarted;
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
                    state.Alive, state.Faulted, state.Measured, state.Completed);
            }

            game.ApplyNetworkCagesCommitted();
        }
    }
}
