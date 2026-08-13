using System;
using System.Collections.Generic;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Табло катки: список участников и начисление очков. Реализаций две —
    /// локальная (SessionManager, для тестовых сцен) и сетевая
    /// (Igruha.Networking.NetworkSessionManager, авторитет на сервере).
    /// Потребители работают только через этот интерфейс, поэтому Core
    /// остаётся без ссылок на NGO.
    /// </summary>
    public interface ISessionScoreboard
    {
        event Action ScoresChanged;

        IReadOnlyList<SessionPlayer> Players { get; }

        /// <summary>Участник, которым управляет эта машина (для камеры и HUD).</summary>
        SessionPlayer LocalPlayer { get; }

        /// <summary>
        /// Вправе ли эта машина решать исход: у сетевой реализации — только сервер.
        /// Клиенты получают результат готовым.
        /// </summary>
        bool HasAuthority { get; }

        SessionPlayer FindPlayer(int playerId);

        /// <summary>Начислить очки за мини-игру. Вызывать только при HasAuthority.</summary>
        void ReportResults(Minigame.MinigameResults results);
    }
}
