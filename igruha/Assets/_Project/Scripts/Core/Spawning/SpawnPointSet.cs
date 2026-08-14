using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Spawning
{
    /// <summary>
    /// Набор точек спавна арены (2–8 игроков + ролевые). Точки — дети этого
    /// объекта. Количество игроков нигде не хардкодится: точек может быть
    /// больше, чем игроков.
    /// </summary>
    public sealed class SpawnPointSet : MonoBehaviour
    {
        private readonly List<SpawnPoint> points = new List<SpawnPoint>(16);
        private readonly List<SpawnPoint> buffer = new List<SpawnPoint>(16);

        private void Awake()
        {
            Collect();
        }

        private void Collect()
        {
            points.Clear();
            GetComponentsInChildren(true, points);
        }

        /// <summary>Точки заданной роли. Возвращает внутренний буфер — не кэшировать между кадрами.</summary>
        public IReadOnlyList<SpawnPoint> GetPoints(SpawnRole role)
        {
            if (points.Count == 0)
            {
                Collect();
            }

            buffer.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].Role == role)
                {
                    buffer.Add(points[i]);
                }
            }

            return buffer;
        }

        /// <summary>i-я точка роли (по кругу, если игроков больше, чем точек).</summary>
        public SpawnPoint GetPoint(SpawnRole role, int index)
        {
            IReadOnlyList<SpawnPoint> rolePoints = GetPoints(role);
            if (rolePoints.Count == 0)
            {
                Debug.LogWarning($"{name}: нет точек спавна роли {role}", this);
                return null;
            }

            return rolePoints[Wrap(index, rolePoints.Count)];
        }

        /// <summary>
        /// i-я из count точек роли с максимальным разносом по кольцу.
        /// Подряд идущие точки при неполном лобби ставят всех в один угол —
        /// на круглой арене это отдаёт всю толпу в один сектор обзора ведущего.
        ///
        /// Слот считается как index × точек / count, а не по целочисленному шагу:
        /// шаг 8/3 = 2 дал бы 0, 2, 4 и оставил полкольца пустым, а эта формула —
        /// 0, 2, 5, то есть настоящий разнос. Результат детерминирован, поэтому
        /// сервер и клиент при одинаковых входах получают одни и те же точки.
        /// </summary>
        public SpawnPoint GetSpreadPoint(SpawnRole role, int index, int count)
        {
            IReadOnlyList<SpawnPoint> rolePoints = GetPoints(role);
            if (rolePoints.Count == 0)
            {
                Debug.LogWarning($"{name}: нет точек спавна роли {role}", this);
                return null;
            }

            if (count <= 0)
            {
                Debug.LogWarning($"{name}: разнос спавнов запрошен на {count} игроков — точки раздаются подряд", this);
                return rolePoints[Wrap(index, rolePoints.Count)];
            }

            // Игроков не меньше, чем точек — разносить нечего, раздаём подряд по кругу.
            int slot = count >= rolePoints.Count
                ? index
                : index * rolePoints.Count / count;

            return rolePoints[Wrap(slot, rolePoints.Count)];
        }

        private static int Wrap(int value, int length) => ((value % length) + length) % length;
    }
}
