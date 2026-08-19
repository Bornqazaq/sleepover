using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Подъём лестничной комнаты: площадки по порядку снизу вверх.
    ///
    /// Нужен болванкам соло-теста. Живой игрок видит лестницу глазами и лезет
    /// по ней сам, а болванка умеет только идти к точке — и, оказавшись под
    /// подъёмом, встаёт, потому что до следующего этажа ей идти некуда:
    /// он ровно над ней. Список площадок превращает подъём в цепочку целей.
    ///
    /// Заполняется билдером арены, руками не расставляется.
    /// </summary>
    public sealed class DuckHuntStairs : MonoBehaviour
    {
        [Tooltip("Площадки подъёма по порядку снизу вверх. Заполняет билдер арены")]
        [SerializeField] private Transform[] steps = System.Array.Empty<Transform>();

        /// <summary>
        /// Насколько выше ног может быть верх площадки, чтобы она всё ещё
        /// считалась той, на которой стоим. Запас на дрожание физики: точного
        /// равенства высот у Rigidbody не бывает.
        /// </summary>
        private const float StandTolerance = 0.15f;

        /// <summary>
        /// Какую долю полуразмера площадки проходить к её дальнему краю.
        /// Ровно на край целиться нельзя: болванка встаёт на кромку, капсула
        /// проседает на округлом низу, и подъём до следующей ступени вырастает
        /// с 0.36 до 0.47 — выше порога всхождения 0.42, после чего подъём
        /// встаёт намертво. Доля меньше единицы держит её на площадке целиком,
        /// но всё ещё дальше радиуса «дошёл».
        /// </summary>
        private const float AimInsetFactor = 0.6f;

        public int StepCount => steps.Length;

        /// <summary>
        /// Куда идти тому, кто стоит на подъёме: следующая площадка цепочки,
        /// строго по порядку. Возвращает false, когда подъём пройден — дальше
        /// цель уже на следующем этаже.
        ///
        /// Порядок обязателен. Раньше выбиралась «ближайшая площадка достаточно
        /// выше ног», то есть через одну — иначе болванка считала себя прибывшей,
        /// ещё стоя на предыдущей. На прямом марше это работало, а на развороте
        /// между маршами прямая до цели через одну проходит мимо площадки
        /// разворота, над пустотой: болванка срезала угол и падала с лестницы.
        /// Соседняя площадка сама по себе ближе радиуса «дошёл», поэтому целимся
        /// не в её центр, а в дальний край — до него идти заведомо есть куда.
        /// </summary>
        public bool TryGetNextStep(Vector3 feetPosition, out Vector3 target)
        {
            target = Vector3.zero;

            int current = FindStandingStep(feetPosition);
            int next = current + 1;
            if (next >= steps.Length || steps[next] == null)
            {
                return false;
            }

            target = GetAimPoint(current, next);
            return true;
        }

        /// <summary>
        /// Площадка, на которой болванка стоит: самая высокая из тех, чей верх
        /// не выше ног. −1 — подъём ещё не начат, идём к самой нижней.
        ///
        /// Именно самая высокая, а не ближайшая по горизонтали. Площадка
        /// разворота между маршами широкая, и её центр оказывается ближе центра
        /// узкой ступени, на которой болванка реально стоит. По «ближайшей»
        /// текущей считалась площадка разворота, следующей — ступень, где
        /// болванка уже находится, и подъём вставал намертво: цель совпадала
        /// с местом.
        ///
        /// Высота выбирает верно и упавшего с середины марша: под его ногами
        /// окажется та ступень, куда он упал, и подъём продолжится с неё,
        /// а не с начала лестницы.
        /// </summary>
        private int FindStandingStep(Vector3 feetPosition)
        {
            int best = -1;
            float bestTop = float.MinValue;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] == null)
                {
                    continue;
                }

                float top = GetTopY(steps[i]);
                if (top > feetPosition.y + StandTolerance)
                {
                    continue;
                }

                float distance = Horizontal(steps[i].position - feetPosition).sqrMagnitude;

                // Одинаковой высоты бывают марш и площадка разворота на его верху —
                // там уже решает близость.
                bool higher = top > bestTop + 0.01f;
                bool sameHeightButCloser = top > bestTop - 0.01f && distance < bestDistance;
                if (!higher && !sameHeightButCloser)
                {
                    continue;
                }

                bestTop = top;
                bestDistance = distance;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// Точка на верхней грани следующей площадки, сдвинутая к её дальнему
        /// краю по ходу подъёма. Центр не годится: половина площадки — меньше
        /// радиуса «дошёл», и болванка останавливается, не сойдя с предыдущей.
        ///
        /// Сдвиг считается по направлению ДАЛЬНЕЙШЕГО подъёма, а не по тому,
        /// откуда пришли. Марш поворачивает: на площадке разворота приход идёт
        /// поперёк комнаты, а подъём продолжается вдоль неё. Толчок «вперёд по
        /// приходу» упирал болванку в торцевую стену лестничной комнаты и она
        /// стояла там до конца раунда.
        /// </summary>
        private Vector3 GetAimPoint(int current, int next)
        {
            Transform step = steps[next];
            Vector3 point = step.position;
            point.y = GetTopY(step);

            Vector3 course = GetClimbCourse(next);
            if (course.sqrMagnitude < 0.0001f && current >= 0 && steps[current] != null)
            {
                // Последняя площадка подъёма: дальше цепочки нет, идём по приходу.
                course = Horizontal(step.position - steps[current].position);
            }

            if (course.sqrMagnitude < 0.0001f)
            {
                return point;
            }

            course.Normalize();
            return point + course * (GetHalfExtentAlong(step, course) * AimInsetFactor);
        }

        /// <summary>Куда подъём идёт дальше этой площадки. Ноль — она последняя.</summary>
        private Vector3 GetClimbCourse(int index)
        {
            for (int i = index + 1; i < steps.Length; i++)
            {
                if (steps[i] != null)
                {
                    return Horizontal(steps[i].position - steps[index].position);
                }
            }

            return Vector3.zero;
        }

        /// <summary>Половина размера площадки вдоль направления подъёма.</summary>
        private static float GetHalfExtentAlong(Transform step, Vector3 course)
        {
            Vector3 extents = step.TryGetComponent(out Collider collider)
                ? collider.bounds.extents
                : Vector3.Scale(step.localScale, Vector3.one) * 0.5f;

            return Mathf.Abs(course.x) * extents.x + Mathf.Abs(course.z) * extents.z;
        }

        /// <summary>Верхняя грань площадки — то, по чему ходят.</summary>
        private static float GetTopY(Transform step) =>
            step.TryGetComponent(out Collider collider)
                ? collider.bounds.max.y
                : step.position.y + step.localScale.y * 0.5f;

        private static Vector3 Horizontal(Vector3 value)
        {
            value.y = 0f;
            return value;
        }
    }
}
