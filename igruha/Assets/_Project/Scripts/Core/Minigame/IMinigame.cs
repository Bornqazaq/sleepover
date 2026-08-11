using System;
using System.Collections.Generic;
using Igruha.Core.Session;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Единый интерфейс мини-игры (CLAUDE.md 3.5, ROADMAP 4.9).
    /// Процесс игры — внутреннее дело реализации; снаружи только старт,
    /// принудительное завершение и отчёт результатов (пока локальное событие,
    /// позже — подтверждение на сервере).
    /// </summary>
    public interface IMinigame
    {
        MinigameDefinition Definition { get; }

        /// <summary>Итоги готовы. Подписывается SessionManager (начисление очков).</summary>
        event Action<MinigameResults> ResultsReported;

        void StartMinigame(IReadOnlyList<SessionPlayer> players);

        /// <summary>Принудительное завершение (таймер, дисконнекты и т.п.).</summary>
        void EndMinigame();
    }
}
