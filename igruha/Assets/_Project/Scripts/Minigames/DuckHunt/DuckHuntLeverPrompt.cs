using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>Local contextual prompt. Selection/authority remain in PlayerInteractor.</summary>
    [DisallowMultipleComponent]
    public sealed class DuckHuntLeverPrompt : MonoBehaviour
    {
        private GameObject panel;

        public void Show(bool visible)
        {
            if (panel == null && visible) Build();
            if (panel != null) panel.SetActive(visible);
        }

        private void Build()
        {
            var canvasObject = new GameObject("Lever prompt canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 35;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            panel = new GameObject("Press E to use lever", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasObject.transform,false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f,.39f);
            rect.sizeDelta = new Vector2(460,80);
            var background = panel.GetComponent<Image>();background.color = new Color(.075f,.048f,.035f,.94f);background.raycastTarget=false;
            var border = new GameObject("Gold rule",typeof(RectTransform),typeof(Image));border.transform.SetParent(panel.transform,false);
            var br=border.GetComponent<RectTransform>();br.anchorMin=new Vector2(0,1);br.anchorMax=Vector2.one;br.sizeDelta=new Vector2(0,3);br.anchoredPosition=Vector2.zero;
            border.GetComponent<Image>().color=new Color(.95f,.7f,.3f);border.GetComponent<Image>().raycastTarget=false;
            var words = new GameObject("Hint", typeof(RectTransform), typeof(TextMeshProUGUI));words.transform.SetParent(panel.transform,false);
            var tr=words.GetComponent<RectTransform>();tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=Vector2.zero;tr.offsetMax=Vector2.zero;
            var text=words.GetComponent<TextMeshProUGUI>();text.text="<color=#FFCF70><b>[E]</b></color>  Press E to use lever";
            text.fontSize=30;text.alignment=TextAlignmentOptions.Center;text.color=new Color(1,.94f,.82f);text.raycastTarget=false;
        }

        private void OnDisable() => Show(false);
    }
}
