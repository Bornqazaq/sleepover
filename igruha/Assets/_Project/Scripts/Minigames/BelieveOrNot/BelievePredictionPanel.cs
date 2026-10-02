using System;
using Igruha.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>Небольшая панель зрителя. Клавиши 1/2 не забирают мышь у камеры.</summary>
    public sealed class BelievePredictionPanel : MonoBehaviour
    {
        private static readonly Color Paper = new Color(.94f, .91f, .83f);
        private static readonly Color Gold = new Color(.94f, .76f, .40f);
        private static readonly Color Surface = new Color(.055f, .065f, .065f, .94f);
        private const float PanelWidth = 430f;
        private const float ChoiceHeight = 204f;
        private const float ResultHeight = 310f;
        private const int CanvasOrder = 15;

        private GameObject root;
        private RectTransform plate;
        private TMP_Text title;
        private TMP_Text body;
        private TMP_Text footer;
        private bool canPick;
        private int firstId;
        private int secondId;
        private int round;
        public event Action<int, int> Picked;
        public bool CanPick => canPick;
        public bool IsVisible => root != null && root.activeSelf;
        public string Body => body != null ? body.text : string.Empty;

        public void Initialize(TMP_FontAsset font)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasOrder;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            root = new GameObject("PredictionCard", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(transform, false);
            plate = (RectTransform)root.transform;
            plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(1f, 1f);
            plate.anchoredPosition = new Vector2(-28f, -260f);
            plate.sizeDelta = new Vector2(PanelWidth, ChoiceHeight);
            var background = root.GetComponent<Image>();
            background.color = Surface;
            background.raycastTarget = false;
            title = Line("Title", font, 23f, Gold, 18f, 36f);
            body = Line("Body", font, 24f, Paper, 62f, 95f);
            footer = Line("Footer", font, 17f, Paper, 165f, 30f);
            Close();
        }

        private TMP_Text Line(string label, TMP_FontAsset font, float size, Color color, float top, float height)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(root.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(20f, -top - height);
            rect.offsetMax = new Vector2(-20f, -top);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.raycastTarget = false;
            text.richText = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        public void Open(int roundNumber, int first, string firstName, int second, string secondName)
        {
            if (IsVisible && round == roundNumber) return;
            round = roundNumber;
            firstId = first;
            secondId = second;
            canPick = true;
            Resize(ChoiceHeight, 95f);
            title.text = "КТО ВЫИГРАЕТ КОН?";
            body.text = "1  " + firstName + "\n2  " + secondName;
            footer.text = "1 / 2 · выбор окончательный и скрытый";
            root.SetActive(true);
        }

        public void Choose(int winnerId)
        {
            if (!canPick || PauseScreen.Current?.IsPaused == true ||
                (winnerId != firstId && winnerId != secondId)) return;
            canPick = false;
            footer.text = "Отправляем прогноз…";
            Picked?.Invoke(round, winnerId);
        }

        public void Confirm(string winnerName)
        {
            canPick = false;
            title.text = "ПРОГНОЗ ПРИНЯТ";
            body.text = "Победит " + winnerName;
            footer.text = "Другие узнают твой выбор при раскрытии";
        }

        public void Lock()
        {
            canPick = false;
            if (IsVisible) footer.text = "Смотрим, у кого галочка…";
        }

        public void ShowResults(string heading, string lines, int count)
        {
            canPick = false;
            Resize(count > 3 ? ResultHeight : ChoiceHeight, count > 3 ? 200f : 95f);
            title.text = heading;
            body.fontSize = 21f;
            body.text = lines;
            footer.text = "+ угадал · - не угадал · без очков";
            root.SetActive(true);
        }

        private void Resize(float height, float bodyHeight)
        {
            plate.sizeDelta = new Vector2(PanelWidth, height);
            body.fontSize = 24f;
            var bodyRect = body.rectTransform;
            bodyRect.offsetMin = new Vector2(20f, -62f - bodyHeight);
            var footRect = footer.rectTransform;
            footRect.offsetMin = new Vector2(20f, -height + 9f);
            footRect.offsetMax = new Vector2(-20f, -height + 39f);
        }

        public void Close()
        {
            canPick = false;
            if (root != null) root.SetActive(false);
        }

        private void Update()
        {
            if (!canPick || Keyboard.current == null || PauseScreen.Current?.IsPaused == true) return;
            if (Keyboard.current.digit1Key.wasPressedThisFrame) Choose(firstId);
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) Choose(secondId);
        }
    }
}
