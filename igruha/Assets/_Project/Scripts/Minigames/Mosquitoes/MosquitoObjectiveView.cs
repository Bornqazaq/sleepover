using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.Mosquitoes
{
    /// <summary>Local objective marker and sleep meter on the existing game canvas.</summary>
    public sealed class MosquitoObjectiveView
    {
        private readonly GameObject root;
        private readonly RectTransform canvas, marker;
        private readonly TMP_Text label;
        private readonly Image sleepFill, biteFill;
        private readonly Canvas owner;
        private Camera camera;

        public MosquitoObjectiveView(TMP_Text status)
        {
            owner = status.GetComponentInParent<Canvas>();
            canvas = owner.transform as RectTransform;
            root = new GameObject("MosquitoObjectives", typeof(RectTransform));
            var rect = root.transform as RectTransform;
            rect.SetParent(canvas, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            sleepFill = Bar("Sleep", rect, new Vector2(.5f, 1), new Vector2(0, -98), new Vector2(310, 7), new Color(.45f, .73f, 1));
            biteFill = Bar("Bite", rect, new Vector2(.5f, .5f), new Vector2(0, -62), new Vector2(100, 6), new Color(1, .64f, .35f));
            var markerObject = new GameObject("Target", typeof(RectTransform), typeof(TextMeshProUGUI));
            marker = markerObject.transform as RectTransform; marker.SetParent(rect, false); marker.sizeDelta = new Vector2(200, 54);
            label = markerObject.GetComponent<TextMeshProUGUI>(); label.font = status.font; label.fontSize = 19;
            label.alignment = TextAlignmentOptions.Center; label.color = new Color(1, .82f, .45f); label.raycastTarget = false;
            camera = Camera.main;
        }
        private static Image Bar(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var track = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = track.transform as RectTransform; rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            var image = track.GetComponent<Image>(); image.color = new Color(.025f, .035f, .065f, .8f); image.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var fillRect = fill.transform as RectTransform; fillRect.SetParent(rect, false); fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one; fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            var result = fill.GetComponent<Image>(); result.color = color; result.raycastTarget = false;
            return result;
        }
        public void Paint(MosquitoesMinigame game, MosquitoBody body)
        {
            sleepFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(game.Sleep.Sleep / game.Config.SleepTarget), 1);
            bool biting = body != null && body.IsAlive && body.IsAttached;
            biteFill.transform.parent.gameObject.SetActive(biting);
            biteFill.rectTransform.anchorMax = new Vector2(biting ? body.BiteFraction : 0, 1);
            bool show = body != null && body.IsAlive && game.Countdown <= 0 &&
                (!body.HasEntered || game.Sleep.CanBite && body.BiteCooldown <= 0);
            marker.gameObject.SetActive(show);
            if (!show) return;
            if (camera == null) camera = Camera.main;
            if (camera == null) return;
            Vector3 target = body.HasEntered ? game.BiteSurface(body.Id) : MosquitoWindowEntry.Approach;
            Vector3 screen = camera.WorldToScreenPoint(target + Vector3.up * .2f);
            if (screen.z < 0) { screen.x = Screen.width - screen.x; screen.y = Screen.height - screen.y; }
            screen.x = Mathf.Clamp(screen.x, 110, Screen.width - 110);
            screen.y = Mathf.Clamp(screen.y, 130, Screen.height - 160);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen,
                owner.renderMode == RenderMode.ScreenSpaceOverlay ? null : owner.worldCamera, out Vector2 point);
            marker.anchoredPosition = point;
            float distance = Vector3.Distance(body.Position, target);
            if (!body.HasEntered) label.SetText("ОКНО\n{0:0} м", distance);
            else if (biting) label.SetText("");
            else if (distance < game.Config.BiteDistance) label.SetText("ДЕРЖИ ЛКМ");
            else label.SetText("УКУС\n{0:0.0} м", distance);
        }
        public void Dispose() { if (root != null) Object.Destroy(root); }
    }
}
