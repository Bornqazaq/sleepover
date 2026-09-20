using System;
using System.Collections.Generic;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Табло катки: список участников, начисление очков и журнал раундов.
    /// Реализаций две — локальная (SessionManager, для тестовых сцен) и
    /// сетевая (Igruha.Networking.NetworkSessionManager, авторитет на
    /// сервере). Потребители работают только через этот интерфейс, поэтому
    /// Core остаётся без ссылок на NGO.
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

        /// <summary>
        /// Начислить очки за мини-игру по формуле <see cref="SessionScoring"/>
        /// и записать раунд в журнал. Вызывать только при HasAuthority.
        /// Заполняет очки и суммы в самих итогах — их дальше показывают и
        /// рассылают клиентам.
        /// </summary>
        void ReportResults(Minigame.MinigameResults results);

        /// <summary>Журнал катки: по строке на участника за каждую засчитанную игру.</summary>
        IReadOnlyList<SessionRoundRecord> History { get; }

        /// <summary>Сколько игр катки уже засчитано.</summary>
        int RoundsPlayed { get; }

        /// <summary>
        /// Чемпионы последней доигранной серии. Обычно один; несколько —
        /// при равенстве сумм (тай-брейк ещё не собран, IGR-81). Пусто —
        /// серия ещё не доиграна или после неё счёт сброшен.
        /// </summary>
        IReadOnlyList<int> Champions { get; }

        bool IsChampion(int playerId);

        /// <summary>
        /// Серия доиграна: зафиксировать чемпионов по текущим суммам.
        /// Вызывать только при HasAuthority. Держатся до следующего сброса
        /// счёта, то есть до старта новой серии.
        /// </summary>
        void CompleteSeries();

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
