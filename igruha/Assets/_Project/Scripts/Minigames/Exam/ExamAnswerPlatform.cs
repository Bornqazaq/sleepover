using UnityEngine;
using Igruha.Core.Arena;

namespace Igruha.Minigames.Exam
{
    /// <summary>
    /// Платформа одного варианта ответа. Пол на створках: когда вариант
    /// оказывается неверным, дно распахивается и стоявшие улетают в яму.
    ///
    /// Про правила игры не знает ничего — ей говорят «раскройся».
    /// Механику створок держит общий <see cref="HingedFloorHatch"/> из Core:
    /// та же деталь служит клеткам цирка.
    /// </summary>
    [RequireComponent(typeof(HingedFloorHatch))]
    public sealed class ExamAnswerPlatform : MonoBehaviour
    {
        [Tooltip("Какому варианту соответствует платформа")]
        [SerializeField] private ExamSide side = ExamSide.A;
        [Tooltip("Размер площадки в метрах: ширина по X, глубина по Z")]
        [SerializeField] private Vector2 size = new Vector2(8.64f, 7.2f);
        [Tooltip("На сколько метров над полом платформы игрок ещё считается стоящим на ней")]
        [SerializeField] private float standingHeight = 4f;

        private HingedFloorHatch hatch;

        public ExamSide Side => side;

        public bool DoorsOpen => hatch != null && hatch.DoorsOpen;

        private void Awake() => hatch = GetComponent<HingedFloorHatch>();

        /// <summary>Распахнуть дно. Единственная точка — в фазе 3 сюда придёт решение сервера.</summary>
        public void OpenDoors(float duration)
        {
            if (hatch == null)
            {
                Debug.LogError($"{name}: нет HingedFloorHatch — платформа не раскроется", this);
                return;
            }

            hatch.OpenDoors(duration);
        }

        /// <summary>Вернуть пол на место к следующему вопросу.</summary>
        public void CloseDoors() => hatch?.CloseDoors();

        /// <summary>
        /// Стоит ли точка на этой платформе.
        ///
        /// Считаем по <b>центру коллайдера</b>, а не по касанию: игрок,
        /// зависший центром над зазором между платформами, не принадлежит
        /// ни одной из них и очков не получает (спека 5.6).
        ///
        /// Вертикаль тоже проверяется: прыжок над платформой засчитывается,
        /// а падение в яму под ней — уже нет.
        /// </summary>
        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);

            if (Mathf.Abs(local.x) > size.x * 0.5f || Mathf.Abs(local.z) > size.y * 0.5f)
            {
                return false;
            }

            return local.y >= -0.5f && local.y <= standingHeight;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = side == ExamSide.A ? Color.cyan : Color.yellow;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, standingHeight * 0.5f, 0f),
                new Vector3(size.x, standingHeight, size.y));
        }
#endif
    }
}
