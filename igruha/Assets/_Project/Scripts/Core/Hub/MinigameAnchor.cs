using System;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Core.Hub
{
    /// <summary>
    /// Предмет-якорь мини-игры в хабе (GDD 2): подошёл → подсказка →
    /// подтверждение → загрузка. Якорь без назначенной игры остаётся
    /// декорацией с подсказкой «скоро» — задел на остальные 14 игр.
    /// </summary>
    public sealed class MinigameAnchor : MonoBehaviour, IInteractable
    {
        [Tooltip("Конфиг мини-игры. Пусто — якорь-заготовка на будущее")]
        [SerializeField] private MinigameDefinition definition;
        [Tooltip("Название предмета для подсказки, если игра ещё не назначена")]
        [SerializeField] private string anchorName = "Якорь";

        public event Action<MinigameAnchor> Activated;

        public MinigameDefinition Definition => definition;

        /// <summary>Готов к запуску: есть конфиг и адрес сцены для Addressables.</summary>
        public bool IsPlayable => definition != null && !string.IsNullOrEmpty(definition.SceneAddress);

        public string InteractionPrompt => IsPlayable
            ? $"E — играть: {definition.DisplayName}"
            : $"{anchorName} — скоро";

        // Подсказку показываем и у незавершённых якорей, поэтому true всегда:
        // решение «запускать или нет» принимает HubController по IsPlayable.
        public bool CanInteract(PlayerController player) => true;

        public void Interact(PlayerController player) => Activated?.Invoke(this);
    }
}
