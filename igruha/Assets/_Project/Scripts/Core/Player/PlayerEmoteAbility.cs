using System;
using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Эмоции-насмешки: зажал Tab — открылось круглое меню, повёл мышью —
    /// выбрал сектор, отпустил Tab — выбранный танец пошёл в луп, пока игрок
    /// не сделает что-то ещё (за прерывание отвечает Animator Controller).
    /// Здесь только логика выбора; отрисовка колеса — EmoteWheel в слое UI,
    /// он читает состояние отсюда. Проигрывание клипа — CharacterAnimatorDriver.
    /// Персонаж без списка эмоций колесо не открывает вовсе.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerEmoteAbility : MonoBehaviour
    {
        /// <summary>Секторов в колесе ровно восемь — под них рассчитана и раскладка UI, и Animator.</summary>
        public const int SectorCount = 8;
        /// <summary>Ноль — «эмоция не выбрана»: и в этом коде, и в параметре Emote аниматора.</summary>
        public const int NoEmote = 0;

        [SerializeField] private PlayerInputReader inputReader;
        [Tooltip("Названия эмоций по секторам, начиная с верхнего и дальше по часовой стрелке (до 8). Пустая строка — сектор занят, но клипа ещё нет; пустой массив — у персонажа нет эмоций вообще")]
        [SerializeField] private string[] emoteNames = Array.Empty<string>();

        [Header("Курсор колеса")]
        [Tooltip("Насколько быстро курсор колеса идёт за мышью: единиц курсора на пиксель смещения")]
        [SerializeField] private float pointerSensitivity = 0.012f;
        [Tooltip("Радиус мёртвой зоны в центре (0..1): пока курсор в ней, ничего не выбрано и отпускание Tab отменяет эмоцию")]
        [Range(0.05f, 0.9f)]
        [SerializeField] private float deadZone = 0.35f;

        /// <summary>Колесо открылось — UI показывает панель.</summary>
        public event Action WheelOpened;
        /// <summary>Колесо закрылось — UI прячет панель.</summary>
        public event Action WheelClosed;
        /// <summary>Выбрана эмоция 1..8 — визуал уходит в зацикленный танец.</summary>
        public event Action<int> EmotePlayed;
        /// <summary>Эмоция снята (отпустил в мёртвой зоне) — визуал возвращается в Idle.</summary>
        public event Action EmoteStopped;

        public IReadOnlyList<string> EmoteNames => emoteNames;
        public bool HasEmotes => emoteNames.Length > 0;
        public bool IsWheelOpen { get; private set; }
        /// <summary>Положение курсора колеса от центра, длина 0..1 — для отрисовки.</summary>
        public Vector2 Pointer { get; private set; }
        /// <summary>Сектор под курсором: 1..8 либо NoEmote, пока курсор в мёртвой зоне.</summary>
        public int HoveredEmote { get; private set; }
        /// <summary>Эмоция, играющая прямо сейчас (1..8), либо NoEmote.</summary>
        public int ActiveEmote { get; private set; }

        private PlayerController motor;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            motor.KnockdownStarted += OnKnockdownStarted;
        }

        private void OnDisable()
        {
            motor.KnockdownStarted -= OnKnockdownStarted;
            if (IsWheelOpen)
            {
                CloseWheel();
            }
        }

        private void Update()
        {
            if (inputReader == null)
            {
                return;
            }

            // Заблокированный не танцует: в «Ангелах» замороженный обязан стоять
            // статуей, и пляшущая статуя ломает всю затею. Нокдаун глушит по той же
            // причине — персонаж в этот момент не управляется.
            bool wantsWheel = inputReader.EmoteHeld && HasEmotes
                              && !motor.IsKnockedDown && !motor.MovementLocked;

            if (wantsWheel && !IsWheelOpen)
            {
                OpenWheel();
            }
            else if (!wantsWheel && IsWheelOpen)
            {
                ApplySelection();
                CloseWheel();
            }

            if (IsWheelOpen)
            {
                MovePointer(inputReader.LookDelta);
            }

            // Пошёл — танец кончился: Animator уводит в Run по Speed, а здесь
            // снимается флаг, иначе колесо будет считать эмоцию всё ещё активной.
            if (ActiveEmote != NoEmote && motor.NormalizedSpeed > 0.1f)
            {
                ActiveEmote = NoEmote;
            }
        }

        private void OpenWheel()
        {
            IsWheelOpen = true;
            Pointer = Vector2.zero;
            HoveredEmote = NoEmote;
            WheelOpened?.Invoke();
        }

        private void CloseWheel()
        {
            IsWheelOpen = false;
            Pointer = Vector2.zero;
            HoveredEmote = NoEmote;
            WheelClosed?.Invoke();
        }

        private void MovePointer(Vector2 lookDelta)
        {
            Pointer = Vector2.ClampMagnitude(Pointer + lookDelta * pointerSensitivity, 1f);
            HoveredEmote = SectorAt(Pointer);
        }

        /// <summary>
        /// Сектор под курсором. Верхний сектор — первый, дальше по часовой стрелке;
        /// atan2 от (x; y) даёт угол против часовой от оси X, поэтому меряем от оси Y
        /// и в обратную сторону — так номера совпадают с раскладкой колеса в UI.
        /// </summary>
        private int SectorAt(Vector2 pointer)
        {
            if (pointer.magnitude < deadZone)
            {
                return NoEmote;
            }

            float degrees = Mathf.Atan2(pointer.x, pointer.y) * Mathf.Rad2Deg;
            float sectorSize = 360f / SectorCount;
            int index = Mathf.RoundToInt(Mathf.Repeat(degrees, 360f) / sectorSize) % SectorCount;
            return index + 1;
        }

        /// <summary>Отпустили Tab: сектор с клипом — играем, мёртвая зона или пустой сектор — снимаем эмоцию.</summary>
        private void ApplySelection()
        {
            int selected = HoveredEmote;

            if (selected == NoEmote || !IsEmoteAvailable(selected))
            {
                ActiveEmote = NoEmote;
                EmoteStopped?.Invoke();
                return;
            }

            ActiveEmote = selected;
            EmotePlayed?.Invoke(selected);
        }

        /// <summary>Сектор в пределах списка и с непустым названием — за ним есть клип.</summary>
        public bool IsEmoteAvailable(int emoteNumber)
        {
            int index = emoteNumber - 1;
            return index >= 0 && index < emoteNames.Length && !string.IsNullOrEmpty(emoteNames[index]);
        }

        private void OnKnockdownStarted(KnockdownType type)
        {
            ActiveEmote = NoEmote;

            if (IsWheelOpen)
            {
                CloseWheel();
            }
        }
    }
}
