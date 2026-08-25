using Igruha.Core.Session;

namespace Igruha.Networking
{
    /// <summary>
    /// Аргументы запуска билда, относящиеся к сети.
    ///
    /// Нужны потому, что адрес хоста нельзя зашить в сцену: у каждой катки он
    /// свой (домашняя сеть, Tailscale, чужая квартира), а пересобирать билд
    /// ради строчки адреса — не вариант. Сцена задаёт значение по умолчанию,
    /// аргумент его перекрывает.
    ///
    /// Сам разбор командной строки живёт в <see cref="LaunchArguments"/>: им
    /// пользуются и сеть, и хаб, и мини-игры, а до <c>Igruha.Networking</c>
    /// из Core не дотянуться. Здесь остались только сетевые имена аргументов —
    /// два разных сканера одних и тех же аргументов рано или поздно
    /// разъезжаются.
    /// </summary>
    public static class NetworkLaunchArguments
    {
        /// <summary>Поднять инстанс клиентом, а не хостом.</summary>
        public const string ClientFlag = "--client";

        /// <summary>Куда подключаться клиенту: <c>--host 100.64.12.34</c>.</summary>
        public const string HostOption = "--host";

        /// <summary>Порт хоста, если он не дефолтный: <c>--port 7777</c>.</summary>
        public const string PortOption = "--port";

        /// <summary>Запущено с флагом клиента.</summary>
        public static bool HasClientFlag() => LaunchArguments.HasFlag(ClientFlag);

        /// <summary>Адрес хоста из аргументов. Ложь — аргумента нет, остаётся значение из сцены.</summary>
        public static bool TryGetHostAddress(out string address) =>
            LaunchArguments.TryGetValue(HostOption, out address);

        /// <summary>Порт из аргументов. Ложь — аргумента нет либо он не число.</summary>
        public static bool TryGetPort(out ushort port)
        {
            port = 0;
            return LaunchArguments.TryGetValue(PortOption, out string raw)
                   && ushort.TryParse(raw, out port)
                   && port != 0;
        }
    }
}
