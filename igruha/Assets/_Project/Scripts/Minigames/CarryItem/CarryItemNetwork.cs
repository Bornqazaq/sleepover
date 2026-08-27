using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Session;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Строка состава: кто играет и за какую команду.
    ///
    /// Составы объявляются <b>готовым списком</b>, а не правилом деления. Правило
    /// (<see cref="TeamAssignment"/>) детерминированно, но опирается на порядок
    /// в ростере, а ростер у клиента собирается своим путём и в свой момент:
    /// повторив деление у себя, он получил бы те же команды только при точно
    /// совпавшем порядке. Готовый список сходится всегда — и переживает уход
    /// игрока, после которого пересчёт по индексу дал бы другие команды.
    /// </summary>
    public struct CarryItemMemberNetState : INetworkSerializable, IEquatable<CarryItemMemberNetState>
    {
        public int PlayerId;

        /// <summary>Команда, <see cref="TeamSide"/> байтом.</summary>
        public byte Team;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Team);
        }

        public bool Equals(CarryItemMemberNetState other) =>
            PlayerId == other.PlayerId && Team == other.Team;
    }

    /// <summary>
    /// Сетевая половина «Переноски предмета»: вешается на тот же объект, что и
    /// <see cref="CarryItemMinigame"/>. Правила остаются обычным MonoBehaviour и
    /// работают без этого компонента, когда сцену открывают напрямую, — образец
    /// взят у <c>MemoryRunNetwork</c> и <c>BelieveOrNotNetwork</c>.
    ///
    /// Фазу, время общего таймера и итоговые места везёт
    /// <c>NetworkMinigameBridge</c> рядом на том же объекте.
    ///
    /// Здесь ровно два канала:
    ///
    /// — <b>состав</b> — один раз на раунд, плюс правка при уходе игрока;
    /// — <b>счёт</b> — оба бака одной структурой <see cref="CarryItemState"/>.
    ///
    /// Всё остальное состояние живёт там, где ему место, и сюда не стягивается:
    /// уровень в бутыли — на самой бутыли, занятые ручки — на
    /// <c>MultiCarryObject</c>, фаза ловушек — на общих часах вообще без
    /// трафика. Стягивать это в один компонент означало бы гонять по сети то,
    /// что и так у всех одинаково.
    /// </summary>
    public sealed class CarryItemNetwork : NetworkBehaviour
    {
        /// <summary>Счёт обеих команд. Пишет сервер, читают все.</summary>
        private readonly NetworkVariable<CarryItemState> state = new NetworkVariable<CarryItemState>();

        /// <summary>Кто за кого играет. Объявляется составом, а не правилом деления.</summary>
        private readonly NetworkList<CarryItemMemberNetState> roster =
            new NetworkList<CarryItemMemberNetState>();

        /// <summary>
        /// Буфер под разбор состава на клиенте. <c>NetworkList</c> не список в
        /// смысле <c>IReadOnlyList</c>, поэтому строки перекладываются сюда —
        /// один раз на приезд, а не каждый кадр.
        /// </summary>
        private readonly List<CarryItemMemberNetState> rosterBuffer = new List<CarryItemMemberNetState>(8);

        private CarryItemMinigame game;

        private bool stateDirty;
        private bool rosterDirty;

        /// <summary>Идёт сетевая катка и эта половина живая.</summary>
        public bool IsActive => IsSpawned;

        private void Awake()
        {
            game = GetComponent<CarryItemMinigame>();
            if (game == null)
            {
                Debug.LogError($"{name}: CarryItemNetwork не нашёл CarryItemMinigame на своём объекте", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            state.OnValueChanged += OnStateChanged;
            roster.OnListChanged += OnRosterChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }

            // Состояние могло приехать до подписки — разбираем то, что уже есть.
            stateDirty = true;
            rosterDirty = true;
        }

        public override void OnNetworkDespawn()
        {
            state.OnValueChanged -= OnStateChanged;
            roster.OnListChanged -= OnRosterChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            base.OnNetworkDespawn();
        }

        // ========== СЕРВЕР ПУБЛИКУЕТ ==========

        /// <summary>
        /// Объявить состав. Зовётся на старте раунда и ещё раз при уходе
        /// игрока: строка ушедшего снимается, а команды остальных не
        /// пересчитываются — иначе половина лобби меняла бы сторону посреди
        /// ходки.
        /// </summary>
        public void PublishRoster(IReadOnlyList<CarryItemMemberNetState> members)
        {
            if (!IsSpawned || !IsServer || members == null)
            {
                return;
            }

            roster.Clear();
            for (int i = 0; i < members.Count; i++)
            {
                roster.Add(members[i]);
            }
        }

        /// <summary>
        /// Объявить счёт. Зовётся на каждое изменение бака — то есть в среднем
        /// раз в такт слива, а не каждый кадр: <see cref="CarryItemState"/>
        /// сравнивается целиком, и одинаковое значение в сеть не уходит.
        /// </summary>
        public void PublishState(in CarryItemState value)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            state.Value = value;
        }

        // ========== НАМЕРЕНИЕ: УДЕРЖАНИЕ У ШТАБЕЛЯ ==========

        /// <summary>
        /// Клиент держит E у штабеля своей команды. Отсчёт полутора секунд
        /// ведёт сервер: тара — это ходка, а ходка — счёт.
        ///
        /// Общего механизма для удержания в Core пока нет — <c>PlayerInteractor</c>
        /// разбирает удержание локально, а серверу его пересылает каждая игра
        /// сама (прецедент — <c>CageButton</c> «Секундомера»). Обобщить это в
        /// Core стоит отдельной задачей: сейчас таких игр уже две.
        ///
        /// <c>true</c> — намерение ушло, этой машине решать нечего.
        /// </summary>
        public bool SubmitStackHold(TeamSide team, bool held)
        {
            if (!IsSpawned || IsServer)
            {
                return false;
            }

            StackHoldRpc((byte)team, held);
            return true;
        }

        /// <summary>
        /// Отправителя берём из <c>RpcParams</c>, а не из аргумента: иначе одним
        /// сообщением можно было бы взять тару за чужого. Свой ли это штабель и
        /// дотягивается ли игрок — проверяет сам штабель в <c>CanInteract</c>.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void StackHoldRpc(byte team, bool held, RpcParams rpcParams = default)
        {
            game?.ApplyStackHold((int)rpcParams.Receive.SenderClientId, (TeamSide)team, held);
        }

        // ========== ПРИЁМ НА КЛИЕНТЕ ==========

        private void OnStateChanged(CarryItemState previous, CarryItemState current) => stateDirty = true;

        private void OnRosterChanged(NetworkListEvent<CarryItemMemberNetState> change) => rosterDirty = true;

        private void LateUpdate()
        {
            if (!IsSpawned || IsServer || game == null)
            {
                return;
            }

            // Состав обязателен и идёт первым: по нему клиент решает, кто свой,
            // — а от этого зависит и фильтр ручек, и своя половина полосы
            // прогресса. Разобрать счёт раньше состава значит покрасить чужую
            // полосу в свои цвета.
            if (rosterDirty)
            {
                rosterDirty = false;

                rosterBuffer.Clear();
                for (int i = 0; i < roster.Count; i++)
                {
                    rosterBuffer.Add(roster[i]);
                }

                game.ApplyNetworkRoster(rosterBuffer);
            }

            if (stateDirty)
            {
                stateDirty = false;
                game.ApplyNetworkState(state.Value);
            }
        }

        // ========== ДИСКОННЕКТ ==========

        /// <summary>
        /// Участник ушёл. Решение принимает контроллер: уникальных ролей в игре
        /// нет, но команда из одного человека — легальный состав, и её уход
        /// заканчивает раунд досрочно (спека 10.1).
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
