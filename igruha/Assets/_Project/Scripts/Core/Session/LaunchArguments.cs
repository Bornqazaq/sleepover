using System;
using UnityEngine;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Аргументы запуска билда — один разбор на весь проект.
    ///
    /// Сетевой слой берёт отсюда роль и адрес (см. <c>NetworkLaunchArguments</c>),
    /// автопрогон — состав, очередь мини-игр и признак болванки. Два сканера
    /// одних и тех же аргументов рано или поздно разъезжаются, поэтому разбор
    /// живёт в Core: до <c>Igruha.Networking</c> отсюда не дотянуться, а нужен
    /// он и хабу, и мини-играм.
    ///
    /// <b>Зачем автопрогон.</b> Живьём восьмерых собрать можно раз в неделю,
    /// а проверять сеть надо каждый день. Восемь процессов на одной машине
    /// живую катку не заменяют — там разный пинг, разные машины и разные
    /// платформы, — но ловят всё, что ломается от самого числа участников:
    /// ротацию ролей, деление очков, раскол, дисконнекты.
    ///
    /// Значения читаются один раз и кэшируются: <c>GetCommandLineArgs</c>
    /// каждый раз выделяет новый массив, а спрашивают эти флаги в том числе
    /// из игрового цикла.
    /// </summary>
    public static class LaunchArguments
    {
        /// <summary>Персонажем этой машины управляет болванка автопрогона.</summary>
        public const string BotFlag = "--bot";

        /// <summary>
        /// Очередь мини-игр, которые хост запустит сам, минуя якорь:
        /// <c>--autostart Exam</c> или <c>--autostart Exam,BelieveOrNot</c>.
        /// Имена — как имена сцен в Build Settings.
        /// </summary>
        public const string AutostartOption = "--autostart";

        /// <summary>Сколько участников дождаться перед автозапуском: <c>--wait-players 8</c>.</summary>
        public const string WaitPlayersOption = "--wait-players";

        private static string[] autostart;
        private static bool autostartParsed;
        private static int waitPlayers = -1;
        private static int botEnabled = -1;

        /// <summary>Этой машиной играет болванка: ходит сама, вопрос берёт из заготовок.</summary>
        public static bool BotEnabled
        {
            get
            {
                if (botEnabled < 0)
                {
                    botEnabled = HasFlag(BotFlag) ? 1 : 0;
                }

                return botEnabled == 1;
            }
        }

        /// <summary>
        /// Сколько участников ждать перед автозапуском. Ноль — аргумента нет,
        /// значит автозапуска ждать нечего и решает вызывающий.
        /// </summary>
        public static int WaitPlayers
        {
            get
            {
                if (waitPlayers < 0)
                {
                    waitPlayers = TryGetValue(WaitPlayersOption, out string raw) && int.TryParse(raw, out int parsed)
                        ? Mathf.Clamp(parsed, 1, 8)
                        : 0;
                }

                return waitPlayers;
            }
        }

        /// <summary>
        /// Очередь автозапуска. Ложь — аргумента нет, игру выбирают руками
        /// у якоря, как в обычной катке.
        /// </summary>
        public static bool TryGetAutostart(out string[] minigames)
        {
            if (!autostartParsed)
            {
                autostartParsed = true;
                autostart = TryGetValue(AutostartOption, out string raw)
                    ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    : null;

                if (autostart != null)
                {
                    for (int i = 0; i < autostart.Length; i++)
                    {
                        autostart[i] = autostart[i].Trim();
                    }
                }
            }

            minigames = autostart;
            return minigames != null && minigames.Length > 0;
        }

        /// <summary>Флаг без значения присутствует в командной строке.</summary>
        public static bool HasFlag(string flag)
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
        public static bool TryGetValue(string option, out string value)
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
                    Debug.LogWarning($"⚠️ Аргумент {option} без значения — игнорирую");
                    return false;
                }

                value = candidate.Trim();
                return true;
            }

            return false;
        }
    }
}
