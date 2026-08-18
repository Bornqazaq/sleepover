using System;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>
    /// Красная кнопка на тумбе — единственное действие игрока во всей игре.
    /// Первое нажатие запускает отсчёт, второе останавливает, третье и дальше
    /// не проходят.
    ///
    /// Вокруг этой кнопки собрана вся честность игры: свой результат нельзя
    /// увидеть в момент нажатия, а чужой — подсмотреть по соседней клетке.
    /// Поэтому подсветка чужой кнопки загорается на «старт» и держится
    /// до конца стадии отмера, гаснув у всех одновременно: видно, кто начал,
    /// но не видно, кто и когда остановился. Гаснущая на «стоп» подсветка
    /// была бы прямым читом на типе «Потолок» — достаточно остановиться
    /// на полсекунды раньше того, у кого уже погасло.
    ///
    /// Сама кнопка ничего не ранжирует: она отдаёт наверх замер интервала,
    /// а что он значит, решают правила игры.
    /// </summary>
    public sealed class CageButton : MonoBehaviour, IInteractable
    {
        public enum ButtonState
        {
            /// <summary>Отсчёт не запускали.</summary>
            Idle,
            /// <summary>Отсчёт идёт.</summary>
            Running,
            /// <summary>Отсчёт остановлен, в этом подраунде кнопка больше не работает.</summary>
            Stopped
        }

        [Tooltip("Лампа кнопки — ей меняется цвет")]
        [SerializeField] private Renderer lamp;
        [Tooltip("Цвет погашенной кнопки")]
        [SerializeField] private Color idleColor = new Color(0.35f, 0.05f, 0.05f);
        [Tooltip("Цвет своей кнопки, пока идёт отсчёт")]
        [SerializeField] private Color ownRunningColor = new Color(1f, 0.15f, 0.1f);
        [Tooltip("Цвет чужой кнопки, пока её хозяин отсчитывает")]
        [SerializeField] private Color neighbourRunningColor = new Color(0.9f, 0.45f, 0.1f);
        [Tooltip("Частота пульсации своей кнопки, Гц")]
        [SerializeField] private float pulseSpeed = 3f;
        [Tooltip("Глубина пульсации, 0..1")]
        [SerializeField] private float pulseAmount = 0.3f;

        /// <summary>Хозяин кнопки нажал «старт».</summary>
        public event Action<CageButton> Started;

        /// <summary>Хозяин нажал «стоп». Второй аргумент — отмеренный интервал, с.</summary>
        public event Action<CageButton, float> Stopped;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock block;
        private PlayerController owner;
        private double startedAt;
        private float measured;

        /// <summary>Открыта ли стадия отмера. Ставит контроллер игры.</summary>
        public bool WindowOpen { get; private set; }

        /// <summary>Показывать ли подсветку чужих кнопок. Флаг плейтеста neighbourButtonLights.</summary>
        public bool NeighbourLightsEnabled { get; set; } = true;

        /// <summary>Видит ли эту кнопку её хозяин. У чужих клеток — false.</summary>
        public bool ViewedByOwner { get; set; }

        public ButtonState State { get; private set; } = ButtonState.Idle;

        /// <summary>Отмеренный интервал последнего подраунда, с. До «стопа» — ноль.</summary>
        public float Measured => measured;

        /// <summary>Успел ли хозяин закрыть отсчёт в этом подраунде.</summary>
        public bool Completed => State == ButtonState.Stopped;

        public string InteractionPrompt => State == ButtonState.Idle ? "Запустить отсчёт" : "Остановить отсчёт";

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            ApplyColor(idleColor);
        }

        /// <summary>Кому принадлежит кнопка. Чужие нажать её не могут.</summary>
        public void SetOwner(PlayerController player)
        {
            owner = player;
        }

        /// <summary>
        /// Открыть окно отмера: кнопка снова с нуля. Зовётся на старте стадии
        /// отмера каждого подраунда.
        /// </summary>
        public void OpenWindow()
        {
            WindowOpen = true;
            State = ButtonState.Idle;
            measured = 0f;
            startedAt = 0d;
            ApplyColor(idleColor);
        }

        /// <summary>
        /// Закрыть окно. Подсветка гаснет у всех одновременно — в этом и смысл:
        /// момент «стопа» соседа так и остаётся невидимым.
        /// </summary>
        public void CloseWindow()
        {
            WindowOpen = false;
            ApplyColor(idleColor);
        }

        public bool CanInteract(PlayerController player)
        {
            return WindowOpen && State != ButtonState.Stopped && player != null && player == owner;
        }

        public void Interact(PlayerController player)
        {
            if (!CanInteract(player))
            {
                return;
            }

            if (State == ButtonState.Idle)
            {
                State = ButtonState.Running;
                startedAt = NetworkClock.Now;
                Started?.Invoke(this);
                return;
            }

            State = ButtonState.Stopped;
            measured = (float)(NetworkClock.Now - startedAt);
            Stopped?.Invoke(this, measured);
        }

        /// <summary>
        /// Сколько идёт отсчёт прямо сейчас. Наружу отдаётся только под флагом
        /// debugShowTimer: увидеть это в игре — значит увидеть свой результат
        /// до стадии показа, а на этом держится весь смысл игры.
        /// </summary>
        public float DebugElapsed => State == ButtonState.Running ? (float)(NetworkClock.Now - startedAt) : measured;

        private void Update()
        {
            if (!WindowOpen)
            {
                return;
            }

            // Пульсирует только своя кнопка и только пока идёт отсчёт: это
            // единственный признак «мой отсчёт пошёл», других в игре нет.
            if (ViewedByOwner)
            {
                if (State == ButtonState.Running)
                {
                    float pulse = 1f - pulseAmount * 0.5f * (1f + Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f));
                    ApplyColor(ownRunningColor * pulse);
                }

                return;
            }

            if (!NeighbourLightsEnabled)
            {
                return;
            }

            // Чужая горит ровно и не гаснет на «стоп».
            ApplyColor(State == ButtonState.Idle ? idleColor : neighbourRunningColor);
        }

        private void ApplyColor(Color color)
        {
            if (lamp == null)
            {
                return;
            }

            lamp.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            lamp.SetPropertyBlock(block);
        }
    }
}
