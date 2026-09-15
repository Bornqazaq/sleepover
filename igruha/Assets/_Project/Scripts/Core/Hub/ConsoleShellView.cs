using Igruha.Core.Minigame;
using Igruha.Core.Session;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Igruha.Core.Hub
{
    /// <summary>Home and party presentation. Shared navigation belongs to ConsoleMenu; profile editing is local.</summary>
    public sealed class ConsoleShellView : MonoBehaviour
    {
        public GameObject home, party, library;
        public Button fullGame, singleGame, back, start, saveName;
        public TMP_Text homeCount, partyCount, seriesInfo, status, footer;
        public TMP_InputField nameInput;
        public ConsoleParticipantCard[] participants;
        public Button[] skins;
        public Image[] skinFrames;
        public Sprite[] portraits;
        public HubPartyProfiles profiles;
        private ConsoleMenu menu;
        private IHubPartyProfiles service;
        private float nextRefresh, messageUntil;
        private int homeChoice, lastOwnCharacter = -2;
        private string lastName;
        private int availableCount, cachedCount = -1;

        public void Initialize(ConsoleMenu owner)
        {
            menu = owner;
            fullGame.onClick.AddListener(() => menu.RequestPage(ConsolePage.Party));
            singleGame.onClick.AddListener(() => menu.RequestPage(ConsolePage.Library));
            back.onClick.AddListener(menu.Back);
            start.onClick.AddListener(menu.StartFullGame);
            saveName.onClick.AddListener(SaveName);
            nameInput.onSubmit.AddListener(_ => SaveName());
            for (int i = 0; i < skins.Length; i++)
            {
                int index = i;
                skins[i].onClick.AddListener(() => service?.ChangeOwnCharacter(index));
            }
            ShowPage(ConsolePage.Home);
        }

        public void ShowPage(ConsolePage page)
        {
            home.SetActive(page == ConsolePage.Home);
            party.SetActive(page == ConsolePage.Party);
            library.SetActive(page == ConsolePage.Library);
            back.gameObject.SetActive(page != ConsolePage.Home);
            if (page != ConsolePage.Party && nameInput.isFocused) nameInput.DeactivateInputField();
            nextRefresh = 0;
        }
        private void OnDisable() { if (nameInput != null) nameInput.DeactivateInputField(); }
        private void OnDestroy() { if (service != null) service.ProfileResult -= OnResult; }
        private void OnResult(string message) { status.text = message; messageUntil = Time.unscaledTime + 4f; }
        private void SaveName() { service?.ChangeOwnName(nameInput.text); nameInput.DeactivateInputField(); }

        private void Update()
        {
            if (menu == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .15f;
            Refresh();
        }
        public void Refresh()
        {
            var current = profiles.Current;
            if (!ReferenceEquals(current, service))
            {
                if (service != null) service.ProfileResult -= OnResult;
                service = current;
                if (service != null) service.ProfileResult += OnResult;
            }
            var session = SessionScoreboard.Current;
            int count = session?.Players.Count ?? 0;
            bool authority = menu.CanHost;
            fullGame.interactable = singleGame.interactable = back.interactable = authority;
            homeCount.text = $"В КОМНАТЕ  {count:00} / 08";
            partyCount.text = $"{count:00} / 08";
            footer.text = authority ? "↑ ↓   ВЫБОР       ENTER   ОТКРЫТЬ       ESC   ВЫКЛЮЧИТЬ" : "РЕЖИМ ВЫБИРАЕТ ХОСТ";
            if (cachedCount != count) { cachedCount = count; availableCount = PartySeries.AvailableCount(menu.Catalog, count); }
            string summary = ConsoleMenu.Relay?.SeriesSummary ?? PartySeries.Summary;
            seriesInfo.text = string.IsNullOrEmpty(summary)
                ? $"{availableCount:00} ИГР  ·  СЛУЧАЙНЫЙ ПОРЯДОК  ·  БЕЗ ПОВТОРОВ"
                : summary;
            var own = session?.LocalPlayer;
            bool ready = count > 0 && own?.Avatar != null;
            int shown = count <= 4 ? 4 : 8;
            for (int i = 0; i < participants.Length; i++)
            {
                participants[i].gameObject.SetActive(i < shown);
                if (i >= shown) continue;
                var player = i < count ? session.Players[i] : null;
                int character = player != null ? service.CharacterOf(player.Id) : -1;
                var portrait = character >= 0 && character < portraits.Length ? portraits[character] : null;
                participants[i].Show(player, player != null && own != null && player.Id == own.Id, portrait, i, shown);
                if (player != null && player.Avatar == null) ready = false;
            }
            start.interactable = authority && ready && availableCount > 0 && !menu.IsLoading;
            var startText = start.GetComponentInChildren<TMP_Text>();
            startText.text = authority ? "НАЧАТЬ ИГРУ  →" : "ЗАПУСКАЕТ ХОСТ";
            nameInput.interactable = saveName.interactable = own?.Avatar != null;
            if (own != null && !nameInput.isFocused && lastName != own.DisplayName)
            { nameInput.SetTextWithoutNotify(own.DisplayName); lastName = own.DisplayName; }
            int ownCharacter = own == null ? -1 : service.CharacterOf(own.Id);
            for (int i = 0; i < skins.Length; i++)
            {
                bool taken = false;
                if (session != null) foreach (var player in session.Players)
                    if (player != own && service.CharacterOf(player.Id) == i) taken = true;
                skins[i].interactable = own?.Avatar != null && !taken && i != ownCharacter;
                var colors = skins[i].colors;
                colors.disabledColor = i == ownCharacter ? Color.white : new Color(.35f,.38f,.38f,.7f);
                skins[i].colors = colors;
                skinFrames[i].color = i == ownCharacter ? new Color(.96f, .74f, .48f) : taken
                    ? new Color(.12f, .17f, .18f) : new Color(.30f, .43f, .44f);
            }
            if (ownCharacter != lastOwnCharacter) { lastOwnCharacter = ownCharacter; nextRefresh = 0; }
            if (Time.unscaledTime >= messageUntil)
                status.text = !ready ? "Ждём, пока все выберут персонажа…" : availableCount == 0
                    ? "Для полной игры нужно минимум два участника." : "Меняй своё имя и выбирай свободный облик. Занятые облики затемнены.";
        }

        public void HandleKeyboard()
        {
            // TMP owns Enter/Esc while editing; it must not start a match or close the television.
            if (nameInput.isFocused) return;
            var k = Keyboard.current; var g = Gamepad.current;
            if ((k != null && k.escapeKey.wasPressedThisFrame) || (g != null && g.buttonEast.wasPressedThisFrame))
            { menu.Back(); return; }
            if (menu.Page == ConsolePage.Home)
            {
                bool move = k != null && (k.downArrowKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame ||
                    k.leftArrowKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame);
                move |= g != null && (g.dpad.down.wasPressedThisFrame || g.dpad.up.wasPressedThisFrame);
                if (move) { homeChoice = 1 - homeChoice; (homeChoice == 0 ? fullGame : singleGame).Select(); }
                if ((k != null && k.enterKey.wasPressedThisFrame) || (g != null && g.buttonSouth.wasPressedThisFrame))
                    menu.RequestPage(homeChoice == 0 ? ConsolePage.Party : ConsolePage.Library);
            }
            // Party uses normal selectable navigation, so Enter activates the focused control only.
        }
    }
}
