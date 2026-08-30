using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Маршрут команды от штабеля к баку: доска через первую пропасть,
    /// горлышко, доска через вторую. Только для болванок соло-теста.
    ///
    /// Нужен потому, что обход препятствий у <c>DebugPlayerBot</c> местный:
    /// щуп смотрит вперёд на полтора метра и видит только то, во что можно
    /// упереться. Восьмиметровый завал он развернуть не может, а <b>пропасть
    /// не видит вовсе</b> — там нет геометрии, щупу не во что упереться, и
    /// болванка уходит в пустоту вместе с тарой. Оба случая пойманы на соло-
    /// прогонах 27.08.
    ///
    /// Живому игроку маршрут не нужен: он видит и доску, и проход.
    ///
    /// Точки идут строго <b>от штабеля к баку</b>. Обратный путь читается тем
    /// же списком с конца — ходка челночная, и второго списка не требуется.
    /// </summary>
    public sealed class CarryItemBotRoute : MonoBehaviour
    {
        [Tooltip("Путевые точки по порядку: от штабеля к баку")]
        [SerializeField] private Transform[] waypoints = System.Array.Empty<Transform>();

        public int Count => waypoints.Length;

        /// <summary>Задать точки из сборки арены. Порядок — от штабеля к баку.</summary>
        public void SetWaypoints(Transform[] points) => waypoints = points;

        /// <summary>
        /// Следующая точка пути от <paramref name="from"/> к
        /// <paramref name="to"/>. <paramref name="isFinal"/> — это уже сама
        /// цель, на ней можно останавливаться.
        ///
        /// Направление читается по оси маршрута: вперёд — к баку, назад — к
        /// штабелю. Ворота, оставшиеся позади, пропускаются.
        /// </summary>
        public Vector3 NextPoint(Vector3 from, Vector3 to, out bool isFinal)
        {
            isFinal = true;

            if (waypoints == null || waypoints.Length == 0)
            {
                return to;
            }

            bool forward = to.x > from.x;

            for (int i = 0; i < waypoints.Length; i++)
            {
                Transform gate = waypoints[forward ? i : waypoints.Length - 1 - i];
                if (gate == null)
                {
                    continue;
                }

                float gateX = gate.position.x;

                // Ворота позади по ходу движения — уже пройдены.
                //
                // Строгое сравнение, без допуска, и это важно в обе стороны.
                // Допуск «ещё не дошёл» выкидывает первую точку маршрута, если
                // до неё остался метр, — болванка идёт по диагонали ко второй,
                // то есть мимо доски в пропасть. Допуск «уже прошёл» на
                // обратном пути наоборот тянет её вперёд, к воротам за спиной,
                // и она топчется между ними и штабелем. Оба случая пойманы на
                // соло-прогонах 27.08.
                if (forward ? gateX <= from.x : gateX >= from.x)
                {
                    continue;
                }

                // Ворота за целью — цель ближе, идём прямо к ней.
                if (forward ? gateX > to.x : gateX < to.x)
                {
                    break;
                }

                isFinal = false;
                return gate.position;
            }

            return to;
        }

        private void OnDrawGizmosSelected()
        {
            if (waypoints == null)
            {
                return;
            }

            Gizmos.color = Color.yellow;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null)
                {
                    continue;
                }

                Gizmos.DrawWireSphere(waypoints[i].position, 0.5f);

                if (i + 1 < waypoints.Length && waypoints[i + 1] != null)
                {
                    Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
                }
            }
        }
    }
}
