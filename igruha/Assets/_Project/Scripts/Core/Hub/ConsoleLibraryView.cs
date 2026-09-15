using System.Collections.Generic;
using Igruha.Core.Minigame;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Igruha.Core.Hub
{
    /// <summary>Presentation only. The catalog and ConsoleMenu remain responsible for selection and launch.</summary>
    public sealed class ConsoleLibraryView : MonoBehaviour, IScrollHandler
    {
        [SerializeField] private ConsoleArtworkLibrary artwork;
        [SerializeField] private ConsoleGameCard cardTemplate;
        [SerializeField] private RectTransform cardStrip;
        [SerializeField] private Image heroCover;
        [SerializeField] private Image heroAccent;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text genre;
        [SerializeField] private TMP_Text description;
        [SerializeField] private TMP_Text playerCount;
        [SerializeField] private TMP_Text page;
        [SerializeField] private TMP_Text positionLabel;
        [SerializeField] private TMP_Text launchLabel;
        [SerializeField] private Image progress;
        [SerializeField] private GameObject previousPage;
        [SerializeField] private GameObject nextPage;
        [SerializeField, Min(1)] private int visibleCards = 5;
        [SerializeField, Min(1f)] private float cardPitch = 240f;
        [SerializeField, Min(0.01f)] private float slideTime = 0.12f;

        private readonly List<ConsoleGameCard> cards = new List<ConsoleGameCard>();
        private MinigameCatalog catalog;
        private float targetX;
        private float velocity;
        private int selectedIndex;
        private Button launchButton, backButton;

        public void Initialize(MinigameCatalog source)
        {
            catalog = source;
            if (cardTemplate == null || cardStrip == null) return;
            int count = catalog != null ? catalog.Games.Count : 0;
            for (int i = 0; i < count; i++)
            {
                ConsoleGameCard card = i == 0 ? cardTemplate : Instantiate(cardTemplate, cardStrip);
                card.CatalogIndex = i;
                card.name = $"GameCard_{i:00}";
                ((RectTransform)card.transform).anchoredPosition = new Vector2(i * cardPitch, 0f);
                MinigameDefinition game = catalog.Get(i);
                ConsoleArtworkLibrary.Entry art = artwork != null ? artwork.Find(game) : null;
                card.Bind(game != null ? game.DisplayName : "Игра недоступна", art?.Cover, catalog.IsPlayable(i));
                cards.Add(card);
            }
            cardTemplate.gameObject.SetActive(count > 0);
            cardStrip.sizeDelta = new Vector2(Mathf.Max(0, count * cardPitch), cardStrip.sizeDelta.y);
        }

        public void ShowSelection(int index, bool authority)
        {
            selectedIndex = index;
            MinigameDefinition game = catalog != null ? catalog.Get(index) : null;
            ConsoleArtworkLibrary.Entry art = artwork != null ? artwork.Find(game) : null;
            Color accent = art != null ? art.Accent : new Color(0.95f, 0.74f, 0.51f);
            for (int i = 0; i < cards.Count; i++) { cards[i].SetSelected(i == index, accent); cards[i].interactable = authority && catalog.IsPlayable(i); }
            if (launchButton != null) launchButton.interactable = authority && catalog.IsPlayable(index);
            heroCover.sprite = art?.Cover;
            heroCover.enabled = heroCover.sprite != null;
            heroAccent.color = accent;
            genre.color = accent;
            title.text = game != null ? game.DisplayName : "Скоро сыграем";
            genre.text = art != null ? art.Genre : "МИНИ-ИГРЫ ДЛЯ КОМПАНИИ";
            description.text = art != null ? art.Summary : (game != null ? game.Objective : "В библиотеке пока нет игр.");
            playerCount.text = game != null ? $"{game.MinPlayers}–{game.MaxPlayers} игроков  /  {CategoryLabel(game.Category)}" : string.Empty;
            page.text = cards.Count > 0 ? $"{index + 1:00} / {cards.Count:00}" : "00 / 00";
            positionLabel.text = $"БИБЛИОТЕКА  /  {cards.Count:00} ИГР";
            bool playable = catalog != null && catalog.IsPlayable(index);
            launchLabel.text = !playable ? "СКОРО В ИГРЕ" : authority ? "ENTER   ИГРАТЬ" : "ВЫБИРАЕТ ХОСТ";
            int first = Mathf.Clamp(index - visibleCards / 2, 0, Mathf.Max(0, cards.Count - visibleCards));
            targetX = -first * cardPitch;
            previousPage.SetActive(first > 0);
            nextPage.SetActive(first + visibleCards < cards.Count);
            progress.rectTransform.anchorMax = new Vector2(cards.Count > 0 ? (float)(index + 1) / cards.Count : 0f, 1f);
            if (launchButton != null && index >= 0 && index < cards.Count)
            {
                var nav = launchButton.navigation; nav.selectOnDown = cards[index]; launchButton.navigation = nav;
                if (backButton != null) backButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = launchButton, selectOnUp = cards[index] };
            }
            if (!isActiveAndEnabled) SnapStrip();
        }

        public void ConfigureNavigation(Button launch, Button back)
        {
            launchButton = launch; backButton = back;
            previousPage.GetComponent<Button>().onClick.AddListener(() => Browse(-1));
            nextPage.GetComponent<Button>().onClick.AddListener(() => Browse(1));
            for (int i = 0; i < cards.Count; i++)
            {
                int left = catalog.NextPlayable(i, -1), right = catalog.NextPlayable(i, 1);
                cards[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnLeft = left >= 0 ? cards[left] : null, selectOnRight = right >= 0 ? cards[right] : null,
                    selectOnUp = launchButton, selectOnDown = backButton };
            }
            launchButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = backButton,
                selectOnDown = cards.Count > 0 ? cards[0] : null };
        }
        private void Browse(int direction)
        {
            if (ConsoleMenu.Active == null || !ConsoleMenu.Active.CanHost) return;
            int index = catalog.NextPlayable(selectedIndex, direction);
            if (index < 0) return;
            ConsoleMenu.Active.SelectGame(index);
            cards[index].Select();
        }
        public void OnScroll(PointerEventData e)
        {
            if (Mathf.Abs(e.scrollDelta.y) > .01f) Browse(e.scrollDelta.y < 0 ? 1 : -1);
        }
        public void FocusSelection()
        {
            if (selectedIndex >= 0 && selectedIndex < cards.Count && cards[selectedIndex].IsInteractable()) cards[selectedIndex].Select();
        }

        private void OnEnable() => SnapStrip();

        private void SnapStrip()
        {
            if (cardStrip != null) cardStrip.anchoredPosition = new Vector2(targetX, 0f);
            velocity = 0f;
        }

        private void Update()
        {
            if (cardStrip == null) return;
            float x = Mathf.SmoothDamp(cardStrip.anchoredPosition.x, targetX, ref velocity,
                slideTime, Mathf.Infinity, Time.unscaledDeltaTime);
            cardStrip.anchoredPosition = new Vector2(x, 0f);
        }

        private static string CategoryLabel(MinigameCategory category)
        {
            switch (category)
            {
                case MinigameCategory.Team: return "командная игра";
                case MinigameCategory.Asymmetric: return "разные роли";
                default: return "каждый за себя";
            }
        }
    }
}
