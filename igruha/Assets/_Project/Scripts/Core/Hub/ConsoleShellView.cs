using Igruha.Core.Minigame;
using Igruha.Core.Session;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
        private int lastOwnCharacter = -2;
        private bool focusPending;
        private Button libraryLaunch;
        private TMP_Text startText;
        private Selectable lastHomeControl;
        private ConsoleLibraryView libraryView;
        private string lastName;
        private int availableCount, cachedCount = -1;

        public void Initialize(ConsoleMenu owner)
        {
            menu = owner;
            startText = start.GetComponentInChildren<TMP_Text>();
            lastHomeControl = fullGame;
            libraryView = library.GetComponent<ConsoleLibraryView>();
            libraryLaunch = library.transform.Find("FeaturedGame/Launch").GetComponent<Button>();
            libraryView.ConfigureNavigation(libraryLaunch, back);
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
            if (home.activeSelf && page != ConsolePage.Home && EventSystem.current != null)
            {
                var selected = EventSystem.current.currentSelectedGameObject;
                if (selected == fullGame.gameObject) lastHomeControl = fullGame;
                else if (selected == singleGame.gameObject) lastHomeControl = singleGame;
            }
            home.SetActive(page == ConsolePage.Home);
            party.SetActive(page == ConsolePage.Party);
            library.SetActive(page == ConsolePage.Library);
            back.gameObject.SetActive(page != ConsolePage.Home);
            if (page != ConsolePage.Party) { nameInput.DeactivateInputField(); RestoreName(); }
            nextRefresh = 0;
            focusPending = true;
        }
        private void OnEnable() { focusPending = true; }
        private void OnDisable()
        {
            if (nameInput != null) nameInput.DeactivateInputField();
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null &&
                events.currentSelectedGameObject.transform.IsChildOf(transform)) events.SetSelectedGameObject(null);
        }
        private void OnDestroy() { if (service != null) service.ProfileResult -= OnResult; }
        private void OnResult(string message)
        {
            status.text = message; messageUntil = Time.unscaledTime + 4f;
            RestoreName();
        }
        private void RestoreName()
        {
            var own = SessionScoreboard.Current?.LocalPlayer;
            if (own != null) { nameInput.SetTextWithoutNotify(own.DisplayName); lastName = own.DisplayName; }
        }
        private void SaveName() { service?.ChangeOwnName(nameInput.text); nameInput.DeactivateInputField(); }

        private void Update()
        {
            if (menu == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .15f;
            Refresh();
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            bool invalid = selected == null || !selected.activeInHierarchy;
            for (int i = 0; i < skins.Length; i++)
                if (selected == skins[i].gameObject && !skins[i].interactable) invalid = true;
            if (focusPending || invalid) { focusPending = false; FocusPage(); }
        }
        private void FocusPage()
        {
            if (EventSystem.current == null) return;
            if (menu.Page == ConsolePage.Home) { if (menu.CanHost) (lastHomeControl != null ? lastHomeControl : fullGame).Select(); }
            else if (menu.Page == ConsolePage.Library) { if (menu.CanHost) libraryView.FocusSelection(); }
            else if (nameInput.interactable) saveName.Select();
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
            if (menu.Page == ConsolePage.Party) ConfigurePartyNavigation();
            if (ownCharacter != lastOwnCharacter) { lastOwnCharacter = ownCharacter; nextRefresh = 0; }
            if (Time.unscaledTime >= messageUntil)
                status.text = !ready ? "Ждём, пока все выберут персонажа…" : availableCount == 0
                    ? "Для полной игры нужно минимум два участника." : "Меняй своё имя и выбирай свободный облик. Занятые облики затемнены.";
        }

        private void ConfigurePartyNavigation()
        {
            Selectable previous = saveName;
            Link(nameInput, back, start, null, saveName);
            Link(saveName, back, start, nameInput, null);
            for (int i = 0; i < skins.Length; i++)
            {
                if (!skins[i].interactable) continue;
                var nav = previous.navigation; nav.selectOnRight = skins[i]; previous.navigation = nav;
                Link(skins[i], back, start, previous, null);
                previous = skins[i];
            }
            Link(back, start, saveName, null, null);
            Link(start, previous, back, saveName, null);
        }
        private static void Link(Selectable item, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            item.navigation = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnUp = up, selectOnDown = down, selectOnLeft = left, selectOnRight = right };
        }
        public void RestoreFocusIfNeeded()
        {
            // Clicking empty space must not strand keyboard focus on a disabled/inactive page.
            var events = EventSystem.current;
            if (events != null && (events.currentSelectedGameObject == null || !events.currentSelectedGameObject.activeInHierarchy))
                FocusPage();
        }
    }
}
