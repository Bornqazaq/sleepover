using Igruha.Core.Minigame;
using Igruha.Minigames.Circus;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Igruha.Minigames.Circus.CircusUiLayout;

namespace Igruha.Minigames.CansOrder
{
    public sealed partial class CansOrderLocalHud
    {
        private GameObject practiceCanvas;
        private TMP_Text practiceScore, practiceNote;
        private int shownPracticeMatches = int.MinValue, shownPracticeCount = -1;
        private bool shownPracticeActive;

        private void RenderPractice()
        {
            bool visible = rules != null && rules.IsPractice && rules.Phase == MinigamePhase.Practice &&
                stageState != null && (stageState.Stage == 2 || stageState.Stage == 3) && shelf != null && shelf.SlotCount > 0;
            if (!visible) { if (practiceCanvas != null) practiceCanvas.SetActive(false); return; }
            rules.RefreshPracticePreview(shelf);
            if (practiceCanvas == null) BuildPracticeCard();
            if (!practiceCanvas.activeSelf) practiceCanvas.SetActive(true);
            int matches = rules.PracticeMatches, count = rules.Round.CanCount;
            if (matches == shownPracticeMatches && count == shownPracticeCount && shelf.Active == shownPracticeActive) return;
            shownPracticeMatches = matches; shownPracticeCount = count; shownPracticeActive = shelf.Active;
            if (matches < 0) practiceScore.text = "Проверяем…";
            else practiceScore.SetText("На месте: {0} / {1}", matches, count);
            practiceScore.color = matches == count ? Mint : Cream;
            practiceNote.text = matches == count ? (shelf.Active ? "Все совпало! Enter — подтвердить" : "Все банки на своих местах")
                : "Переставляй банки — число обновится";
        }

        private void BuildPracticeCard()
        {
            practiceCanvas = new GameObject("Cans practice feedback", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            // The legacy HUD lives in a bottom-anchored panel; this overlay needs
            // its own screen space, rather than inheriting that panel's bounds.
            practiceCanvas.transform.SetParent(rules.transform, false);
            var canvas = practiceCanvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 130;
            var scaler = practiceCanvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var card = Card(practiceCanvas.transform, "Practice matches", -256, -235, 432, 116, Ink);
            card.anchorMin = card.anchorMax = new Vector2(1, 1);
            var font = label != null ? label.font : TMP_Settings.defaultFontAsset;
            practiceScore = Text(card, "Matches", font, "", 0, 20, 400, 42, 32, Cream);
            practiceScore.fontStyle = FontStyles.Bold;
            practiceNote = Text(card, "Hint", font, "", 0, -28, 400, 28, 18, Muted);
        }

        private void OnDestroy() { if (practiceCanvas != null) Destroy(practiceCanvas); }
    }
}
