using System;
using System.Collections.Generic;
using Igruha.Core.Session;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Строгая очередь ходов: по одному, порядок случаен, но задан один раз
    /// и дальше неизменен. Выбывшие и ушедшие пропускаются.
    ///
    /// Живёт в Core, а не в мини-игре, потому что «ходят по очереди» — не
    /// свойство «Рейса на память», а механика, которая понадобится любой
    /// следующей пошаговой игре пула.
    ///
    /// <b>Почему не <see cref="SpecialRoleHistory"/>.</b> Тот выдаёт роль
    /// случайному из тех, кто ещё не был, и сбрасывает круг, когда побывали все.
    /// Здесь другое: фиксированный порядок, указатель на текущего и пропуск
    /// выбывших. Общего между ними — только слово «по очереди».
    ///
    /// Переиспользуемый контейнер: между ходами не аллоцирует.
    /// </summary>
    public sealed class TurnQueue
    {
        /// <summary>Ходить некому: состав пуст либо все выбыли.</summary>
        public const int NoPlayer = -1;

        private readonly List<int> order = new List<int>(8);
        private readonly HashSet<int> retired = new HashSet<int>();
        private int cursor = -1;

        /// <summary>Ход перешёл. Аргумент — <see cref="NoPlayer"/>, когда ходить стало некому.</summary>
        public event Action<int> TurnChanged;

        /// <summary>Чей сейчас ход.</summary>
        public int CurrentPlayerId { get; private set; } = NoPlayer;

        /// <summary>Сколько игроков ещё ходит.</summary>
        public int ActiveCount => order.Count - retired.Count;

        /// <summary>
        /// Собрать очередь. Порядок задаётся сидом, а не внутренним рандомом:
        /// в сетевой фазе сид объявляет сервер, и порядок сходится у всех
        /// без отдельной репликации списка.
        /// </summary>
        public void Build(IReadOnlyList<SessionPlayer> players, int seed)
        {
            Clear();

            if (players == null || players.Count == 0)
            {
                return;
            }

            for (int i = 0; i < players.Count; i++)
            {
                order.Add(players[i].Id);
            }

            // Тасование Фишера — Йетса от сида: тот же сид даёт тот же порядок.
            var random = new System.Random(seed);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            cursor = -1;
            Advance();
        }

        /// <summary>Передать ход следующему активному игроку по кругу.</summary>
        public void Advance()
        {
            if (order.Count == 0)
            {
                SetCurrent(NoPlayer);
                return;
            }

            for (int offset = 1; offset <= order.Count; offset++)
            {
                int index = (cursor + offset) % order.Count;
                int candidate = order[index];

                if (retired.Contains(candidate))
                {
                    continue;
                }

                cursor = index;
                SetCurrent(candidate);
                return;
            }

            SetCurrent(NoPlayer);
        }

        /// <summary>
        /// Игрок больше не ходит: дошёл до цели либо выбрал лимит попыток.
        ///
        /// Из порядка <b>не удаляется</b>, а помечается неактивным: позиция
        /// в очереди нужна дальше как тайбрейк для тех, до кого ход так и не
        /// дошёл. Если выбывает текущий — ход сразу уходит следующему.
        /// </summary>
        public void Retire(int playerId)
        {
            if (!order.Contains(playerId) || !retired.Add(playerId))
            {
                return;
            }

            if (CurrentPlayerId == playerId)
            {
                Advance();
            }
        }

        /// <summary>
        /// Игрок ушёл из матча. В отличие от <see cref="Retire"/> вычёркивается
        /// совсем — его в матче больше нет.
        ///
        /// Уход того, чей сейчас ход, — самый дорогой случай в пошаговой игре:
        /// не передай ход немедленно, и очередь встанет до конца общего таймера.
        /// </summary>
        public void Remove(int playerId)
        {
            int index = order.IndexOf(playerId);
            if (index < 0)
            {
                return;
            }

            bool wasCurrent = CurrentPlayerId == playerId;

            order.RemoveAt(index);
            retired.Remove(playerId);

            if (index <= cursor)
            {
                cursor--;
            }

            if (wasCurrent)
            {
                Advance();
            }
            else if (ActiveCount == 0)
            {
                SetCurrent(NoPlayer);
            }
        }

        /// <summary>
        /// Место игрока в очереди, с нуля. −1, если его в очереди нет.
        /// Нужен как последний тайбрейк: двое, до кого ход не дошёл вовсе,
        /// обязаны получить разные места.
        /// </summary>
        public int PositionOf(int playerId) => order.IndexOf(playerId);

        /// <summary>Ходит ли игрок ещё.</summary>
        public bool IsActive(int playerId) => order.Contains(playerId) && !retired.Contains(playerId);

        public void Clear()
        {
            order.Clear();
            retired.Clear();
            cursor = -1;
            CurrentPlayerId = NoPlayer;
        }

        private void SetCurrent(int playerId)
        {
            if (CurrentPlayerId == playerId)
            {
                return;
            }

            CurrentPlayerId = playerId;
            TurnChanged?.Invoke(playerId);
        }
    }
}
