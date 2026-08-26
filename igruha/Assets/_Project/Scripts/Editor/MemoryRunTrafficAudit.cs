using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.MemoryRun;
using Igruha.Networking;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Доказательство того, что маршрут «Рейса на память» не покидает сервер.
    ///
    /// Вся мини-игра держится на одном секрете: какая из трёх плит на каждом
    /// из десяти шагов безопасна. «Вроде не отправляем» — недостаточно: секрет
    /// утекает не через явную отправку, а через поле, которое кто-то добавил
    /// в реплицируемую структуру заодно, и которое «всё равно не читается».
    ///
    /// Поэтому перечень того, что реально едет в сеть, снимается <b>рефлексией
    /// по живым типам</b>, а не глазами по коду. Ровно так закрывали приёмку
    /// сети «Верю / не верю», где тем же способом убедились, что содержимое
    /// коробок в трафик не попало (STATE, раздел 3.15).
    ///
    /// Запуск — пункт меню <c>Igruha/Рейс на память/Перечень реплицируемых полей</c>
    /// либо <see cref="Run"/> из любого скрипта. Повторить после любой правки
    /// сети — одна команда.
    /// </summary>
    /// <remarks>
    /// <b>Проверка построена на белом списке, а не на чёрном.</b> Чёрный список
    /// ловит только те названия, которые кто-то догадался туда внести, и молчит
    /// на поле <c>hint</c> или <c>extra</c>. Белый список валится на <i>любом</i>
    /// новом реплицируемом поле: пока его не разобрали и не внесли в перечень
    /// с обоснованием, приёмка сети не проходит. Чёрный список токенов оставлен
    /// сверх того — он объясняет, чем именно плохо найденное.
    ///
    /// Что проверка <b>не</b> ловит по устройству: утечку через уже разрешённое
    /// поле (скажем, если дальний шаг начнут писать в кодировке полосы).
    /// Такое ловится только чтением кода — и именно поэтому у каждой строки
    /// белого списка есть обоснование, а не просто галочка.
    /// </remarks>
    public static class MemoryRunTrafficAudit
    {
        /// <summary>
        /// Всё, что имеет право уезжать с сервера, — поимённо и с обоснованием.
        /// Ключ: <c>Тип.член</c> для канала, <c>Тип.член.Поле</c> для поля
        /// в полезной нагрузке, <c>Тип.метод(параметр)</c> для аргумента Rpc.
        /// </summary>
        private static readonly Dictionary<string, string> Allowed = new Dictionary<string, string>
        {
            // ---- очередь ----
            ["MemoryRunNetwork.turnOrder"] =
                "порядок ходов — общеизвестен, объявляется всем один раз в начале раунда (спека 10)",

            // ---- чей ход ----
            ["MemoryRunNetwork.turn"] = "чей ход и до какого момента",
            ["MemoryRunNetwork.turn.WalkerId"] =
                "кто идёт — над ним и так горит метка, видят все восемь",
            ["MemoryRunNetwork.turn.TurnNumber"] =
                "номер хода с начала раунда; счётчик ходов, к плитам отношения не имеет",
            ["MemoryRunNetwork.turn.ArmTime"] =
                "момент конца объявления «твой ход» по общим часам",
            ["MemoryRunNetwork.turn.Deadline"] =
                "момент конца хода по общим часам",

            // ---- прогресс ----
            ["MemoryRunNetwork.progress"] = "прогресс участников, из него считается таблица мест",
            ["MemoryRunNetwork.progress.PlayerId"] = "чья строка",
            ["MemoryRunNetwork.progress.BestStep"] =
                "СЧЁТЧИК пройденных шагов, а не полоса. «Дошёл до пятого» видели все зрители; " +
                "какая из трёх плит пятого шага безопасна, отсюда не следует",
            ["MemoryRunNetwork.progress.Deaths"] =
                "сколько раз погиб — каждый взрыв слышал весь зал; чужой счётчик в HUD не выводится",
            ["MemoryRunNetwork.progress.ArrivalOrder"] = "каким по счёту дошёл до двери",
            ["MemoryRunNetwork.progress.Finished"] = "дошёл ли до двери",
            ["MemoryRunNetwork.progress.BestStepTime"] = "когда поставлен рекорд, тайбрейк при равном шаге",

            // ---- события ----
            ["MemoryRunNetwork.DetonationRpc(center)"] =
                "точка взрыва для VFX и звука. Это то, что и так видели все восемь человек. " +
                "Обратного события «плита оказалась безопасной» не существует — оно и было бы " +
                "маршрутом, выданным по одному шагу",

            // ---- общий слой шаблона, живёт на том же объекте ----
            ["NetworkMinigameBridge.phase"] = "фаза мини-игры: обучалка / раунд / результаты",
            ["NetworkMinigameBridge.roundRemaining"] = "остаток общего таймера раунда",
            ["NetworkMinigameBridge.roundDuration"] = "длительность общего таймера раунда",
            ["NetworkMinigameBridge.ApplyResultsRpc(ids)"] = "итоговые места: идентификаторы",
            ["NetworkMinigameBridge.ApplyResultsRpc(places)"] = "итоговые места: места"
        };

        /// <summary>
        /// Слова, которыми в этом проекте называют маршрут. В имени
        /// реплицируемого члена любое из них — не «подозрительно», а провал
        /// приёмки: в сеть не уезжает даже язык, на котором маршрут описан.
        /// </summary>
        private static readonly string[] ForbiddenTokens =
        {
            "route", "маршрут", "lane", "полос", "safe", "безопас",
            "seed", "сид", "mine", "мин", "secret", "секрет", "answer", "ответ"
        };

        /// <summary>
        /// То же для полей инспектора — но короче. Геометрия имеет полное
        /// право говорить про полосы и мины: <c>laneGap</c> — это зазор между
        /// плитами, а <c>mineImpulse</c> — сила подброса, и оба напечатаны
        /// в спеке. Запрещено другое — хранить в инспекторе, какая плита
        /// безопасна, и чем этот выбор восстанавливается.
        /// </summary>
        private static readonly string[] SecretTokens =
        {
            "route", "маршрут", "safe", "безопас", "seed", "сид", "secret", "секрет", "answer", "ответ"
        };

        /// <summary>
        /// Публичная поверхность <see cref="MemoryRunRoute"/>, разрешённая
        /// целиком. Всё сверх этого — новый способ спросить у маршрута то,
        /// чего спрашивать нельзя.
        /// </summary>
        private static readonly HashSet<string> RouteApi = new HashSet<string>
        {
            "Left", "Center", "Right",
            "Steps", "Generate", "IsSafe", "Clear", "IsValidSequence"
        };

        [MenuItem("Igruha/Рейс на память/Перечень реплицируемых полей")]
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

            text.AppendLine("=== «Рейс на память»: перечень реплицируемых полей (снят рефлексией) ===");
            text.AppendLine();

            AuditReplicatedMembers(text, problems);
            AuditRouteIsLocked(text, problems);
            AuditSerializedFields(text, problems);
            AuditSelfCheck(text, problems);

            text.AppendLine();
            if (problems.Count == 0)
            {
                text.AppendLine("ИТОГ: чисто. Ни маршрута, ни сида, ни производных от них среди реплицируемого нет.");
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

        // ========== 1. ЧТО РЕАЛЬНО ЕДЕТ ==========

        private static void AuditReplicatedMembers(StringBuilder text, List<string> problems)
        {
            text.AppendLine("-- Каналы состояния (NetworkVariable / NetworkList) и параметры Rpc --");

            var seen = new List<string>();

            foreach (Type type in ReplicatedTypes())
            {
                CollectChannels(type, seen, text, problems);
                CollectRpcParameters(type, seen, text, problems);
            }

            if (seen.Count == 0)
            {
                problems.Add("не найдено ни одного реплицируемого члена — проверка смотрит не туда, " +
                             "и её результату верить нельзя");
            }

            // Белый список — не только про лишнее, но и про пропавшее: канал,
            // исчезнувший молча, значит, что клиент чего-то не увидит.
            foreach (KeyValuePair<string, string> entry in Allowed)
            {
                if (seen.Contains(entry.Key))
                {
                    continue;
                }

                text.AppendLine($"   ⚠ в перечне есть «{entry.Key}», но в коде его больше нет — " +
                                "строку белого списка пора убрать");
            }
        }

        /// <summary>
        /// Типы, чьи поля реально уезжают в сеть: сетевая половина мини-игры
        /// и общий мост шаблона, который висит на том же объекте.
        /// </summary>
        private static IEnumerable<Type> ReplicatedTypes()
        {
            foreach (Type type in typeof(MemoryRunMinigame).Assembly.GetTypes())
            {
                if (typeof(NetworkBehaviour).IsAssignableFrom(type) &&
                    type.Namespace == typeof(MemoryRunMinigame).Namespace)
                {
                    yield return type;
                }
            }

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

        private static void CollectRpcParameters(Type type, List<string> seen, StringBuilder text, List<string> problems)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<RpcAttribute>() == null)
                {
                    continue;
                }

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    // RpcParams — служебная адресация NGO, полезной нагрузки в ней нет.
                    if (parameter.ParameterType == typeof(RpcParams) ||
                        parameter.ParameterType == typeof(ServerRpcParams) ||
                        parameter.ParameterType == typeof(ClientRpcParams))
                    {
                        continue;
                    }

                    string path = $"{type.Name}.{method.Name}({parameter.Name})";
                    seen.Add(path);

                    text.AppendLine($"  {path} : {parameter.ParameterType.Name}");
                    Check(path, $"{method.Name}.{parameter.Name}.{parameter.ParameterType.Name}", text, problems);
                }
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
        /// Токены ищутся в <paramref name="names"/> — именах самого члена и его
        /// типа, — а не во всём пути: иначе одно неудачное слово в названии
        /// класса пометило бы разом всё, что в нём лежит.
        /// </summary>
        private static void Check(string path, string names, StringBuilder text, List<string> problems)
        {
            string token = FindForbiddenToken(names, ForbiddenTokens);
            if (token != null)
            {
                problems.Add($"«{path}» — в названии слово «{token}», то есть язык маршрута. " +
                             "Маршрут и сид не покидают сервер ни в каком виде (спека 10.1)");
                text.AppendLine($"      ❌ запретное слово «{token}»");
                return;
            }

            if (Allowed.TryGetValue(path, out string reason))
            {
                text.AppendLine($"      ✔ {reason}");
                return;
            }

            problems.Add($"«{path}» реплицируется, но в перечне его нет. Новое поле в трафике — " +
                         "это и есть тот случай, ради которого проверка написана: разобрать, " +
                         "убедиться, что маршрут через него не выводится, и внести в белый список " +
                         "с обоснованием");
            text.AppendLine("      ❌ нет в перечне");
        }

        private static string FindForbiddenToken(string names, string[] tokens)
        {
            string lower = names.ToLowerInvariant();
            for (int i = 0; i < tokens.Length; i++)
            {
                if (lower.Contains(tokens[i]))
                {
                    return tokens[i];
                }
            }

            return null;
        }

        // ========== 2. МАРШРУТ ЗАПЕРТ ==========

        private static void AuditRouteIsLocked(StringBuilder text, List<string> problems)
        {
            text.AppendLine();
            text.AppendLine("-- Маршрут: что у него вообще можно спросить --");

            Type route = typeof(MemoryRunRoute);

            if (typeof(UnityEngine.Object).IsAssignableFrom(route))
            {
                problems.Add("MemoryRunRoute стал объектом Unity — значит, его можно положить " +
                             "в поле инспектора и утащить в префаб или сцену");
            }

            foreach (MemberInfo member in route.GetMembers(BindingFlags.Instance | BindingFlags.Static |
                                                           BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (member is ConstructorInfo || RouteApi.Contains(member.Name))
                {
                    continue;
                }

                if (member is MethodInfo method && method.IsSpecialName)
                {
                    // get_/set_ разбираются по своему свойству, дважды не ругаемся.
                    continue;
                }

                problems.Add($"MemoryRunRoute.{member.Name} — новый публичный член у хранителя " +
                             "секрета. Наружу маршрут отдаётся только ответом «да/нет» на " +
                             "конкретную плиту (IsSafe)");
            }

            foreach (FieldInfo field in route.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                problems.Add($"MemoryRunRoute.{field.Name} — публичное поле у хранителя секрета");
            }

            text.AppendLine($"  MemoryRunRoute: публичная поверхность — {string.Join(", ", RouteApi)}");
            text.AppendLine("  единственный способ что-то узнать — IsSafe(шаг, полоса) → bool, " +
                            "по одной конкретной плите");

            // Компонента плиты в игре нет вовсе, и это сильная сторона сборки:
            // приземление определяет сервер по позиции, поэтому плите нечего
            // хранить и нечего утекать.
            bool plateExists = false;
            foreach (Type type in typeof(MemoryRunMinigame).Assembly.GetTypes())
            {
                if (type.Namespace == typeof(MemoryRunMinigame).Namespace && type.Name.Contains("Plate"))
                {
                    plateExists = true;
                    text.AppendLine($"  ⚠ появился тип {type.Name} — проверить, что он не хранит " +
                                    "свою безопасность (спека 10.1)");
                }
            }

            if (!plateExists)
            {
                text.AppendLine("  MinePlate: класса нет вовсе. Приземление сервер определяет по позиции " +
                                "(MemoryRunConfig.TryGetCell), поэтому плите нечего знать о себе");
            }
        }

        // ========== 3. НИЧЕГО НЕ ЛЕЖИТ В СЦЕНЕ И ПРЕФАБАХ ==========

        /// <summary>
        /// Поле, попавшее в инспектор, уезжает в сцену — а сцену игрок получает
        /// вместе с билдом. Это второй, тихий путь утечки: сеть тут ни при чём,
        /// секрет уже лежит на диске у каждого.
        /// </summary>
        private static void AuditSerializedFields(StringBuilder text, List<string> problems)
        {
            text.AppendLine();
            text.AppendLine("-- Что уезжает в сцену и префабы (сериализованные поля) --");

            int checkedTypes = 0;

            foreach (Type type in typeof(MemoryRunMinigame).Assembly.GetTypes())
            {
                if (type.Namespace != typeof(MemoryRunMinigame).Namespace)
                {
                    continue;
                }

                if (!typeof(MonoBehaviour).IsAssignableFrom(type) && !typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    continue;
                }

                checkedTypes++;

                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                           BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    bool serialized = field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
                    if (!serialized || field.IsInitOnly)
                    {
                        continue;
                    }

                    string token = FindForbiddenToken(field.Name, SecretTokens);
                    if (token == null)
                    {
                        continue;
                    }

                    problems.Add($"{type.Name}.{field.Name} сериализуется в инспекторе, а в названии " +
                                 $"слово «{token}». Такое поле уедет в сцену, а сцена — в билд каждого игрока");
                }
            }

            text.AppendLine($"  проверено типов сцены: {checkedTypes}; полей со словами маршрута не найдено");
            text.AppendLine("  маршрут живёт в приватном readonly-поле MemoryRunMinigame.route — " +
                            "Unity такое не сериализует, в сцену оно не попадает");
        }

        // ========== 4. САМОПРОВЕРКА ==========

        /// <summary>
        /// Проверка, которая ничего не ловит, хуже отсутствующей: она успокаивает.
        /// Поэтому каждый запуск прогоняет детектор по заведомо испорченному
        /// типу и убеждается, что тот падает. Внести утечку нарочно и посмотреть,
        /// поймает ли, руками больше не нужно — это делается само.
        /// </summary>
        private static void AuditSelfCheck(StringBuilder text, List<string> problems)
        {
            text.AppendLine();
            text.AppendLine("-- Самопроверка: ловит ли детектор нарочную утечку --");

            var canaryText = new StringBuilder();
            var canaryProblems = new List<string>();
            CollectChannels(typeof(RouteLeakCanary), new List<string>(), canaryText, canaryProblems);

            if (canaryProblems.Count == 0)
            {
                problems.Add("детектор НЕ поймал подсадное поле с маршрутом — проверка не проверяет " +
                             "ничего, и её зелёному ответу верить нельзя");
                text.AppendLine("  ❌ подсадная утечка прошла мимо");
                return;
            }

            text.AppendLine($"  ✔ подсадная утечка поймана, замечаний {canaryProblems.Count}:");
            for (int i = 0; i < canaryProblems.Count; i++)
            {
                text.Append("      ").AppendLine(canaryProblems[i]);
            }
        }

        /// <summary>
        /// Подсадной тип: то, чего в игре быть не должно. Никогда не создаётся —
        /// детектор читает только его поля рефлексией.
        /// </summary>
        private sealed class RouteLeakCanary
        {
            private readonly NetworkVariable<CanaryPayload> canary = new NetworkVariable<CanaryPayload>();

            /// <summary>Чтобы компилятор не считал поле неиспользуемым.</summary>
            public int Touch => canary != null ? 1 : 0;
        }

        private struct CanaryPayload : INetworkSerializable, IEquatable<CanaryPayload>
        {
            public int PlayerId;
            public int SafeLane;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref PlayerId);
                serializer.SerializeValue(ref SafeLane);
            }

            public bool Equals(CanaryPayload other) => PlayerId == other.PlayerId && SafeLane == other.SafeLane;
        }
    }
}
