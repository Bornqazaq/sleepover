using UnityEngine;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Зона касания Водящего: радиус зачёта, проверка попадания в него и
    /// рисование этого радиуса в редакторе — в одном месте.
    ///
    /// Не физический триггер, хотя напрашивается. Спека требует, чтобы два
    /// касания в один тик разбирались в порядке обхода списка Бегущих: только
    /// так порядок мест выходит одинаковым на всех машинах. Триггеры приходят
    /// в порядке физического движка, который такой гарантии не даёт. Поэтому
    /// зона лишь отвечает на вопрос «этот внутри?», а спрашивает её серверный
    /// тик мини-игры, перебирая Бегущих по списку.
    ///
    /// Расстояние меряется по горизонтали: Водящий стоит на постаменте, и
    /// Бегущий, запрыгнувший к нему, отличается по высоте на полметра —
    /// сферическая проверка засчитывала бы касание неодинаково с земли и
    /// с постамента.
    /// </summary>
    public sealed class KeeperTouchZone : MonoBehaviour
    {
        private const int GizmoSegments = 32;

        private float radius;

        /// <summary>Радиус засчитываемого касания, юниты. Ноль — зона молчит.</summary>
        public float Radius => radius;

        /// <summary>Радиус приходит из конфига мини-игры, а не из инспектора: зона живёт только в раунде.</summary>
        public void Configure(float touchRadius) => radius = Mathf.Max(0f, touchRadius);

        /// <summary>Точка внутри зоны касания. Высота не учитывается.</summary>
        public bool Contains(Vector3 worldPoint)
        {
            if (radius <= 0f)
            {
                return false;
            }

            Vector3 offset = worldPoint - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= radius * radius;
        }

        private void OnDrawGizmosSelected()
        {
            if (radius <= 0f)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            Vector3 center = transform.position;
            Vector3 previous = center + new Vector3(radius, 0f, 0f);

            for (int i = 1; i <= GizmoSegments; i++)
            {
                float angle = Mathf.PI * 2f * i / GizmoSegments;
                Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, point);
                previous = point;
            }
        }
    }
}
