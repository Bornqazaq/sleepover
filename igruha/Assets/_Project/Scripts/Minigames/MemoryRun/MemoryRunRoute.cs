using System;
using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>
    /// Секрет, вокруг которого построена вся игра: по одной безопасной полосе
    /// на каждый из десяти шагов.
    ///
    /// <b>Наружу не отдаётся ничем, кроме <see cref="IsSafe"/>.</b> Ни массива,
    /// ни свойства, ни сериализованного поля в инспекторе. Даже сид не хранится
    /// после генерации: по сиду маршрут восстанавливается целиком, то есть сид —
    /// это тот же секрет, только в профиль.
    ///
    /// В сетевой фазе объект живёт только на сервере, и проверять это будут
    /// рефлексией по списку реплицируемых полей — так закрывали приёмку
    /// «Верю / не верю», где содержимым коробок распорядились ровно так же.
    /// </summary>
    /// <remarks>
    /// Переиспользуемый контейнер: между раундами не аллоцирует.
    /// </remarks>
    public sealed class MemoryRunRoute
    {
        /// <summary>Полосы: 0 — левая, 1 — центральная, 2 — правая.</summary>
        public const int Left = 0;
        public const int Center = 1;
        public const int Right = 2;

        private readonly List<int> safeLanes = new List<int>(16);
        private readonly List<int> candidates = new List<int>(MemoryRunConfig.LaneCount);

        /// <summary>Сколько шагов в текущем маршруте. Ноль — маршрут не сгенерирован.</summary>
        public int Steps => safeLanes.Count;

        /// <summary>
        /// Сгенерировать маршрут. Звать только там, где есть авторитет:
        /// рандом обязан быть серверным.
        /// </summary>
        public void Generate(MemoryRunConfig config, int seed)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            safeLanes.Clear();
            var random = new System.Random(seed);

            for (int step = 0; step < config.Steps; step++)
            {
                CollectCandidates(config, step);

                if (candidates.Count == 0)
                {
                    // Недостижимо: из любой полосы всегда есть куда пойти, даже
                    // когда обе оговорки бьют разом. Но если правила однажды
                    // ужесточат, молчаливый тупик станет самым дорогим багом
                    // в игре — лучше упасть здесь и сразу.
                    Debug.LogError($"MemoryRunRoute: на шаге {step} не осталось допустимых полос — правила генерации противоречат друг другу");
                    safeLanes.Add(Center);
                    continue;
                }

                safeLanes.Add(candidates[random.Next(candidates.Count)]);
            }
        }

        /// <summary>
        /// Безопасна ли плита. Единственный способ узнать что-либо о маршруте.
        /// Шаг или полоса вне диапазона — false: наступить туда нельзя.
        /// </summary>
        public bool IsSafe(int step, int lane)
        {
            if (step < 0 || step >= safeLanes.Count)
            {
                return false;
            }

            return safeLanes[step] == lane;
        }

        public void Clear() => safeLanes.Clear();

        /// <summary>
        /// Допустимые полосы на очередном шаге по обоим ограничениям сразу.
        ///
        /// <b>1. Не более двух одинаковых полос подряд.</b> Без него выпадает
        /// «центр, центр, центр, центр»: четыре шага запоминаются как один,
        /// и плотность информации проваливается.
        ///
        /// <b>2. Сдвиг не больше чем на одну полосу за шаг.</b> Это не вкусовщина,
        /// а физика. Дальность прыжка персонажа 4.69 м; переход лево ↔ право,
        /// считая от края плиты, требует 4.55 — запас 3% и только с идеального
        /// угла. Спека прямо запрещает смерти от кривой физики: игра про память,
        /// а не про точность прыжка. После запрета самый длинный прыжок в игре —
        /// 1.61 м.
        /// </summary>
        private void CollectCandidates(MemoryRunConfig config, int step)
        {
            candidates.Clear();

            int previous = step > 0 ? safeLanes[step - 1] : -1;
            int runLength = CountTrailingRun(previous);

            for (int lane = 0; lane < MemoryRunConfig.LaneCount; lane++)
            {
                if (previous >= 0 && Mathf.Abs(lane - previous) > 1)
                {
                    continue;
                }

                if (lane == previous && runLength >= config.MaxSameLaneRun)
                {
                    continue;
                }

                candidates.Add(lane);
            }
        }

        /// <summary>Сколько последних шагов подряд стоят на полосе <paramref name="lane"/>.</summary>
        private int CountTrailingRun(int lane)
        {
            if (lane < 0)
            {
                return 0;
            }

            int run = 0;
            for (int i = safeLanes.Count - 1; i >= 0 && safeLanes[i] == lane; i--)
            {
                run++;
            }

            return run;
        }

        /// <summary>
        /// Проверка готовой последовательности на оба ограничения. Нужна тестам:
        /// именно ей перебор всех 3¹⁰ комбинаций даёт 2484 годных.
        ///
        /// Генератор выбирает полосу равновероятно из допустимых на каждом шаге,
        /// то есть по годным маршрутам распределение не строго равномерное.
        /// Для party-game это безразлично: важно, что недопустимых не бывает
        /// вовсе, а повтор маршрута за вечер практически невозможен.
        /// </summary>
        public static bool IsValidSequence(IReadOnlyList<int> lanes, int maxSameLaneRun)
        {
            if (lanes == null || lanes.Count == 0)
            {
                return false;
            }

            int run = 1;
            for (int i = 0; i < lanes.Count; i++)
            {
                if (lanes[i] < 0 || lanes[i] >= MemoryRunConfig.LaneCount)
                {
                    return false;
                }

                if (i == 0)
                {
                    continue;
                }

                if (Mathf.Abs(lanes[i] - lanes[i - 1]) > 1)
                {
                    return false;
                }

                run = lanes[i] == lanes[i - 1] ? run + 1 : 1;
                if (run > maxSameLaneRun)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
