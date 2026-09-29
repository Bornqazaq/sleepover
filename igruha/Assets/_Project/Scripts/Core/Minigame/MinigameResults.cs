using System.Collections.Generic;
using Igruha.Core.UI;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Итог мини-игры: место каждого игрока, а после начисления — очки за
    /// раунд и сумма катки. Места ставит игра, очки — табло по формуле
    /// <see cref="Session.SessionScoring"/>.
    ///
    /// Очки едут вместе с местами: клиент получает их одним сообщением и не
    /// ждёт, пока доедет репликация ростера, — иначе экран итогов на клиенте
    /// показывал бы прошлую сумму.
    ///
    /// Переиспользуемый контейнер (без аллокаций между раундами).
    /// </summary>
    public sealed class MinigameResults
    {
        public readonly struct PlayerResult
        {
            public int PlayerId { get; }

            /// <summary>Место, начиная с 1. Равные результаты делят место.</summary>
            public int Place { get; }

            /// <summary>Очки за этот раунд. Ноль до начисления.</summary>
            public int Points { get; }

            /// <summary>Сумма катки после начисления. Ноль до начисления.</summary>
            public int Total { get; }
            public RoundResultDetail Detail { get; }

            public PlayerResult(int playerId, int place) : this(playerId, place, 0, 0) { }

            public PlayerResult(int playerId, int place, int points, int total, RoundResultDetail detail = default)
            {
                PlayerId = playerId;
                Place = place;
                Points = points;
                Total = total;
                Detail = detail;
            }

            public PlayerResult WithAward(int points, int total) =>
                new PlayerResult(PlayerId, Place, points, total, Detail);
        }

        private readonly List<PlayerResult> entries = new List<PlayerResult>(8);

        public IReadOnlyList<PlayerResult> Entries => entries;

        /// <summary>
        /// Сколько игроков начинало раунд. По нему считаются очки: ушедший
        /// посреди матча не обесценивает победу оставшихся (IGR-372).
        /// Ноль — не задано, табло возьмёт свой ростер.
        /// </summary>
        public int PlayerCount { get; set; }

        /// <summary>Ключ игры для журнала катки — имя сцены. Пусто — табло запишет «?».</summary>
        public string GameKey { get; set; } = string.Empty;
        public string MetricTitle { get; set; } = "РЕЗУЛЬТАТ";
        public bool AreTeams { get; set; }

        /// <summary>
        /// Очки этого раунда идут в общий счёт катки. Так только в серии
        /// («Полная игра»): одиночная игра с телевизора считает очки только
        /// внутри себя и показывает победителя, а сумму катки не трогает.
        /// </summary>
        public bool CountsTowardSession { get; set; }

        /// <summary>Очки начислены: <see cref="PlayerResult.Points"/> и <see cref="PlayerResult.Total"/> заполнены.</summary>
        public bool Awarded { get; private set; }

        /// <summary>
        /// Убрать участников, оставив состав и ключ игры: их ставит контроллер
        /// до сбора мест, а помощники расстановки (<c>ScoreRanking</c> и
        /// другие) чистят контейнер уже внутри сбора.
        /// </summary>
        public void Clear()
        {
            entries.Clear();
            Awarded = false;
        }

        /// <summary>Сбросить всё, включая состав и ключ игры.</summary>
        public void Reset()
        {
            Clear();
            PlayerCount = 0;
            GameKey = string.Empty;
            MetricTitle = "РЕЗУЛЬТАТ";
            AreTeams = false;
            CountsTowardSession = false;
        }

        public void Add(int playerId, int place) => entries.Add(new PlayerResult(playerId, place));

        /// <summary>Добавить с готовыми очками — так итоги приезжают по сети.</summary>
        public void Add(int playerId, int place, int points, int total, RoundResultDetail detail = default)
        {
            entries.Add(new PlayerResult(playerId, place, points, total, detail));
            Awarded = true;
        }

        public void SetDetail(int index, RoundResultDetail detail)
        {
            var entry = entries[index];
            entries[index] = new PlayerResult(entry.PlayerId, entry.Place, entry.Points, entry.Total, detail);
        }

        public int IndexOf(int playerId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].PlayerId == playerId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Записать очки участнику. Зовёт табло при начислении.</summary>
        public void SetAward(int index, int points, int total)
        {
            if (index < 0 || index >= entries.Count)
            {
                return;
            }

            entries[index] = entries[index].WithAward(points, total);
            Awarded = true;
        }

        public void CopyFrom(MinigameResults other)
        {
            Reset();
            if (other == null)
            {
                return;
            }

            PlayerCount = other.PlayerCount;
            GameKey = other.GameKey;
            MetricTitle = other.MetricTitle;
            AreTeams = other.AreTeams;
            CountsTowardSession = other.CountsTowardSession;
            for (int i = 0; i < other.entries.Count; i++)
            {
                entries.Add(other.entries[i]);
            }

            Awarded = other.Awarded;
        }
    }
}
