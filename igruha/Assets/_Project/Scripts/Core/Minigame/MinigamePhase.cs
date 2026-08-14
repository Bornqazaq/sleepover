namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Фаза мини-игры. В сетевой катке её объявляет сервер, остальные машины
    /// только применяют — поэтому фаза, а не набор булевых флагов.
    /// </summary>
    public enum MinigamePhase
    {
        /// <summary>Сцена загружена, игроки ещё не получены.</summary>
        Idle = 0,
        /// <summary>Обучающая заставка, управление выключено.</summary>
        Tutorial = 1,
        /// <summary>Идёт раунд, таймер тикает.</summary>
        Round = 2,
        /// <summary>Раунд кончился, показаны места.</summary>
        Results = 3
    }
}
