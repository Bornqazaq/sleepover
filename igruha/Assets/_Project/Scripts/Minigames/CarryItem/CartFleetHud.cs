using Igruha.Core.Session;
using Igruha.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Two vessel silhouettes per original team. Reads replicated stock even after theft.</summary>
    public sealed class CartFleetHud : MonoBehaviour
    {
        private CarryItemMinigame game;
        private Canvas canvas;
        private CartFleetGraphic graphic;
        private readonly TMP_Text[] labels = new TMP_Text[2];
        private readonly int[] stock = { -1, -1 };
        private readonly bool[] waiting = new bool[2];
        public void Bind(CarryItemMinigame owner)
        {
            game = owner;
            canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 45;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var root = new GameObject("Fleet panel", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(transform, false); root.anchorMin = root.anchorMax = root.pivot = new Vector2(0, 1);
            root.anchoredPosition = new Vector2(24, -24); root.sizeDelta = new Vector2(246, 128);
            graphic = root.gameObject.AddComponent<CartFleetGraphic>(); graphic.raycastTarget = false;
            Label(root, "ЗАПАС ТЕЛЕЖЕК", new Vector2(14, -16), 16, new Vector2(220, 26));
            for (int i = 0; i < 2; i++) labels[i] = Label(root, "", new Vector2(14, -53 - i * 42), 20, new Vector2(96, 28));
        }
        private static TMP_Text Label(RectTransform root, string text, Vector2 at, float size, Vector2 dimensions)
        {
            var go = new GameObject("Fleet label", typeof(RectTransform)); go.transform.SetParent(root, false);
            var label = go.AddComponent<TextMeshProUGUI>(); label.text = text; label.fontSize = size;
            label.color = MinigameUiStyle.OnDark; label.raycastTarget = false;
            var rect = label.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = at; rect.sizeDelta = dimensions; return label;
        }
        private void LateUpdate()
        {
            canvas.enabled = game.GameplayActive;
            if (!canvas.enabled) return;
            for (int i = 0; i < 2; i++)
            {
                var cart = game.CartOf(i == 0 ? TeamSide.A : TeamSide.B);
                int count = cart != null ? cart.RemainingCarts : 0;
                bool pending = cart != null && cart.IsLost && !cart.IsDepleted;
                if (stock[i] == count && waiting[i] == pending) continue;
                stock[i] = count; waiting[i] = pending;
                labels[i].text = (i == 0 ? "А" : "Б") + "  " + count + "/2";
                graphic.SetStock(i, count, pending);
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CartFleetGraphic : MaskableGraphic
    {
        private readonly int[] stock = new int[2];
        private readonly bool[] waiting = new bool[2];
        public void SetStock(int team, int count, bool pending)
        { stock[team] = count; waiting[team] = pending; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); Box(vh, new Vector2(123, -64), new Vector2(246, 128), MinigameUiStyle.HudSurface);
            for (int row = 0; row < 2; row++) for (int slot = 0; slot < CarryItemConfig.CartsPerTeam; slot++)
            {
                bool available = slot >= CarryItemConfig.CartsPerTeam - stock[row];
                Color color = available ? TeamPalette.ColorOf(row == 0 ? TeamSide.A : TeamSide.B) : new Color(.3f, .34f, .36f);
                if (available && waiting[row]) color = new Color(1, .76f, .35f);
                Vector2 p = new Vector2(134 + slot * 57, -63 - row * 42);
                Box(vh, p, new Vector2(33, 16), color);
                Box(vh, p + new Vector2(-20, 10), new Vector2(9, 3), color);
                Box(vh, p + new Vector2(-16, 5), new Vector2(3, 10), color);
                Box(vh, p + new Vector2(-11, -12), new Vector2(7, 7), color);
                Box(vh, p + new Vector2(11, -12), new Vector2(7, 7), color);
                if (!available)
                {
                    for (int i = -17; i <= 17; i += 2) Box(vh, p + new Vector2(i, i * .7f), new Vector2(4, 4), new Color(.95f, .38f, .32f));
                }
            }
        }
        private static void Box(VertexHelper vh, Vector2 p, Vector2 size, Color c)
        {
            int n = vh.currentVertCount; Vector2 h = size * .5f;
            vh.AddVert(p + new Vector2(-h.x, -h.y), c, Vector2.zero); vh.AddVert(p + new Vector2(-h.x, h.y), c, Vector2.zero);
            vh.AddVert(p + new Vector2(h.x, h.y), c, Vector2.zero); vh.AddVert(p + new Vector2(h.x, -h.y), c, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
        }
    }
}
