using System;
using Igruha.Core.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    /// <summary>Presentation only. Availability, time and confirmation come from CharacterSelectScreen.</summary>
    public sealed class CharacterSelectionView : MonoBehaviour
    {
        [Serializable]
        public sealed class Portrait
        {
            public string Name;
            public string Tagline;
            public Sprite Face;
            public Sprite Body;
            public Color Accent = Color.white;
        }

        [SerializeField] private Portrait[] portraits = Array.Empty<Portrait>();
        [SerializeField] private Image hero, heroBackdrop, heroAccent;
        [SerializeField] private TMP_Text heroName, heroTagline, heroNumber, availableLabel;
        [SerializeField] private TMP_Text secondsLabel, timerCaption, statusLabel;
        [SerializeField] private CountdownRing ring;
        [SerializeField] private Image progress;
        [SerializeField] private Button playButton;
        [SerializeField] private TMP_Text playLabel;
        private CharacterSelectScreen owner;
        private CharacterRoster roster;
        private CharacterSlotButton[] slots;
        private int focused = -1;
        private int lastSecond = -1;
        private bool pending;
        private static readonly Color Cream = new Color(.957f, .925f, .851f);
        private static readonly Color Gold = new Color(.957f, .741f, .506f);
        private static readonly Color Urgent = new Color(1f, .46f, .33f);

        private void Awake() { playButton.onClick.AddListener(Confirm); }
        private void OnDestroy() { if (playButton != null) playButton.onClick.RemoveListener(Confirm); }
        private void Confirm() { if (focused >= 0) owner.OnSlotChosen(focused); }

        public void Initialize(CharacterSelectScreen screen, CharacterRoster characters, CharacterSlotButton[] buttons)
        {
            owner = screen; roster = characters; slots = buttons;
            focused = -1; lastSecond = -1; pending = false;
            for (int i = 0; i < slots.Length; i++)
                slots[i].SetPortrait(i < roster.Characters.Count ? Find(roster.Characters[i].DisplayName) : null);
        }

        private Portrait Find(string name)
        {
            foreach (var portrait in portraits) if (portrait.Name == name) return portrait;
            return null;
        }

        public void RefreshAvailability()
        {
            int free = 0;
            foreach (var slot in slots) if (slot.IsAvailable) free++;
            availableLabel.text = $"КОМПАНИЯ  /  СВОБОДНО {free:00}";
            if (focused < 0 || !slots[focused].IsAvailable)
                for (int i = 0; i < slots.Length; i++) if (slots[i].IsAvailable) { Focus(i); break; }
            playButton.interactable = !pending && focused >= 0 && slots[focused].IsAvailable;
        }

        public void Focus(int index)
        {
            if (pending || index < 0 || index >= slots.Length || !slots[index].IsAvailable) return;
            focused = index;
            var character = roster.Characters[index];
            var art = Find(character.DisplayName);
            hero.sprite = art?.Body; hero.enabled = hero.sprite != null;
            heroName.text = character.DisplayName;
            heroNumber.text = $"{index + 1:00} / {roster.Characters.Count:00}";
            heroTagline.text = art?.Tagline ?? "ТВОЯ ИСТОРИЯ НАЧИНАЕТСЯ ЗДЕСЬ";
            Color accent = art?.Accent ?? Gold;
            heroAccent.color = accent;
            heroBackdrop.color = new Color(accent.r * .28f, accent.g * .28f, accent.b * .28f, 1);
            heroTagline.color = accent;
            playLabel.text = "ИГРАТЬ ЗА " + character.DisplayName.ToUpperInvariant();
            for (int i = 0; i < slots.Length; i++) slots[i].SetFocused(i == index);
            playButton.interactable = true;
        }

        public void SetPending(bool value)
        {
            pending = value;
            playButton.interactable = !value && focused >= 0 && slots[focused].IsAvailable;
            statusLabel.text = value ? "Закрепляем персонажа за тобой…" : "Нажми на героя — и присоединяйся к компании.";
            if (!value) lastSecond = -1;
        }

        public void SetCountdown(float seconds, float duration)
        {
            float fraction = duration > 0 ? Mathf.Clamp01(seconds / duration) : 0;
            ring.SetProgress(fraction);
            progress.rectTransform.anchorMax = new Vector2(fraction, 1);
            int whole = Mathf.CeilToInt(Mathf.Max(0, seconds));
            if (whole != lastSecond)
            {
                secondsLabel.text = whole.ToString("00");
                timerCaption.text = whole == 0 ? "ВЫБИРАЕМ ГЕРОЯ" : "ДО АВТОВЫБОРА";
                lastSecond = whole;
            }
            bool urgent = seconds <= 5;
            Color tone = urgent ? Urgent : Gold;
            ring.color = tone; progress.color = tone; secondsLabel.color = urgent ? tone : Cream;
            if (urgent && !pending)
                statusLabel.text = seconds > 0 ? "Не успеешь выбрать — достанется случайный свободный герой." : "Подбираем свободного персонажа…";
        }
    }
}
