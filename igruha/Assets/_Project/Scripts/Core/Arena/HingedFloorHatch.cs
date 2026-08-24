using System;
using System.Collections;
using UnityEngine;

namespace Igruha.Core.Arena
{
    /// <summary>
    /// Пол на двух створках: половины сидят на петлях по внешним краям
    /// и распахиваются вниз, роняя всех, кто стоял сверху.
    ///
    /// Компонент не знает, что именно он держит и почему открывается. Клетка
    /// цирка распахивает им дно, платформа «Экзамена» — неверный вариант
    /// ответа. Решение принимает игра, здесь только механика.
    ///
    /// <b>Створки поворачиваются, а не исчезают.</b> Это принципиально другой
    /// эффект, чем у <c>CollapsingFloorTrap</c>, который просто убирает
    /// коллайдеры: там пол пропадает, здесь — раскрывается люк.
    ///
    /// Состояние меняется единственной парой методов — в сетевой фазе они же
    /// становятся точкой применения реплицированного состояния.
    /// </summary>
    public sealed class HingedFloorHatch : MonoBehaviour
    {
        [Tooltip("Левая створка: петля по внешнему краю, распахивается вниз")]
        [SerializeField] private Transform doorLeft;
        [Tooltip("Правая створка: петля по внешнему краю, распахивается вниз")]
        [SerializeField] private Transform doorRight;
        [Tooltip("На сколько градусов распахиваются створки")]
        [SerializeField] private float doorOpenAngle = 110f;
        [Tooltip("С какого угла створки перестают держать игрока. Раньше — он съезжает по наклонной и его подбрасывает")]
        [SerializeField] private float doorReleaseAngle = 25f;

        /// <summary>
        /// Створки наклонились достаточно, чтобы отпустить стоявшего: с этого
        /// момента он падает. Именно здесь, а не в конце анимации — падение
        /// начинается раньше, чем створки договорят.
        /// </summary>
        public event Action<HingedFloorHatch> Released;

        /// <summary>Створки распахнуты или в процессе раскрытия.</summary>
        public bool DoorsOpen { get; private set; }

        private Collider[] doorColliders = Array.Empty<Collider>();
        private Coroutine swingRoutine;

        private void Awake() => CacheDoorColliders();

        /// <summary>
        /// Собрать коллайдеры обеих створок. Это и есть опора: снимаем их —
        /// стоявший проваливается сам, без переноса и телепорта.
        /// </summary>
        private void CacheDoorColliders()
        {
            var left = doorLeft != null ? doorLeft.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
            var right = doorRight != null ? doorRight.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
            doorColliders = new Collider[left.Length + right.Length];
            left.CopyTo(doorColliders, 0);
            right.CopyTo(doorColliders, left.Length);
        }

        /// <summary>
        /// Распахнуть пол за <paramref name="duration"/> секунд. Повторный
        /// вызов на уже открытых створках игнорируется — иначе анимация
        /// перезапустилась бы с нуля и пассажир повис бы в воздухе.
        /// </summary>
        public void OpenDoors(float duration)
        {
            if (DoorsOpen)
            {
                return;
            }

            DoorsOpen = true;

            // На выключенном объекте корутины не бывает: Unity отбивает
            // StartCoroutine с ошибкой, флаг остался бы поднятым, а створки
            // закрытыми — состояние разъехалось бы с картинкой. Такое приходит
            // из сети, когда реплицированное «открыто» догоняет клетку,
            // выключенную по числу игроков. Применяем конец анимации сразу.
            if (!isActiveAndEnabled)
            {
                SetDoorAngle(doorOpenAngle);
                SetDoorCollidersEnabled(false);
                Released?.Invoke(this);
                return;
            }

            if (swingRoutine != null)
            {
                StopCoroutine(swingRoutine);
            }

            swingRoutine = StartCoroutine(SwingDoors(duration));
        }

        /// <summary>Вернуть пол на место и снова сделать его опорой.</summary>
        public void CloseDoors()
        {
            if (swingRoutine != null)
            {
                StopCoroutine(swingRoutine);
                swingRoutine = null;
            }

            DoorsOpen = false;
            SetDoorAngle(0f);
            SetDoorCollidersEnabled(true);
        }

        private IEnumerator SwingDoors(float duration)
        {
            float elapsed = 0f;
            float span = Mathf.Max(0.01f, duration);
            bool released = false;

            while (elapsed < span)
            {
                elapsed += Time.deltaTime;
                float angle = Mathf.Lerp(0f, doorOpenAngle, elapsed / span);
                SetDoorAngle(angle);

                // Коллайдеры снимаются, как только створки заметно наклонились:
                // дальше игрок съезжал бы по наклонной плоскости, и вращающийся
                // коллайдер подбрасывал бы его вбок вместо падения вниз.
                if (!released && angle >= doorReleaseAngle)
                {
                    released = true;
                    SetDoorCollidersEnabled(false);
                    Released?.Invoke(this);
                }

                yield return null;
            }

            SetDoorAngle(doorOpenAngle);

            // Угол отпускания могли задать больше угла раскрытия — тогда
            // цикл выше не сработает ни разу, и без этой ветки створки
            // остались бы держать игрока распахнутыми.
            if (!released)
            {
                SetDoorCollidersEnabled(false);
                Released?.Invoke(this);
            }

            swingRoutine = null;
        }

        private void SetDoorAngle(float angle)
        {
            if (doorLeft != null)
            {
                doorLeft.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }

            if (doorRight != null)
            {
                doorRight.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private void SetDoorCollidersEnabled(bool value)
        {
            for (int i = 0; i < doorColliders.Length; i++)
            {
                if (doorColliders[i] != null)
                {
                    doorColliders[i].enabled = value;
                }
            }
        }
    }
}
