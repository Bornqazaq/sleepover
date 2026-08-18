using Igruha.Core.Player;

namespace Igruha.Core.Interaction
{
    /// <summary>
    /// Интерактив, который живёт всё удержание кнопки, а не срабатывает разово.
    /// Кнопка отсчёта в «Секундомере»: держишь — время идёт, отпустил — встало.
    ///
    /// Разовое <see cref="IInteractable.Interact"/> у таких объектов не зовётся:
    /// иначе одно нажатие означало бы сразу и «взял», и «начал держать».
    /// </summary>
    public interface IHoldInteractable : IInteractable
    {
        /// <summary>Удержание началось или кончилось. Единственная точка входа.</summary>
        void HoldChanged(PlayerController player, bool held);
    }
}
