using Igruha.Core.Player;

namespace Igruha.Core.Interaction
{
    /// <summary>
    /// Объект, с которым игрок взаимодействует кнопкой Interact:
    /// якоря мини-игр в хабе, кнопки ловушек, предметы.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>
        /// Готовая подсказка с клавишей и способом ввода (InteractionPromptText).
        /// Обычный Interact требует удержания E; HUD не добавляет клавишу повторно.
        /// Специальный ввод, например ЛКМ у полки, указывается самим объектом.
        /// </summary>
        string InteractionPrompt { get; }

        bool CanInteract(PlayerController player);

        void Interact(PlayerController player);
    }
}
