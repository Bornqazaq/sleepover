using UnityEngine;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Ручка рычага: перекинута, пока ловушка в сработавшем положении или на
    /// перезарядке, и поднята, когда всё вернулось в исходное.
    ///
    /// Состояние берётся у самой ловушки, а не у кнопки. Кнопка отвечает на
    /// вопрос «можно ли жать», а он не совпадает со «сработало ли»:
    /// дверь-переключатель нажимается в любой момент, но створка при этом то
    /// закрыта, то открыта — показывать надо именно её. Одно условие
    /// покрывает оба вида ловушек: у двери и провала есть состояние, у
    /// гейзера состояния нет вовсе, зато есть перезарядка.
    ///
    /// Опрос двух флагов в кадре, а не подписка: события, покрывающего оба
    /// случая, у ловушки нет, а читать два бита дешевле, чем заводить его.
    /// </summary>
    public sealed class TrapLever : MonoBehaviour
    {
        [Tooltip("Ловушка, состояние которой показывает ручка")]
        [SerializeField] private TrapBase trap;

        [Tooltip("Что поворачивается. Ось поворота — начало координат этого объекта, то есть точка крепления к плите")]
        [SerializeField] private Transform handle;

        [Tooltip("Углы ручки в исходном положении")]
        [SerializeField] private Vector3 readyEuler;

        [Tooltip("Углы ручки в перекинутом положении")]
        [SerializeField] private Vector3 firedEuler;

        [Tooltip("За сколько секунд ручка проходит путь целиком, с")]
        [SerializeField] private float flipDuration = 0.12f;

        private Quaternion origin;
        private Quaternion target;
        private float progress = 1f;
        private bool flipped;

        /// <summary>Ручка должна быть перекинута: ловушка сработала или ещё не остыла.</summary>
        private bool ShouldFlip => trap != null && (trap.IsSprung || !trap.IsReady);

        private void OnEnable()
        {
            // Стартовое положение ставим рывком: ловушку могли заранее
            // привести в сработавшее состояние — например, присланным
            // по сети состоянием раунда.
            flipped = ShouldFlip;
            origin = target = Angles(flipped);
            progress = 1f;
            ApplyRotation();
        }

        private void Update()
        {
            bool wanted = ShouldFlip;
            if (wanted != flipped)
            {
                flipped = wanted;
                origin = handle != null ? handle.localRotation : target;
                target = Angles(wanted);
                progress = flipDuration > 0f ? 0f : 1f;
            }

            if (progress >= 1f)
            {
                return;
            }

            progress = Mathf.Min(1f, progress + Time.deltaTime / flipDuration);
            ApplyRotation();
        }

        private void ApplyRotation()
        {
            if (handle != null)
            {
                handle.localRotation = Quaternion.Slerp(origin, target, progress);
            }
        }

        private Quaternion Angles(bool isFlipped) => Quaternion.Euler(isFlipped ? firedEuler : readyEuler);
    }
}
