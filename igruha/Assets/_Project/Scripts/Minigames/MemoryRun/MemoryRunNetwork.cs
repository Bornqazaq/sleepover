using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>
    /// Чей сейчас ход и до какого момента. Одной структурой, потому что
    /// назначаются они одним решением сервера: приехавший раньше новый дедлайн
    /// под старым игроком означал бы чужой таймер над головой.
    ///
    /// <b>Время — два момента, а не остаток.</b> <see cref="ArmTime"/> — когда
    /// кончится двухсекундное объявление «твой ход», <see cref="Deadline"/> —
    /// когда ход оборвётся. Оба по <see cref="NetworkClock"/>, поэтому
    /// объявляются один раз на ход, а не досылаются каждый кадр. Тот же приём,
    /// что у «Экзамена», «Порядка банок» и «Верю / не верю».
    ///
    /// <b>Маршрута здесь нет и быть не может.</b> Номер шага, на котором стоит
    /// идущий, тоже не едет: его не показывают никому (спека 9.4), а вывести
    /// из него можно ровно то, ради чего игра существует.
    /// </summary>
    public struct MemoryRunTurnNetState : INetworkSerializable, IEquatable<MemoryRunTurnNetState>
    {
        /// <summary>Кто идёт. <see cref="TurnQueue.NoPlayer"/> — ходить некому.</summary>
        public int WalkerId;

        /// <summary>
        /// Порядковый номер хода с начала раунда. Ходить в игре может и один
        /// и тот же участник подряд, и без счётчика два таких хода различались
        /// бы только дедлайнами — а приёмке на восьми машинах нужно видеть,
        /// что очередь провернулась целиком.
        /// </summary>
        public int TurnNumber;

        /// <summary>Момент, когда кончится объявление и пойдёт отсчёт хода.</summary>
        public double ArmTime;

        /// <summary>Момент, когда ход оборвётся по таймеру.</summary>
        public double Deadline;

        /// <summary>Раунд ещё не начался либо ходить уже некому.</summary>
        public static MemoryRunTurnNetState Idle => new MemoryRunTurnNetState
        {
            WalkerId = TurnQueue.NoPlayer
        };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref WalkerId);
            serializer.SerializeValue(ref TurnNumber);
            serializer.SerializeValue(ref ArmTime);
            serializer.SerializeValue(ref Deadline);
        }

        public bool Equals(MemoryRunTurnNetState other) =>
            WalkerId == other.WalkerId &&
            TurnNumber == other.TurnNumber &&
            ArmTime.Equals(other.ArmTime) &&
            Deadline.Equals(other.Deadline);
    }

    /// <summary>
    /// Строка участника в сети: дальний шаг, время рекорда, смерти, прибытие.
    /// Ровно те четыре числа, из которых <see cref="MemoryRunRanking"/> строит
    /// места, — чтобы у клиента получалась та же таблица, что у сервера.
    ///
    /// <b>Дальний шаг — это счётчик, а не полоса.</b> «Дошёл до пятого» видели
    /// все семеро зрителей, на этом игра и построена; какая из трёх плит на
    /// пятом шаге безопасна, отсюда не следует никак.
    /// </summary>
    public struct MemoryRunProgressNetState : INetworkSerializable, IEquatable<MemoryRunProgressNetState>
    {
        public int PlayerId;
        public byte BestStep;
        public byte Deaths;
        public byte ArrivalOrder;
        public bool Finished;
        public double BestStepTime;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref BestStep);
            serializer.SerializeValue(ref Deaths);
            serializer.SerializeValue(ref ArrivalOrder);
            serializer.SerializeValue(ref Finished);
            serializer.SerializeValue(ref BestStepTime);
        }

        public bool Equals(MemoryRunProgressNetState other) =>
            PlayerId == other.PlayerId &&
            BestStep == other.BestStep &&
            Deaths == other.Deaths &&
            ArrivalOrder == other.ArrivalOrder &&
            Finished == other.Finished &&
            BestStepTime.Equals(other.BestStepTime);
    }

    /// <summary>
    /// Сетевая половина «Рейса на память»: вешается на тот же объект, что и
    /// <see cref="MemoryRunMinigame"/>. Правила остаются обычным
    /// MonoBehaviour и работают без этого компонента, когда сцену открывают
    /// напрямую, — образец взят у <c>BelieveOrNotNetwork</c> и <c>ExamNetwork</c>.
    ///
    /// Фазу, время общего таймера и итоговые места везёт
    /// <c>NetworkMinigameBridge</c> рядом на том же объекте.
    ///
    /// <b>🔴 Чего здесь нет и не будет — маршрута.</b> Ни целиком, ни по одному
    /// шагу, ни «зашифрованным», ни внутри структуры, где он «всё равно не
    /// читается». Сид генерации — тот же секрет, только в профиль: по нему
    /// маршрут восстанавливается целиком, поэтому он тоже остаётся приватным
    /// полем сервера и не едет никуда.
    ///
    /// Порядок очереди объявляется <b>готовым списком</b>, а не сидом тасования.
    /// Так решено намеренно: сид сошёлся бы у клиента в тот же порядок только
    /// при точно совпадающем ростере, а список сходится всегда — и на сервере
    /// не остаётся ни одного сида, который что-то значит.
    ///
    /// Три канала и один <c>Rpc</c>, больше ничего. Перечень снимается
    /// рефлексией и проверяется <c>MemoryRunTrafficAudit</c> — это и есть
    /// приёмка секрета (спека 10.1).
    /// </summary>
    public sealed class MemoryRunNetwork : NetworkBehaviour
    {
        /// <summary>Порядок ходов. Объявлен всем: очередь — общеизвестна.</summary>
        private readonly NetworkList<int> turnOrder = new NetworkList<int>();

        /// <summary>Чей ход и до какого момента.</summary>
        private readonly NetworkVariable<MemoryRunTurnNetState> turn =
            new NetworkVariable<MemoryRunTurnNetState>(MemoryRunTurnNetState.Idle);

        /// <summary>Прогресс всех участников — из него считается итоговая таблица.</summary>
        private readonly NetworkList<MemoryRunProgressNetState> progress =
            new NetworkList<MemoryRunProgressNetState>();

        /// <summary>
        /// Буфер под разбор очереди на клиенте. <c>NetworkList</c> не список
        /// в смысле <c>IReadOnlyList</c>, поэтому порядок перекладывается сюда —
        /// один раз на приезд, а не каждый кадр.
        /// </summary>
        private readonly List<int> orderBuffer = new List<int>(8);

        private MemoryRunMinigame game;

        private bool turnDirty;
        private bool orderDirty;
        private bool progressDirty;

        /// <summary>
        /// Состав, под который состояние уже разобрано. Порядок спавна этого
        /// объекта и старта мини-игры ничем не связан: очередь вполне может
        /// приехать раньше, чем контроллер получит ростер, — и тогда её
        /// пришлось бы разбирать заново, иначе у клиента не было бы ни строки
        /// «чей ход», ни метки над идущим.
        /// </summary>
        private int appliedRoster = -1;

        /// <summary>Идёт сетевая катка и эта половина живая.</summary>
        public bool IsActive => IsSpawned;

        private void Awake()
        {
            game = GetComponent<MemoryRunMinigame>();
            if (game == null)
            {
                Debug.LogError($"{name}: MemoryRunNetwork не нашёл MemoryRunMinigame на своём объекте", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            turn.OnValueChanged += OnTurnChanged;
            turnOrder.OnListChanged += OnOrderChanged;
            progress.OnListChanged += OnProgressChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }

            // Состояние могло приехать до подписки — разбираем то, что уже есть.
            turnDirty = true;
            orderDirty = true;
            progressDirty = true;
        }

        public override void OnNetworkDespawn()
        {
            turn.OnValueChanged -= OnTurnChanged;
            turnOrder.OnListChanged -= OnOrderChanged;
            progress.OnListChanged -= OnProgressChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            base.OnNetworkDespawn();
        }

        // ========== СЕРВЕР ПУБЛИКУЕТ ==========

        /// <summary>
        /// Объявить порядок ходов. Один раз на раунд, в начале: спека прямо
        /// требует, чтобы порядок был зафиксирован и дальше не менялся.
        /// </summary>
        public void PublishTurnOrder(IReadOnlyList<int> order)
        {
            if (!IsSpawned || !IsServer || order == null)
            {
                return;
            }

            turnOrder.Clear();
            for (int i = 0; i < order.Count; i++)
            {
                turnOrder.Add(order[i]);
            }
        }

        /// <summary>
        /// Объявить ход: кто идёт и два момента общих часов. Зовётся один раз
        /// на ход, а не каждый кадр, — в этом весь смысл моментов вместо остатка.
        /// </summary>
        public void PublishTurn(int walkerId, int turnNumber, double armTime, double deadline)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            turn.Value = new MemoryRunTurnNetState
            {
                WalkerId = walkerId,
                TurnNumber = turnNumber,
                ArmTime = armTime,
                Deadline = deadline
            };
        }

        /// <summary>
        /// Разослать прогресс участников.
        ///
        /// Список правится поэлементно, а не пересобирается: <c>Clear</c> плюс
        /// восемь <c>Add</c> — это шестнадцать событий списка на каждую смерть,
        /// тогда как меняется в ней одна строка.
        /// </summary>
        public void PublishProgress(IReadOnlyList<MemoryRunProgress> records)
        {
            if (!IsSpawned || !IsServer || records == null)
            {
                return;
            }

            if (progress.Count != records.Count)
            {
                progress.Clear();
                for (int i = 0; i < records.Count; i++)
                {
                    progress.Add(ToNetState(records[i]));
                }

                return;
            }

            for (int i = 0; i < records.Count; i++)
            {
                MemoryRunProgressNetState next = ToNetState(records[i]);
                if (!progress[i].Equals(next))
                {
                    progress[i] = next;
                }
            }
        }

        /// <summary>
        /// Плита сдетонировала. <b>Это событие, а не состояние</b>: взрыв
        /// слышали и видели все восемь человек в зале, и уходит наружу ровно
        /// то, что они видели, — место взрыва.
        ///
        /// Обратного события «плита оказалась безопасной» не существует и
        /// существовать не может: это и был бы маршрут, выданный по одному
        /// шагу. Что шаг пройден, зрители видят сами — по отсутствию взрыва.
        /// </summary>
        public void AnnounceDetonation(Vector3 center)
        {
            if (IsSpawned && IsServer)
            {
                DetonationRpc(center);
            }
        }

        [Rpc(SendTo.NotServer)]
        private void DetonationRpc(Vector3 center) => game?.ApplyDetonation(center);

        private static MemoryRunProgressNetState ToNetState(in MemoryRunProgress record) =>
            new MemoryRunProgressNetState
            {
                PlayerId = record.PlayerId,
                BestStep = (byte)Mathf.Clamp(record.BestStep, 0, byte.MaxValue),
                Deaths = (byte)Mathf.Clamp(record.Deaths, 0, byte.MaxValue),
                ArrivalOrder = (byte)Mathf.Clamp(record.ArrivalOrder, 0, byte.MaxValue),
                Finished = record.Finished,
                BestStepTime = record.BestStepTime
            };

        private static MemoryRunProgress ToProgress(in MemoryRunProgressNetState state) =>
            new MemoryRunProgress
            {
                PlayerId = state.PlayerId,
                BestStep = state.BestStep,
                BestStepTime = state.BestStepTime,
                Deaths = state.Deaths,
                Finished = state.Finished,
                ArrivalOrder = state.ArrivalOrder
            };

        // ========== ПРИЁМ НА КЛИЕНТЕ ==========

        private void OnTurnChanged(MemoryRunTurnNetState previous, MemoryRunTurnNetState current) => turnDirty = true;

        private void OnOrderChanged(NetworkListEvent<int> change) => orderDirty = true;

        private void OnProgressChanged(NetworkListEvent<MemoryRunProgressNetState> change) => progressDirty = true;

        private void LateUpdate()
        {
            if (!IsSpawned || IsServer || game == null)
            {
                return;
            }

            // Ростер мог собраться уже после того, как состояние разобрали:
            // тогда разбираем заново, иначе клиент досидит раунд без очереди
            // в строке состояния и без метки над идущим.
            if (game.RosterCount != appliedRoster)
            {
                appliedRoster = game.RosterCount;
                turnDirty = true;
                orderDirty = true;
                progressDirty = true;
            }

            // Порядок обязателен: строка состояния читает очередь, и разобрать
            // ход раньше очереди значит показать «Дальше:» по пустому списку.
            if (orderDirty)
            {
                orderDirty = false;

                orderBuffer.Clear();
                for (int i = 0; i < turnOrder.Count; i++)
                {
                    orderBuffer.Add(turnOrder[i]);
                }

                game.ApplyNetworkTurnOrder(orderBuffer);
            }

            if (progressDirty)
            {
                progressDirty = false;
                for (int i = 0; i < progress.Count; i++)
                {
                    game.ApplyNetworkProgress(ToProgress(progress[i]));
                }
            }

            if (turnDirty)
            {
                turnDirty = false;
                MemoryRunTurnNetState state = turn.Value;
                game.ApplyNetworkTurn(state.WalkerId, state.ArmTime, state.Deadline);
            }
        }

        // ========== ДИСКОННЕКТ ==========

        /// <summary>
        /// Участник ушёл. Решение принимает контроллер: в пошаговой игре уход
        /// того, чей сейчас ход, дороже всех прочих — очередь встанет до конца
        /// общего таймера, если ход не передать немедленно (спека 10.2).
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
