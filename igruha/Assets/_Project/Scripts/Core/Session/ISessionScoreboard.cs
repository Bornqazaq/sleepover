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

        /// <summary>Был ли игрок в этой особой роли с последнего сброса истории.</summary>
        bool HasPlayedSpecialRole(int playerId, string roleKey);

        /// <summary>Отметить, что игрок побывал в роли. Вызывать только при HasAuthority.</summary>
        void MarkSpecialRole(int playerId, string roleKey);

        /// <summary>
        /// Выбрать следующего на особую роль (Водящий, охотник, оператор):
        /// случайный из тех, кто ещё не был, со сбросом истории, когда побывали все.
        /// Рандом обязан быть серверным, поэтому метод работает только при
        /// HasAuthority; иначе возвращает SpecialRoleHistory.NoPlayer.
        /// </summary>
        int PickSpecialRole(string roleKey);
    }
}
