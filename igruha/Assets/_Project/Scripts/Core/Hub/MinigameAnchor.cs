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
    public sealed class MinigameAnchor : MonoBehaviour, IInteractable, ILocalInteraction
    {
        [Tooltip("Конфиг мини-игры. Пусто — якорь-заготовка на будущее")]
        [SerializeField] private MinigameDefinition definition;
        [Tooltip("Название предмета для подсказки, если игра ещё не назначена")]
        [SerializeField] private string anchorName = "Якорь";

        /// <summary>
        /// Кто нажал — важно: взаимодействие исполняется на сервере, поэтому нажатие
        /// клиента приходит в HubController хоста. Без игрока в событии хост не мог бы
        /// отличить своё нажатие от чужого и открывал бы окно подтверждения себе,
        /// когда кнопку жмёт кто-то другой.
        /// </summary>
        public event Action<MinigameAnchor, PlayerController> Activated;

        public MinigameDefinition Definition => definition;

        /// <summary>Готов к запуску: есть конфиг и имя сцены в Build Settings.</summary>
        public bool IsPlayable => definition != null && !string.IsNullOrEmpty(definition.SceneName);

        public string InteractionPrompt
        {
            get
            {
                if (!IsPlayable)
                {
                    return $"{anchorName} — скоро";
                }

                // На клиенте кнопка не сработает (запускает только хост) — честнее
                // сказать это сразу, чем оставить человека жать E в пустоту.
                return CanStartHere
                    ? $"E — играть: {definition.DisplayName}\nдальше Enter — подтвердить"
                    : $"{definition.DisplayName} — запускает хост";
            }
        }

        /// <summary>Эта машина вправе запустить мини-игру: одиночный тест или хост.</summary>
        private static bool CanStartHere
        {
            get
            {
                Unity.Netcode.NetworkManager network = Unity.Netcode.NetworkManager.Singleton;
                return network == null || !network.IsListening || network.IsServer;
            }
        }

        // Подсказку показываем и у незавершённых якорей, поэтому true всегда:
        // решение «запускать или нет» принимает HubController по IsPlayable.
        public bool CanInteract(PlayerController player) => true;

        public void Interact(PlayerController player) => Activated?.Invoke(this, player);
    }
}
