using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>Табло личного турнира. Обновляется событиями, анимация не создаёт строк.</summary>
    public sealed class BelieveTournamentHud : MonoBehaviour
    {
        private static readonly Color Ink = new Color(.055f, .075f, .073f, .97f);
        private static readonly Color Gold = new Color(.94f, .75f, .43f);
        private static readonly Color Paper = new Color(.91f, .9f, .84f);
        private TMP_FontAsset font;
        private Sprite sprite;
        private GameObject board, history, finals, banner, crown;
        private TMP_Text boardTitle, boardRows, boardFooter, streak, reputation, finalNames, finalScore;
        private TMP_Text bannerTitle, bannerName, bannerFooter;
        private CanvasGroup bannerGroup;
        private RectTransform bannerRect;
        private float appearedAt;
        private const float EntranceSeconds = .45f;
        public bool BannerVisible => banner != null && banner.activeSelf;

        public void Initialize(TMP_FontAsset typeface, Sprite plateSprite)
        {
            font = typeface; sprite = plateSprite;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 17;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            board = Plate("Standings", new Vector2(0, 1), new Vector2(28, -252), new Vector2(410, 438));
            Accent(board.transform, new Vector2(0, -4), new Vector2(356, 2));
            boardTitle = Text("Title", board.transform, 21, new Vector2(0, -33), new Vector2(374, 34), Gold);
            Text("Columns", board.transform, 15, new Vector2(0, -68), new Vector2(370, 24), Paper).text =
                "ИГРОК<pos=64%>ПОБЕДЫ<pos=82%>КОНЫ";
            boardRows = Text("Rows", board.transform, 21, new Vector2(0, -216), new Vector2(370, 270), Paper);
            boardRows.alignment = TextAlignmentOptions.TopLeft;
            boardRows.lineSpacing = 13;
            boardFooter = Text("Footer", board.transform, 17, new Vector2(0, -375), new Vector2(374, 30), Gold);
            streak = Text("Streak", board.transform, 16, new Vector2(0, -409), new Vector2(374, 30), Paper);
            history = Plate("Reputation", new Vector2(.5f, 0), new Vector2(0, 326), new Vector2(750, 46));
            reputation = Text("History", history.transform, 18, new Vector2(0, -23), new Vector2(728, 36), Paper);
            reputation.alignment = TextAlignmentOptions.Center;
            finals = Plate("FinalScore", new Vector2(1, 1), new Vector2(-28, -22), new Vector2(470, 118));
            finalNames = Text("Names", finals.transform, 18, new Vector2(0, -29), new Vector2(438, 32), Paper);
            finalNames.alignment = TextAlignmentOptions.Center;
            finalScore = Text("Score", finals.transform, 38, new Vector2(0, -77), new Vector2(438, 66), Gold);
            finalScore.alignment = TextAlignmentOptions.Center;
            banner = Plate("FinalBanner", new Vector2(.5f, .5f), Vector2.zero, new Vector2(990, 382));
            bannerRect = (RectTransform)banner.transform;
            bannerGroup = banner.AddComponent<CanvasGroup>();
            bannerGroup.blocksRaycasts = false; bannerGroup.interactable = false;
            Accent(banner.transform, new Vector2(0, -16), new Vector2(890, 3));
            Accent(banner.transform, new Vector2(0, -365), new Vector2(890, 3));
            bannerTitle = Text("Title", banner.transform, 23, new Vector2(0, -59), new Vector2(920, 40), Gold);
            bannerName = Text("Name", banner.transform, 56, new Vector2(0, -195), new Vector2(914, 108), Paper);
            bannerName.enableAutoSizing = true; bannerName.fontSizeMin = 34; bannerName.fontSizeMax = 56;
            bannerFooter = Text("Footer", banner.transform, 21, new Vector2(0, -305), new Vector2(882, 82), Gold);
            bannerFooter.enableAutoSizing = true; bannerFooter.fontSizeMin = 16; bannerFooter.fontSizeMax = 21;
            bannerTitle.alignment = bannerName.alignment = bannerFooter.alignment = TextAlignmentOptions.Center;
            crown = new GameObject("Crown", typeof(RectTransform), typeof(BelieveCrownGraphic));
            crown.transform.SetParent(banner.transform, false);
            var cr = (RectTransform)crown.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(.5f, 1); cr.anchoredPosition = new Vector2(0, -116);
            cr.sizeDelta = new Vector2(90, 54);
            crown.GetComponent<BelieveCrownGraphic>().color = Gold;
            Close();
        }

        private GameObject Plate(string title, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(title, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = r.pivot = anchor;
            if (anchor.y == 0) r.pivot = new Vector2(.5f, 1);
            r.anchoredPosition = position; r.sizeDelta = size;
            var image = go.GetComponent<Image>(); image.sprite = sprite; image.type = Image.Type.Sliced;
            image.color = Ink; image.raycastTarget = false;
            return go;
        }

        private TMP_Text Text(string name, Transform parent, float size, Vector2 position, Vector2 bounds, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, 1); r.anchoredPosition = position; r.sizeDelta = bounds;
            var text = go.GetComponent<TMP_Text>(); text.font = font; text.fontSize = size; text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft; text.raycastTarget = false;
            return text;
        }

        private static void Accent(Transform parent, Vector2 position, Vector2 bounds)
        {
            var go = new GameObject("GoldRule", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, 1); r.anchoredPosition = position; r.sizeDelta = bounds;
            var image = go.GetComponent<Image>(); image.color = Gold; image.raycastTarget = false;
        }

        public void ShowStandings(string title, string rows, string footer, string predictions, bool visible)
        {
            board.SetActive(visible);
            if (!visible) return;
            boardTitle.text = title; boardRows.text = rows; boardFooter.text = footer; streak.text = predictions;
        }

        public void ShowReputation(string text, bool visible)
        { history.SetActive(visible); if (visible) reputation.text = text; }

        public void ShowFinalScore(bool visible, string a, string b, int winsA, int winsB)
        {
            finals.SetActive(visible);
            if (!visible) return;
            finalNames.text = a + "   /   " + b;
            finalScore.text = winsA + " : " + winsB;
        }

        public void ShowBanner(string title, string name, string footer, bool winner)
        {
            if (!banner.activeSelf) { appearedAt = Time.unscaledTime; bannerGroup.alpha = 0; banner.SetActive(true); }
            bannerTitle.text = title; bannerName.text = name; bannerFooter.text = footer;
            crown.SetActive(winner);
        }
        public void HideBanner() { if (banner != null) banner.SetActive(false); }
        public void Close()
        { board?.SetActive(false); history?.SetActive(false); finals?.SetActive(false); HideBanner(); }

        private void Update()
        {
            if (banner == null || !banner.activeSelf) return;
            float t = Mathf.Clamp01((Time.unscaledTime - appearedAt) / EntranceSeconds);
            float smooth = 1f - Mathf.Pow(1f - t, 3f);
            bannerGroup.alpha = smooth;
            bannerRect.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, smooth);
        }
    }

    internal sealed class BelieveCrownGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Add(vh, r, 0, .85f); Add(vh, r, .23f, .55f); Add(vh, r, .5f, 1);
            Add(vh, r, .77f, .55f); Add(vh, r, 1, .85f); Add(vh, r, .86f, .12f);
            Add(vh, r, .14f, .12f); Add(vh, r, .5f, .35f);
            for (int i = 0; i < 7; i++) vh.AddTriangle(7, i, (i + 1) % 7);
        }
        private void Add(VertexHelper vh, Rect r, float x, float y)
        { vh.AddVert(new Vector3(r.xMin + x * r.width, r.yMin + y * r.height), color, Vector2.zero); }
    }
}
