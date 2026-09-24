using System;
using System.Collections.Generic;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    /// <summary>Одна раскладка вводного экрана для всех сцен; сцены не хранят его копии.</summary>
    public sealed class TutorialView : MonoBehaviour
    {
        private const float DesignWidth = 1920f;
        private const float DesignHeight = 1080f;
        private const int MaxDisplayedPlayers = 8;
        private TMP_FontAsset font;
        private TMP_Text title;
        private TMP_Text category;
        private TMP_Text objective;
        private TMP_Text status;
        private TMP_Text readyLabel;
        private Button readyButton;
        private TMP_Text controls;
        private readonly TMP_Text[] playerLabels = new TMP_Text[MaxDisplayedPlayers];
        private readonly Image[] playerCards = new Image[MaxDisplayedPlayers];
        private readonly List<ControlHint> hints = new List<ControlHint>();
        private RectTransform content;
        private RectTransform fullContent;
        private RectTransform compactContent;
        private Image wash;
        private TMP_Text compactStatus;
        private TMP_Text practiceLabel;
        private Button practiceButton;
        public RawImage ArenaPreview { get; private set; }
        public RectTransform DemonstrationRoot { get; private set; }

        public void Build(TMP_FontAsset textFont, Action ready, Action practice)
        {
            font = textFont;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(DesignWidth, DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)transform;
            content = new GameObject("Layout", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(root, false);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f,.5f);
            content.sizeDelta = new Vector2(DesignWidth, DesignHeight);
            fullContent = content;
            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            var backgroundRect = (RectTransform)background.transform;
            backgroundRect.SetParent(root,false);
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = backgroundRect.offsetMax = Vector2.zero;
            backgroundRect.SetAsFirstSibling();
            wash = background.GetComponent<Image>();
            wash.color = new Color(.025f,.031f,.045f,1f);
            Label("Brand", "КОМНАТА  /  ПЕРЕД НАЧАЛОМ", 80, 50, 1400, 36, 23, UiSkin.Accent);
            title = Label("Title", "", 80, 102, 1580, 85, 62, UiSkin.TextPrimary);
            category = Label("Category", "", 80, 195, 1500, 36, 24, UiSkin.TextMuted);
            DemonstrationRoot = Box("Demonstration", 80, 265, 1030, 410, UiSkin.Card).rectTransform;
            var preview = new GameObject("ArenaPreview", typeof(RectTransform), typeof(RawImage));
            Place((RectTransform)preview.transform, 80,265,1030,410);
            ArenaPreview = preview.GetComponent<RawImage>();
            ArenaPreview.raycastTarget = false;
            Box("PreviewCaptionBackground", 80,265,1030,52,UiSkin.Card);
            Label("PreviewCaption", "ТРЕНИРОВКА НА АРЕНЕ · БЕЗ ОЧКОВ В ЗАЧЁТ", 100, 280, 960, 32, 21, UiSkin.Accent);
            var practiceImage = Box("TryPractice", 80, 700, 1030, 64, UiSkin.Plate);
            practiceButton = practiceImage.gameObject.AddComponent<Button>();
            practiceButton.targetGraphic = practiceImage;
            practiceButton.onClick.AddListener(() => practice());
            practiceLabel = Label("TryPracticeLabel", "Попробовать на арене  →", 100,711,990,46,27,UiSkin.TextPrimary);
            practiceLabel.alignment = TextAlignmentOptions.Center;
            Label("GoalCaption", "ЗАДАЧА  ·  ПРАВИЛА МОЖНО ПРОКРУТИТЬ", 1160, 266, 680, 36, 22, UiSkin.Accent);
            objective = ScrollText("Objective", 1160, 314, 680, 215, 25);
            Label("ControlsCaption", "УПРАВЛЕНИЕ  ·  ПРОКРУТИ СПИСОК", 1160, 550, 680, 42, 22, UiSkin.Accent);
            controls = ScrollText("Controls", 1160, 598, 680, 220, 23);
            Label("PlayersCaption", "ГОТОВНОСТЬ ИГРОКОВ", 80, 805, 1280, 32, 22, UiSkin.TextMuted);
            for (int i = 0; i < MaxDisplayedPlayers; i++)
            {
                playerCards[i] = Box("Player_"+i, 80 + i*220, 854, 208, 72, UiSkin.Plate);
                playerLabels[i] = Label("PlayerLabel_"+i, "", 91 + i*220, 863, 188, 56, 19, UiSkin.TextPrimary);
                playerLabels[i].enableAutoSizing = true;
                playerLabels[i].fontSizeMin = 14;
                playerLabels[i].fontSizeMax = 19;
                playerLabels[i].richText = false;
            }
            status = Label("Status", "Ждём игроков…", 80, 966, 1190, 60, 26, UiSkin.TextSecondary);
            var buttonImage = Box("Ready", 1370, 962, 470, 68, UiSkin.Accent);
            readyButton = buttonImage.gameObject.AddComponent<Button>();
            readyButton.targetGraphic = buttonImage;
            readyButton.onClick.AddListener(() => ready());
            readyLabel = Label("ReadyLabel", "Я готов", 1390, 970, 430, 52, 28, UiSkin.AccentInk);
            readyLabel.alignment = TextAlignmentOptions.Center;
            compactContent = new GameObject("PracticeOverlay", typeof(RectTransform)).GetComponent<RectTransform>();
            compactContent.SetParent(root,false);
            compactContent.anchorMin = compactContent.anchorMax = compactContent.pivot = new Vector2(.5f,.5f);
            compactContent.sizeDelta = new Vector2(DesignWidth,DesignHeight);
            content = compactContent;
            Box("PracticeBanner", 390, 24, 1140, 88, UiSkin.Card);
            compactStatus = Label("PracticeStatus", "", 410,34,1100,70,23,UiSkin.TextPrimary);
            compactStatus.alignment = TextAlignmentOptions.Center;
            content = fullContent;
            SetExpanded(true);
        }

        public void Show(MinigameDefinition definition)
        {
            gameObject.SetActive(true);
            practiceButton.interactable = true;
            practiceLabel.text = "Попробовать на арене  →";
            SetExpanded(true);
            title.text = definition != null ? definition.DisplayName : "Мини-игра";
            objective.text = definition != null ? definition.Objective : "Дождитесь остальных участников.";
            category.text = definition == null ? "" : definition.Category == MinigameCategory.Team
                ? "КОМАНДА НА КОМАНДУ" : definition.Category == MinigameCategory.Asymmetric
                    ? "РАЗНЫЕ РОЛИ" : "ВСЕ ПРОТИВ ВСЕХ";
            ControlHintParser.Parse(definition != null ? definition.ControlHints : Array.Empty<string>(), hints);
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < hints.Count; i++)
            {
                if (i > 0) text.Append("\n\n");
                if (!string.IsNullOrEmpty(hints[i].Key)) text.Append(hints[i].Key).Append("  —  ");
                text.Append(hints[i].Action);
            }
            controls.text = text.ToString();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(practiceButton.gameObject);
        }

        public void SetReadiness(IReadOnlyList<SessionPlayer> players,
            IReadOnlyList<TutorialParticipant> participants, int localId)
        {
            int readyCount = 0;
            bool localReady = false;
            bool localParticipates = false;
            for (int i = 0; i < MaxDisplayedPlayers; i++)
            {
                bool active = i < participants.Count;
                playerCards[i].gameObject.SetActive(active);
                playerLabels[i].gameObject.SetActive(active);
                if (!active) continue;
                var entry = participants[i];
                if (entry.Ready) readyCount++;
                if (entry.PlayerId == localId) { localReady = entry.Ready; localParticipates = true; }
                string name = "Игрок " + (i + 1);
                for (int j = 0; j < players.Count; j++)
                    if (players[j].Id == entry.PlayerId) { name = players[j].DisplayName; break; }
                playerLabels[i].text = name + (entry.PlayerId == localId ? " (вы)" : "") +
                    (entry.Ready ? "\nГотов" : "\nИзучает правила");
                playerCards[i].color = entry.Ready ? new Color(.13f,.30f,.24f,1) : UiSkin.Plate;
            }
            readyButton.interactable = localParticipates;
            practiceButton.interactable &= localParticipates;
            readyLabel.text = localReady ? "Готов!  Отменить" : localParticipates ? "Я готов" : "Вы наблюдаете";
            compactStatus.text = "ТРЕНИРОВКА · БЕЗ ЗАЧЁТА    |    Готовы " + readyCount + "/" + participants.Count +
                "\nF1 — правила / играть    ·    F2 — " + (localReady ? "отменить готовность" : "я готов");
            status.text = "Готовы " + readyCount + " из " + participants.Count + "  ·  Начнём, когда готовы все";
        }

        public void SetExpanded(bool expanded)
        {
            fullContent.gameObject.SetActive(expanded);
            compactContent.gameObject.SetActive(!expanded);
            wash.enabled = expanded;
            if (!expanded && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        public void SetPracticeComplete()
        {
            practiceButton.interactable = false;
            practiceLabel.text = "Тренировка завершена. Готовы начать?";
        }

        private TMP_Text ScrollText(string name, float x, float y, float width, float height, float size)
        {
            var viewport = Box(name + "Viewport", x, y, width, height, new Color(0,0,0,.01f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport.rectTransform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            var label = Label(name, "", 0,0,width-12,height,size,UiSkin.TextPrimary);
            label.rectTransform.SetParent(viewport.transform,false);
            label.rectTransform.anchorMin = new Vector2(0,1);
            label.rectTransform.anchorMax = new Vector2(1,1);
            label.rectTransform.pivot = new Vector2(0,1);
            label.rectTransform.anchoredPosition = Vector2.zero;
            label.rectTransform.sizeDelta = new Vector2(-12,0);
            label.overflowMode = TextOverflowModes.Overflow;
            var fitter = label.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = label.rectTransform;
            var rail = new GameObject(name + "Scroll", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var railRect = (RectTransform)rail.transform;
            railRect.SetParent(viewport.transform,false);
            railRect.anchorMin = new Vector2(1,0);
            railRect.anchorMax = new Vector2(1,1);
            railRect.pivot = new Vector2(1,.5f);
            railRect.sizeDelta = new Vector2(6,0);
            rail.GetComponent<Image>().color = UiSkin.Plate;
            var thumb = new GameObject("Thumb", typeof(RectTransform), typeof(Image));
            var thumbRect = (RectTransform)thumb.transform;
            thumbRect.SetParent(railRect,false);
            thumbRect.anchorMin = Vector2.zero;
            thumbRect.anchorMax = Vector2.one;
            thumbRect.offsetMin = thumbRect.offsetMax = Vector2.zero;
            var thumbImage = thumb.GetComponent<Image>();
            thumbImage.color = UiSkin.Accent;
            var scrollbar = rail.GetComponent<Scrollbar>();
            scrollbar.handleRect = thumbRect;
            scrollbar.targetGraphic = thumbImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return label;
        }

        private Image Box(string name, float x, float y, float width, float height, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            Place((RectTransform)go.transform, x,y,width,height);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private TMP_Text Label(string name, string text, float x, float y, float width, float height, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            Place((RectTransform)go.transform,x,y,width,height);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        private void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.SetParent(content,false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0,1);
            rect.anchoredPosition = new Vector2(x,-y);
            rect.sizeDelta = new Vector2(width,height);
        }
    }
}
