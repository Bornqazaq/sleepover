namespace Igruha.Core.Session
{
    /// <summary>
    /// Точка доступа к текущему табло катки. Сетевая реализация всегда важнее
    /// локальной: локальная остаётся в сценах как заглушка для тестов, и без
    /// приоритета порядок Awake/OnNetworkSpawn решал бы, кто победит.
    /// </summary>
    public static class SessionScoreboard
    {
        private static ISessionScoreboard local;
        private static ISessionScoreboard networked;

        public static ISessionScoreboard Current => networked ?? local;

        /// <summary>Идёт сетевая катка: игроков спавнит сервер, а не локальный спавнер.</summary>
        public static bool IsNetworked => networked != null;

        public static void RegisterLocal(ISessionScoreboard scoreboard) => local = scoreboard;

        public static void RegisterNetworked(ISessionScoreboard scoreboard) => networked = scoreboard;

        public static void Unregister(ISessionScoreboard scoreboard)
        {
            if (networked == scoreboard)
            {
                networked = null;
            }

            if (local == scoreboard)
            {
                local = null;
            }
        }
    }
}
