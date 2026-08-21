using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Маршрут болванки по коридору этажа: ломаная из точек, каждая — середина
    /// свободной полосы на своём срезе трассы.
    ///
    /// Нужен потому, что <c>DebugPlayerBot</c> — жадный искатель: он идёт прямо
    /// на цель и обходит помеху вбок фиксированным числом проб. На пустом полу
    /// этого хватает, а этаж Duck Hunt по спеке заставлен укрытиями (12 штук на
    /// первом), и прямая от входа до лестницы пересекает их насквозь. Болванка
    /// упиралась в первое же и стояла до конца раунда.
    ///
    /// Маршрут строится один раз на этаж и живёт у вызывающего, а не
    /// пересчитывается из геометрии каждый кадр: именно на пересчёте прошлые
    /// попытки и разъезжались — каждая правка вскрывала новый частный случай.
    ///
    /// Это отладочная навигация, к правилам игры отношения не имеет: живой
    /// игрок видит этаж глазами.
    /// </summary>
    public static class DuckHuntBotRoute
    {
        /// <summary>Шаг между срезами трассы, ШП. Мельче — маршрут виляет, крупнее — срезает углы мимо щелей.</summary>
        private const float SampleStep = 2f;

        /// <summary>Шаг перебора по глубине при поиске свободной полосы, ШП.</summary>
        private const float DepthStep = 0.25f;

        /// <summary>Отступ от стен по глубине, ШП: у самой стены ходить незачем.</summary>
        private const float DepthMargin = 1f;

        /// <summary>Высота, на которой щупается проход, ШП. Выше низкого укрытия — его тоже нужно обходить.</summary>
        private const float ProbeHeight = 1f;

        /// <summary>Насколько широкой должна быть полоса, чтобы считаться проходом, ШП.</summary>
        private const float MinLaneWidth = 0.8f;

        /// <summary>
        /// Построить маршрут по коридору этажа от прогресса <paramref name="fromProgress"/>
        /// до <paramref name="toProgress"/>. Точки идут по возрастанию прогресса.
        /// </summary>
        /// <param name="preferredDepth">
        /// Полоса, которой болванка держится по возможности. Разным болванкам
        /// задают разные: с общей полосой они сходятся в одну точку и упираются
        /// друг в друга — телá игроков в помехи маршрута не входят, обходить
        /// друг друга болванки не умеют, и обе стоят до конца раунда.
        /// </param>
        public static void Build(
            List<Vector3> route,
            DuckHuntArena arena,
            int floor,
            float fromProgress,
            float toProgress,
            LayerMask obstacles,
            float bodyRadius,
            float preferredDepth)
        {
            route.Clear();
            if (arena == null || arena.Config == null)
            {
                return;
            }

            float depthLimit = arena.Config.FloorDepthUnits / arena.Config.CharacterWidth - DepthMargin;
            float anchor = Mathf.Clamp(preferredDepth, DepthMargin, depthLimit);

            for (float p = fromProgress; p <= toProgress + 0.01f; p += SampleStep)
            {
                // Тянемся к своей полосе на каждом срезе, а не только на первом.
                // Иначе маршруты разных болванок сходятся в общую щель через
                // пару шагов — и они снова упираются друг в друга.
                if (!TryFindLane(arena, floor, p, DepthMargin, depthLimit, obstacles, bodyRadius, anchor, out float depth))
                {
                    continue;
                }

                route.Add(arena.GetWorldPoint(floor, p, depth));
            }
        }

        /// <summary>
        /// Самая подходящая свободная полоса на срезе: из достаточно широких
        /// берётся ближайшая к прошлой, а не самая широкая. Иначе маршрут
        /// перекидывает болванку через весь этаж всякий раз, когда за укрытием
        /// открывается полоса пошире — и она идёт поперёк трассы, а не вперёд.
        /// </summary>
        private static bool TryFindLane(
            DuckHuntArena arena,
            int floor,
            float progress,
            float depthFrom,
            float depthTo,
            LayerMask obstacles,
            float bodyRadius,
            float preferredDepth,
            out float depth)
        {
            depth = 0f;

            float runStart = float.NaN;
            float bestScore = float.MaxValue;
            bool found = false;

            for (float z = depthFrom; z <= depthTo + 0.01f; z += DepthStep)
            {
                Vector3 point = arena.GetWorldPoint(floor, progress, z, ProbeHeight);
                bool blocked = Physics.CheckSphere(point, bodyRadius, obstacles, QueryTriggerInteraction.Ignore);

                if (!blocked)
                {
                    if (float.IsNaN(runStart))
                    {
                        runStart = z;
                    }

                    if (z + DepthStep <= depthTo + 0.01f)
                    {
                        continue;
                    }
                }

                if (float.IsNaN(runStart))
                {
                    continue;
                }

                float runEnd = blocked ? z - DepthStep : z;
                if (runEnd - runStart >= MinLaneWidth)
                {
                    float middle = (runStart + runEnd) * 0.5f;
                    float score = float.IsNaN(preferredDepth) ? 0f : Mathf.Abs(middle - preferredDepth);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        depth = middle;
                        found = true;
                    }
                }

                runStart = float.NaN;
            }

            return found;
        }
    }
}
