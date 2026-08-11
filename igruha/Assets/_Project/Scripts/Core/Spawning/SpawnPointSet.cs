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

            return rolePoints[index % rolePoints.Count];
        }
    }
}
