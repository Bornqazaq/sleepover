using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Player;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Кнопка активации ловушек (Duck Hunt, Камеры-ловушки, Рейс на память).
    /// Игрок жмёт Interact — срабатывают связанные ловушки против соперников.
    /// </summary>
    public sealed class TrapActivationButton : MonoBehaviour, IInteractable
    {
        [SerializeField] private string buttonName = "Кнопка";
        [Tooltip("Ловушки, которые запускает эта кнопка")]
        [SerializeField] private TrapBase[] traps;

        public string InteractionPrompt => $"Активировать: {buttonName}";

        public bool CanInteract(PlayerController player)
        {
            for (int i = 0; i < traps.Length; i++)
            {
                if (traps[i] != null && traps[i].IsReady)
                {
                    return true;
                }
            }

            return false;
        }

        public void Interact(PlayerController player)
        {
            for (int i = 0; i < traps.Length; i++)
            {
                if (traps[i] != null)
                {
                    traps[i].Activate();
                }
            }
        }
    }
}
