namespace Igruha.Core.Session
{
    /// <summary>
    /// Одна строка журнала катки: что получил участник за один раунд.
    /// Журнал — доказательство, что каждая сыгранная игра учтена: по нему
    /// считаются число сыгранных игр и победы, по нему же разбирается спор
    /// «а за третью игру мне начислили?».
    /// </summary>
    public readonly struct SessionRoundRecord
    {
        /// <summary>Номер раунда в катке, с единицы.</summary>
        public int Round { get; }

        /// <summary>Ключ игры — имя сцены.</summary>
        public string GameKey { get; }

        public int PlayerId { get; }

        public int Place { get; }

        /// <summary>Очки, начисленные за этот раунд.</summary>
        public int Points { get; }

        public SessionRoundRecord(int round, string gameKey, int playerId, int place, int points)
        {
            Round = round;
            GameKey = gameKey ?? string.Empty;
            PlayerId = playerId;
            Place = place;
            Points = points;
        }
    }
}
