using System;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Player;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Кнопка активации ловушек (Duck Hunt, Камеры-ловушки, Рейс на память).
    /// Игрок жмёт Interact — срабатывают связанные ловушки против соперников.
    ///
    /// Кнопка показывает свою готовность: гаснет на кулдауне и загорается снова.
    /// Без этого нажатие — лотерея, потому что жмущий не знает, сработает ли,
    /// а ради нажатия он отклоняется от маршрута и подставляется под выстрел.
    /// </summary>
    public sealed class TrapActivationButton : MonoBehaviour, IInteractable
    {
        [SerializeField] private string buttonName = "Кнопка";
        [Tooltip("Ловушки, которые запускает эта кнопка")]
        [SerializeField] private TrapBase[] traps;

        [Header("Индикация готовности")]
        [Tooltip("Что перекрашивается. Пусто — кнопка работает, но выглядит одинаково в любом состоянии")]
        [SerializeField] private Renderer indicator;
        [Tooltip("Материал готовой кнопки")]
        [SerializeField] private Material readyMaterial;
        [Tooltip("Материал кнопки на перезарядке")]
        [SerializeField] private Material cooldownMaterial;

        /// <summary>Кнопка снова готова (true) или ушла на перезарядку (false).</summary>
        public event Action<bool> ReadyChanged;

        private bool isReady = true;

        /// <summary>Хотя бы одна связанная ловушка готова сработать.</summary>
        public bool IsReady => isReady;

        public string InteractionPrompt => $"Активировать: {buttonName}";

        private void OnEnable()
        {
            for (int i = 0; i < traps.Length; i++)
            {
                if (traps[i] != null)
                {
                    traps[i].ReadyChanged += HandleTrapReadyChanged;
                }
            }

            // Состояние берём у ловушек, а не считаем кнопку готовой по умолчанию:
            // сцену могли включить с ловушкой, уже ушедшей на кулдаун.
            isReady = !EvaluateReady();
            RefreshReady();
        }

        private void OnDisable()
        {
            for (int i = 0; i < traps.Length; i++)
            {
                if (traps[i] != null)
                {
                    traps[i].ReadyChanged -= HandleTrapReadyChanged;
                }
            }
        }

        private void HandleTrapReadyChanged(bool _) => RefreshReady();

        private void RefreshReady()
        {
            bool ready = EvaluateReady();
            if (ready == isReady)
            {
                return;
            }

            isReady = ready;
            ApplyIndicator(ready);
            ReadyChanged?.Invoke(ready);
        }

        private bool EvaluateReady()
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

        private void ApplyIndicator(bool ready)
        {
            Material material = ready ? readyMaterial : cooldownMaterial;
            if (indicator != null && material != null)
            {
                indicator.sharedMaterial = material;
            }
        }

        public bool CanInteract(PlayerController player) => IsReady;

        public void Interact(PlayerController player)
        {
            for (int i = 0; i < traps.Length; i++)
            {
                if (traps[i] != null)
                {
                    traps[i].Activate();
                }
            }

            // Ловушка могла и не уйти на кулдаун (её кто-то опередил в этом же
            // кадре) — состояние кнопки перечитываем, а не выставляем вслепую.
            RefreshReady();
        }
    }
}
