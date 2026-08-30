using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Items;
using Igruha.Minigames.CarryItem;
using Igruha.Networking;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Перечень того, что «Переноска предмета» реально гоняет по сети — снятый
    /// рефлексией по живым типам, а не глазами по коду.
    ///
    /// <b>Секрета в этой игре нет.</b> Соперник видит ровно то же, что своя
    /// команда: и уровень чужой бутыли, и чужой крен — это часть игры
    /// (спека 8.2). Поэтому проверяется здесь не утечка, а <b>лишнее</b>:
    /// каждое реплицируемое поле обязано быть внесено в перечень с
    /// обоснованием. Приём тот же, что закрывал приёмку «Рейса на память»
    /// (IGR-425) и «Верю / не верю».
    ///
    /// Зачем это нужно игре без секрета. У «Переноски» четыре канала на трёх
    /// разных объектах, и добавить в любой из них поле «заодно» ничего не
    /// стоит: структура состояния бутыли уходит на каждый тик слива, а
    /// состояние ручек — на каждый захват. Лишнее число в них не сломает игру
    /// и не покажется в логе — оно просто будет ехать по сети всю катку.
    /// Белый список валится на любом новом поле, пока его не разобрали.
    ///
    /// Запуск — пункт меню <c>Igruha/Переноска предмета/Перечень реплицируемых полей</c>
    /// либо <see cref="Run"/> из любого скрипта. Повторять после каждой правки
    /// сети — одна команда.
    /// </summary>
    public static class CarryItemTrafficAudit
    {
        /// <summary>
        /// Что вправе ехать по сети и зачем. Путь → обоснование.
        ///
        /// Белый список, а не чёрный: чёрный ловил бы только те названия,
        /// которые кто-то догадался в него внести, и молчал бы на поле,
        /// добавленное завтра.
        /// </summary>
        private static readonly Dictionary<string, string> Allowed = new Dictionary<string, string>
        {
            // ---- Состояние раунда: CarryItemNetwork ----
            ["CarryItemNetwork.state"] =
                "счёт обеих команд одной структурой — это и есть исход раунда",
            ["CarryItemNetwork.state.TeamA"] = "бак команды A",
            ["CarryItemNetwork.state.TeamB"] = "бак команды B",
            ["CarryItemNetwork.roster"] =
                "состав команд: готовым списком, а не правилом деления — у клиента свой порядок ростера",
            ["CarryItemNetwork.roster.PlayerId"] = "кто именно, номер участника сессии",
            ["CarryItemNetwork.roster.Team"] = "за какую команду играет",
            ["CarryItemNetwork.roster.Left"] = "вышел ли из матча: по этому числу раздаются ручки бутыли",
            ["CarryItemNetwork.StackHoldRpc(team)"] = "у чьего штабеля держат E; отправителя берём из RpcParams",
            ["CarryItemNetwork.StackHoldRpc(held)"] = "начали держать или отпустили",

            // ---- Ручки и полёт: MultiCarryObject ----
            ["MultiCarryObject.netState"] =
                "занятые ручки — состояние, а не событие: защищает от двойного захвата и догоняет опоздавшего",
            ["MultiCarryObject.netState.Handle0"] = "кто держит ручку 0, NetworkObjectId",
            ["MultiCarryObject.netState.Handle1"] = "кто держит ручку 1",
            ["MultiCarryObject.netState.Handle2"] = "кто держит ручку 2",
            ["MultiCarryObject.netState.Handle3"] = "кто держит ручку 3",
            ["MultiCarryObject.netState.InFlight"] =
                "объект брошен и ещё не приземлился: на свистке такой не засчитывается, и взяться за него нельзя",
            ["MultiCarryObject.netState.LastRelease"] =
                "причина последнего срыва, байт — под звук и эффекты; отдельного канала под неё не заводим",
            ["MultiCarryObject.CarrierIntentRpc(intent)"] =
                "вектор ввода несущего: сервер отличает им рывок от полёта, движение считает не по нему",
            ["MultiCarryObject.RequestThrowRpc()"] = "намерение бросить; отправителя берём из RpcParams",
            ["MultiCarryObject.RequestReleaseRpc(reason)"] =
                "сбитый несущий сообщает, что уронил ручку: нокдаун считает его мотор, у сервера чужой выключен",

            // ---- Бутыль: WaterBottle ----
            ["WaterBottle.netState"] = "чья бутыль, сколько ручек, сколько воды — одной структурой",
            ["WaterBottle.netState.Team"] = "чья бутыль: чужой за её ручку не возьмётся",
            ["WaterBottle.netState.Handles"] = "сколько у неё ручек — размер команды на момент выдачи",
            ["WaterBottle.netState.Water"] = "остаток воды: это счёт, и клиент его только показывает",
            ["WaterBottle.AnnounceLossRpc(amount)"] = "сколько воды ушло — под эффект, сам уровень едет состоянием",
            ["WaterBottle.AnnounceLossRpc(reason)"] = "почему ушло: по уровню не видно, брызги это или струя",

            // ---- Общий мост фаз, он же у всех мини-игр ----
            ["NetworkMinigameBridge.phase"] = "фаза мини-игры, общий механизм проекта",
            ["NetworkMinigameBridge.roundRemaining"] = "остаток общего таймера, общий механизм",
            ["NetworkMinigameBridge.roundDuration"] = "длительность раунда, общий механизм",
            ["NetworkMinigameBridge.ApplyResultsRpc(ids)"] = "итоговые места, общий механизм",
            ["NetworkMinigameBridge.ApplyResultsRpc(places)"] = "итоговые места, общий механизм",
            ["NetworkMinigameBridge.LeaveRoundRpc()"] = "выход из раунда, общий механизм"
        };

        /// <summary>
        /// Чего в трафике этой игры быть не может ни под каким соусом.
        ///
        /// Не секреты — их здесь нет, — а <b>признаки того, что по сети поехало
        /// то, что и так одинаково у всех</b>: числа баланса из конфига,
        /// геометрия арены, фаза ловушек. Всё это либо лежит в ассете на каждой
        /// машине, либо считается от общих часов, и появление такого поля в
        /// канале означает лишний трафик на всю катку.
        /// </summary>
        private static readonly string[] ForbiddenTokens =
        {
            "config", "settings", "capacity", "threshold", "cooldown",
            "period", "phaseoffset", "radius", "waypoint", "route", "arena"
        };

        [MenuItem("Igruha/Переноска предмета/Перечень реплицируемых полей")]
        public static void RunFromMenu()
        {
            bool clean = Run(out string report);

            if (clean)
            {
                Debug.Log(report);
                return;
            }

            Debug.LogError(report);
        }

        /// <summary>
        /// Снять перечень и проверить его. Возвращает false, если в трафик
        /// попало что-то, чего там быть не должно.
        /// </summary>
        public static bool Run(out string report)
        {
            var text = new StringBuilder(4096);
            var problems = new List<string>();
            var seen = new List<string>();

            text.AppendLine("=== «Переноска предмета»: перечень реплицируемых полей (снят рефлексией) ===");
            text.AppendLine();
            text.AppendLine("-- Каналы состояния (NetworkVariable / NetworkList) и параметры Rpc --");

            foreach (Type type in ReplicatedTypes())
            {
                CollectChannels(type, seen, text, problems);
                CollectRpcParameters(type, seen, text, problems);
            }

            AuditWhitelistIsCurrent(seen, text, problems);
            AuditClockDrivenStaysOffTheWire(text, problems);
            AuditSelfCheck(text, problems);

            text.AppendLine();
            if (problems.Count == 0)
            {
                text.AppendLine($"ИТОГ: чисто. Реплицируется {seen.Count} членов, каждый — с обоснованием.");
                report = text.ToString();
                return true;
            }

            text.AppendLine($"ИТОГ: ПРОВАЛ, замечаний {problems.Count}:");
            for (int i = 0; i < problems.Count; i++)
            {
                text.Append("  ").Append(i + 1).Append(". ").AppendLine(problems[i]);
            }

            report = text.ToString();
            return false;
        }

        /// <summary>
        /// Что смотрим: сетевую половину самой игры, переноску из Core (её
        /// каналы принадлежат этой игре, хоть класс и общий) и общий мост фаз.
        /// </summary>
        private static IEnumerable<Type> ReplicatedTypes()
        {
            foreach (Type type in typeof(CarryItemMinigame).Assembly.GetTypes())
            {
                if (typeof(NetworkBehaviour).IsAssignableFrom(type) &&
                    type.Namespace == typeof(CarryItemMinigame).Namespace)
                {
                    yield return type;
                }
            }

            yield return typeof(MultiCarryObject);
            yield return typeof(NetworkMinigameBridge);
        }

        private static void CollectChannels(Type type, List<string> seen, StringBuilder text, List<string> problems)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!IsChannel(field.FieldType, out string channel, out Type payload))
                {
                    continue;
                }

                string path = $"{type.Name}.{field.Name}";
                seen.Add(path);

                text.AppendLine($"  {path} : {channel}<{payload.Name}>");
                Check(path, $"{field.Name}.{payload.Name}", text, problems);

                foreach (FieldInfo leaf in PayloadFields(payload))
                {
                    string leafPath = $"{path}.{leaf.Name}";
                    seen.Add(leafPath);

                    text.AppendLine($"      {leaf.Name} : {leaf.FieldType.Name}");
                    Check(leafPath, leaf.Name, text, problems);
                }
            }
        }

        private static void CollectRpcParameters(Type type, List<string> seen, StringBuilder text,
            List<string> problems)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<RpcAttribute>() == null)
                {
                    continue;
                }

                bool hasPayload = false;

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    // RpcParams — служебная адресация NGO, полезной нагрузки в ней нет.
                    if (parameter.ParameterType == typeof(RpcParams) ||
                        parameter.ParameterType == typeof(ServerRpcParams) ||
                        parameter.ParameterType == typeof(ClientRpcParams))
                    {
                        continue;
                    }

                    hasPayload = true;

                    string path = $"{type.Name}.{method.Name}({parameter.Name})";
                    seen.Add(path);

                    text.AppendLine($"  {path} : {parameter.ParameterType.Name}");
                    Check(path, $"{method.Name}.{parameter.Name}.{parameter.ParameterType.Name}", text, problems);
                }

                if (hasPayload)
                {
                    continue;
                }

                // Сообщение без нагрузки — тоже часть перечня: по нему видно,
                // что намерение уходит пустым, а отправителя берут из RpcParams.
                string emptyPath = $"{type.Name}.{method.Name}()";
                seen.Add(emptyPath);
                text.AppendLine($"  {emptyPath} : без нагрузки");
                Check(emptyPath, method.Name, text, problems);
            }
        }

        private static bool IsChannel(Type fieldType, out string channel, out Type payload)
        {
            channel = null;
            payload = null;

            if (!fieldType.IsGenericType)
            {
                return false;
            }

            Type definition = fieldType.GetGenericTypeDefinition();
            if (definition == typeof(NetworkVariable<>))
            {
                channel = "NetworkVariable";
            }
            else if (definition == typeof(NetworkList<>))
            {
                channel = "NetworkList";
            }
            else
            {
                return false;
            }

            payload = fieldType.GetGenericArguments()[0];
            return true;
        }

        /// <summary>Публичные поля полезной нагрузки — примитив не раскрывается.</summary>
        private static IEnumerable<FieldInfo> PayloadFields(Type payload)
        {
            if (payload.IsPrimitive || payload.IsEnum || payload == typeof(string))
            {
                yield break;
            }

            foreach (FieldInfo field in payload.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                yield return field;
            }
        }

        /// <summary>
        /// Одно место, где путь сверяется и с белым списком, и с токенами.
        /// Токены ищутся в именах самого члена и его типа, а не во всём пути:
        /// иначе одно неудачное слово в названии класса пометило бы разом всё,
        /// что в нём лежит.
        /// </summary>
        private static void Check(string path, string names, StringBuilder text, List<string> problems)
        {
            string token = FindForbiddenToken(names);
            if (token != null)
            {
                text.AppendLine($"      ⛔ содержит «{token}» — это либо число из конфига, либо фаза от общих " +
                                "часов; и то и другое есть у каждой машины и по сети ехать не должно");
                problems.Add($"{path}: похоже на то, что и так одинаково у всех (токен «{token}»)");
                return;
            }

            if (Allowed.TryGetValue(path, out string reason))
            {
                text.AppendLine($"      ✔ {reason}");
                return;
            }

            text.AppendLine("      ⛔ в перечне нет");
            problems.Add($"{path}: реплицируется, но в перечне не разобрано. " +
                         "Внеси с обоснованием — или убери из трафика");
        }

        private static string FindForbiddenToken(string names)
        {
            string lower = names.ToLowerInvariant();
            for (int i = 0; i < ForbiddenTokens.Length; i++)
            {
                if (lower.Contains(ForbiddenTokens[i]))
                {
                    return ForbiddenTokens[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Перечень не должен и протухать. Строка, оставшаяся в белом списке
        /// после того как поле убрали из трафика, — это разрешение, выданное
        /// неизвестно чему: следующее поле с тем же именем пройдёт молча.
        /// </summary>
        private static void AuditWhitelistIsCurrent(List<string> seen, StringBuilder text, List<string> problems)
        {
            text.AppendLine();
            text.AppendLine("-- Перечень не протух --");

            var live = new HashSet<string>(seen);
            int stale = 0;

            foreach (KeyValuePair<string, string> entry in Allowed)
            {
                if (live.Contains(entry.Key))
                {
                    continue;
                }

                stale++;
                text.AppendLine($"  ⛔ {entry.Key}: в перечне есть, в трафике нет");
                problems.Add($"{entry.Key}: разрешение выдано полю, которого больше нет — убери строку из перечня");
            }

            if (stale == 0)
            {
                text.AppendLine("  ✔ все строки перечня соответствуют живым членам");
            }
        }

        /// <summary>
        /// Ловушки не имеют права появиться в трафике вовсе: балка, тачка и
        /// труба считают фазу от <c>NetworkClock</c>, то есть сходятся у всех
        /// без единого пакета. Канал у любой из них означал бы, что кто-то
        /// решил «на всякий случай синхронизировать» — и заодно сломал
        /// единственную причину, по которой они бесплатны.
        /// </summary>
        private static void AuditClockDrivenStaysOffTheWire(StringBuilder text, List<string> problems)
        {
            text.AppendLine();
            text.AppendLine("-- Ловушки остаются вне трафика --");

            Type[] clockDriven =
            {
                typeof(Igruha.Core.Traps.SwingingBeamTrap),
                typeof(Igruha.Core.Traps.PeriodicTrapDriver),
                typeof(Igruha.Core.Traps.PushZone)
            };

            foreach (Type type in clockDriven)
            {
                bool networked = typeof(NetworkBehaviour).IsAssignableFrom(type);
                if (networked)
                {
                    text.AppendLine($"  ⛔ {type.Name}: стал NetworkBehaviour");
                    problems.Add($"{type.Name}: ловушке сеть не нужна — её фаза считается от общих часов");
                    continue;
                }

                text.AppendLine($"  ✔ {type.Name}: обычный MonoBehaviour, фаза от общих часов");
            }
        }

        /// <summary>
        /// Проверка проверки. Зелёный отчёт ничего не стоит, пока не показано,
        /// что он умеет краснеть: сломанный сбор молча пропустит всё подряд, и
        /// отличить его от честного «чисто» будет нечем.
        ///
        /// Поэтому через тот же сбор прогоняется подсадной класс с заведомо
        /// лишним каналом — числами из конфига. Он обязан дать ровно два
        /// замечания: одно на сам канал, второе на его поле.
        /// </summary>
        private static void AuditSelfCheck(StringBuilder text, List<string> problems)
        {
            text.AppendLine();
            text.AppendLine("-- Проверка проверки (подсадная утечка) --");

            var probe = new StringBuilder();
            var caught = new List<string>();
            var ignored = new List<string>();

            CollectChannels(typeof(BalanceLeakCanary), ignored, probe, caught);

            if (caught.Count >= 2)
            {
                text.AppendLine($"  ✔ подсадной канал пойман, замечаний {caught.Count}");
                return;
            }

            text.AppendLine($"  ⛔ подсадной канал НЕ пойман (замечаний {caught.Count})");
            problems.Add("сбор перечня сломан: подсадная утечка прошла молча, значит и настоящая пройдёт");
        }

        /// <summary>
        /// Подсадной. Не используется в игре и существует ровно затем, чтобы
        /// проверка на нём спотыкалась: канал с числами баланса — именно то,
        /// чего в трафике быть не должно, потому что конфиг лежит у каждого.
        /// </summary>
        private sealed class BalanceLeakCanary : NetworkBehaviour
        {
            private readonly NetworkVariable<CanaryPayload> configLeak = new NetworkVariable<CanaryPayload>();

            /// <summary>Ссылка на поле, чтобы компилятор не счёл его неиспользуемым.</summary>
            public CanaryPayload Peek => configLeak.Value;
        }

        /// <summary>Нагрузка подсадного: заведомо запретное имя поля.</summary>
        public struct CanaryPayload : INetworkSerializable, IEquatable<CanaryPayload>
        {
            public int TankCapacity;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter =>
                serializer.SerializeValue(ref TankCapacity);

            public bool Equals(CanaryPayload other) => TankCapacity == other.TankCapacity;
        }
    }
}
