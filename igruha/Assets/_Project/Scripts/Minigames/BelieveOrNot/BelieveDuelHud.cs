using System;
using Igruha.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>Общий отсчёт кона и публичная клятва. Курсор нужен только выбирающему.</summary>
    public sealed class BelieveDuelHud : MonoBehaviour
    {
        private static readonly Color Surface = new Color(.075f, .10f, .11f, .97f);
        private static readonly Color Gold = new Color(.97f, .79f, .46f);
        private static readonly Color Paper = new Color(.96f, .93f, .86f);
        private static readonly Color Red = new Color(1f, .48f, .38f);
        private const int UrgentSeconds = 5;
        private readonly PanelCursor cursor = new PanelCursor();
        private GameObject clockRoot, oathRoot, choices;
        private TMP_Text clockTitle, clockValue, statement, verdict;
        private TMP_FontAsset font;
        private Sprite panelSprite;
        private RectTransform matchStatus;
        private bool choosing;
        private int seconds = -1;
        private byte shownStage = byte.MaxValue;
        public event Action<BelieveOath> Picked;
        public bool Choosing => choosing;
        public string StatementText => statement != null ? statement.text : string.Empty;
        public string VerdictText => verdict != null ? verdict.text : string.Empty;

        public void Initialize(TMP_FontAsset typeface, Sprite roundedPanel = null, RectTransform status = null)
        {
            font = typeface;
            panelSprite = roundedPanel;
            matchStatus = status;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 16;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            clockRoot = Plate("DuelClock", transform, new Vector2(.5f, 1f), new Vector2(0f, -22f), new Vector2(228f, 94f));
            clockTitle = Label("Stage", clockRoot.transform, 18f, new Vector2(0f, -18f), new Vector2(218f, 25f));
            clockValue = Label("Seconds", clockRoot.transform, 36f, new Vector2(0f, -59f), new Vector2(218f, 64f));
            oathRoot = Plate("Oath", transform, new Vector2(.5f, 0f), new Vector2(0f, 270f), new Vector2(710f, 138f));
            statement = Label("Promise", oathRoot.transform, 26f, new Vector2(0f, -29f), new Vector2(680f, 40f));
            verdict = Label("Verdict", oathRoot.transform, 19f, new Vector2(0f, -108f), new Vector2(680f, 30f));
            choices = new GameObject("Choices", typeof(RectTransform));
            choices.transform.SetParent(oathRoot.transform, false);
            var rect = (RectTransform)choices.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -75f);
            rect.sizeDelta = Vector2.zero;
            MakeChoice("1  У МЕНЯ", -168f, BelieveOath.Mine);
            MakeChoice("2  У ТЕБЯ", 168f, BelieveOath.Yours);
            Close();
        }

        private GameObject Plate(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            // Bottom-anchored card grows down from its stated top edge.
            if (anchor.y == 0f) rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            go.GetComponent<Image>().color = Surface;
            go.GetComponent<Image>().sprite = panelSprite;
            go.GetComponent<Image>().type = Image.Type.Sliced;
            go.GetComponent<Image>().raycastTarget = false;
            return go;
        }

        private TMP_Text Label(string name, Transform parent, float size, Vector2 position, Vector2 bounds)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = bounds;
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = Paper;
            text.alignment = TextAlignmentOptions.Center;
            text.richText = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private void MakeChoice(string title, float x, BelieveOath oath)
        {
            var go = Plate(title, choices.transform, new Vector2(.5f, .5f), new Vector2(x, 0f), new Vector2(318f, 46f));
            var background = go.GetComponent<Image>();
            background.color = new Color(.31f, .25f, .19f);
            background.raycastTarget = true;
            var button = go.AddComponent<Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.highlightedColor = Gold;
            button.colors = colors;
            button.onClick.AddListener(() => Choose(oath));
            Label("Text", go.transform, 22f, new Vector2(0f, -23f), new Vector2(305f, 40f)).text = title;
        }

        public void Choose(BelieveOath oath)
        {
            if (!choosing || PauseScreen.Current?.IsPaused == true || !BelieveOathRules.IsClaim(oath)) return;
            SetChoosing(false);
            verdict.text = "Клятва отправлена…";
            Picked?.Invoke(oath);
        }

        private void SetChoosing(bool value)
        {
            if (choosing == value) return;
            choosing = value;
            if (value) cursor.Release(); else cursor.Restore();
        }

        public void ShowOath(bool visible, bool canChoose, string speaker, BelieveOath oath, bool revealed, bool truth)
        {
            SetChoosing(visible && canChoose);
            oathRoot.SetActive(visible);
            choices.SetActive(choosing);
            if (!visible) return;
            statement.text = choosing ? "ДАЙ КЛЯТВУ: ГДЕ ВЫИГРЫШНАЯ?" :
                oath == BelieveOath.Pending ? speaker + " выбирает клятву…" : speaker + ": " + BelieveOathRules.Statement(oath);
            statement.color = Gold;
            verdict.text = revealed ? (truth ? "СКАЗАЛ ПРАВДУ" : "СОЛГАЛ") :
                oath == BelieveOath.Declined ? "В этом коне клятвы не было" :
                choosing ? "Можно блефовать · выбор окончательный" : "Проверим после открытия коробок";
            verdict.color = revealed && !truth ? Red : Paper;
            var rect = (RectTransform)oathRoot.transform;
            rect.sizeDelta = new Vector2(710f, choosing ? 138f : 98f);
            verdict.rectTransform.anchoredPosition = new Vector2(0f, choosing ? -108f : -68f);
        }

        public void ShowClock(byte stage, float remaining)
        {
            clockRoot.SetActive(stage != 0);
            if (stage == 0) return;
            if (shownStage != stage)
            {
                shownStage = stage;
                clockTitle.text = stage == BelieveStage.Oath ? "ДАЙ КЛЯТВУ" :
                    stage == BelieveStage.Persuasion ? "ВРЕМЯ РЕШИТЬ" :
                    stage == BelieveStage.Peek ? "ПОСМОТРИ КАРТОЧКУ" :
                    stage == BelieveStage.Seating ? "ЗА СТОЛ" : stage == BelieveStage.Cancelled ? "КОН ОТМЕНЁН" : "РАСКРЫТИЕ";
                seconds = -1;
            }
            int whole = Mathf.CeilToInt(Mathf.Max(0f, remaining));
            if (seconds == whole) return;
            seconds = whole;
            clockValue.text = stage == BelieveStage.Reveal || stage == BelieveStage.Reaction || stage == BelieveStage.Cancelled ? "—" : whole.ToString();
            clockValue.color = stage == BelieveStage.Persuasion && whole <= UrgentSeconds ? Red : Gold;
        }

        public void Close()
        {
            SetChoosing(false);
            clockRoot?.SetActive(false);
            oathRoot?.SetActive(false);
        }

        private void OnDisable() => SetChoosing(false);

        // RoundHud переставляет статус при выходе из тренировки. Возвращаем
        // локальную раскладку после этого перехода, не меняя общий HUD других игр.
        private void LateUpdate()
        {
            if (Igruha.Core.Minigame.MinigameControllerBase.Current?.IsPractice == true) return;
            if (matchStatus != null && matchStatus.anchorMin.x != 0f) ArrangeStatus(matchStatus);
        }

        public static void ArrangeStatus(RectTransform plate)
        {
            plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(0f, 1f);
            plate.anchoredPosition = new Vector2(28f, -22f);
            plate.sizeDelta = new Vector2(700f, 96f);
        }

        private void Update()
        {
            if (!choosing || Keyboard.current == null || PauseScreen.Current?.IsPaused == true) return;
            if (Keyboard.current.digit1Key.wasPressedThisFrame) Choose(BelieveOath.Mine);
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) Choose(BelieveOath.Yours);
        }
    }
}
