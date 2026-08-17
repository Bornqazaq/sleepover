using System;
using UnityEngine;

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
    /// Разбор командной строки собран в одном месте: им пользуются и
    /// <see cref="NetworkRoleResolver"/> (роль), и <see cref="AppNetworkManager"/>
    /// (адрес), а два разных сканера одних и тех же аргументов рано или поздно
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
        public static bool HasClientFlag() => HasFlag(ClientFlag);

        /// <summary>Адрес хоста из аргументов. Ложь — аргумента нет, остаётся значение из сцены.</summary>
        public static bool TryGetHostAddress(out string address) => TryGetValue(HostOption, out address);

        /// <summary>Порт из аргументов. Ложь — аргумента нет либо он не число.</summary>
        public static bool TryGetPort(out ushort port)
        {
            port = 0;
            return TryGetValue(PortOption, out string raw) && ushort.TryParse(raw, out port) && port != 0;
        }

        private static bool HasFlag(string flag)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Значение опции вида <c>--ключ значение</c>. Пустое значение и
        /// значение, начинающееся с дефиса, считаем опечаткой: молча взять
        /// следующий флаг как адрес — худший из возможных исходов, клиент
        /// будет стучаться в никуда и никто не поймёт почему.
        /// </summary>
        private static bool TryGetValue(string option, out string value)
        {
            value = null;
            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (!string.Equals(arguments[i], option, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string candidate = arguments[i + 1];
                if (string.IsNullOrWhiteSpace(candidate) || candidate.StartsWith("-", StringComparison.Ordinal))
                {
                    Debug.LogWarning($"⚠️ Аргумент {option} без значения — игнорирую, останется значение из сцены");
                    return false;
                }

                value = candidate.Trim();
                return true;
            }

            return false;
        }
    }
}
