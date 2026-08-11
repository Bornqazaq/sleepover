using Igruha.Core.Player;

namespace Igruha.Core.Interaction
{
    /// <summary>
    /// Объект, с которым игрок взаимодействует кнопкой Interact:
    /// якоря мини-игр в хабе, кнопки ловушек, предметы.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Текст подсказки «нажми E — …».</summary>
        string InteractionPrompt { get; }

        bool CanInteract(PlayerController player);

        void Interact(PlayerController player);
    }
}
