using Igruha.Core.CameraSystems;
using Igruha.Core.Audio;
using Igruha.Core.Player;
using Igruha.Core.UI;
using Igruha.Minigames.HoleInWall;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.EditorTools
{
    /// <summary>Единственный сборщик итогов для сохранённых сцен и билдеров арен.</summary>
    public static class UnifiedResultsBuilder
    {
        private const int Capacity = 8;
        private const float Width = 1000f, RowHeight = 66f;
        private static readonly Color Muted = new Color(.29f, .40f, .37f);

        private const string PrefabPath = "Assets/_Project/Prefabs/UI/UnifiedRoundResults.prefab";

        public static void BuildPrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "UI");
            var root = new GameObject("ResultsTemplate", typeof(RectTransform));
            try
            {
                var hud = root.AddComponent<RoundHud>();
                var panel = new GameObject("ResultsPanel", typeof(RectTransform));
                panel.transform.SetParent(root.transform, false); Stretch((RectTransform)panel.transform);
                var data = new SerializedObject(hud); Assign(data, "resultsPanel", panel); data.ApplyModifiedPropertiesWithoutUndo();
                BuildContents(hud);
                PrefabUtility.SaveAsPrefabAsset(panel, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        public static void Apply(RoundHud hud)
        {
            var data = new SerializedObject(hud);
            var panel = data.FindProperty("resultsPanel").objectReferenceValue as GameObject;
            if (panel == null) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { BuildPrefab(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
            if (PrefabUtility.GetCorrespondingObjectFromSource(panel) != prefab)
            {
                var parent = panel.transform.parent;
                int index = panel.transform.GetSiblingIndex();
                Object.DestroyImmediate(panel);
                panel = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                panel.transform.SetSiblingIndex(index);
            }
            var view = panel.GetComponent<RoundResultsView>();
            var card = (RectTransform)panel.transform.Find("ResultsCard");
            var hole = Find<HoleInWallMinigame>(hud);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(hole != null ? 1f : .5f, .5f);
            card.anchoredPosition = new Vector2(hole != null ? -48 : 0, 0);
            panel.transform.Find("Scrim").GetComponent<Image>().color = new Color(.02f, .06f, .07f, hole != null ? .16f : .62f);
            Assign(data, "resultsPanel", panel); Assign(data, "resultsView", view);
            Assign(data, "resultsTitle", card.Find("Title").GetComponent<TMP_Text>()); Assign(data, "resultsText", null);
            Assign(data, "restartButton", card.Find("RestartButton").GetComponent<Button>());
            Assign(data, "resultsCamera", Find<ThirdPersonCameraRig>(hud));
            data.FindProperty("resultRows").arraySize = 0; data.ApplyModifiedPropertiesWithoutUndo();
            if (hole != null)
            {
                var old = panel.transform.Find("PlatformWinners");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                BuildPodiumCaption(hud, hole, panel.transform);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(card);
            PrefabUtility.RecordPrefabInstancePropertyModifications(panel.transform.Find("Scrim").GetComponent<Image>());
            panel.SetActive(false);
        }

        private static void BuildContents(RoundHud hud)
        {
            var data = new SerializedObject(hud);
            var panel = data.FindProperty("resultsPanel").objectReferenceValue as GameObject;
            if (panel == null) return;
            var canvas = panel.GetComponent<Canvas>();
            if (canvas == null) canvas = panel.AddComponent<Canvas>();
            canvas.overrideSorting = true; canvas.sortingOrder = 1000;
            if (panel.GetComponent<GraphicRaycaster>() == null) panel.AddComponent<GraphicRaycaster>();
            for (int i = panel.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(panel.transform.GetChild(i).gameObject);
            var view = panel.GetComponent<RoundResultsView>();
            if (view == null) view = panel.AddComponent<RoundResultsView>();
            var fields = new SerializedObject(view);
            var scrim = Image("Scrim", panel.transform, new Color(.02f, .06f, .07f, .62f));
            Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;
            var card = Image("ResultsCard", panel.transform, MinigameUiStyle.Paper, true);
            var rect = card.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0, 0);
            rect.sizeDelta = new Vector2(Width, 100);
            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 26, 24);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var rule = Image("Accent", card.transform, MinigameUiStyle.Ink);
            Height(rule.gameObject, 4);
            var eyebrow = Label("Eyebrow", card.transform, "ИТОГИ РАУНДА", 17, Muted, 27);
            eyebrow.characterSpacing = 2;
            var title = Label("Title", card.transform, "Раунд завершён", 44, MinigameUiStyle.Ink, 62, true);
            var subtitle = Label("Subtitle", card.transform, "", 22, Muted, 32);
            var legend = new GameObject("Columns", typeof(RectTransform));
            legend.transform.SetParent(card.transform, false);
            Height(legend, 28);
            Cell("PlaceHeader", legend.transform, "МЕСТО", 16, 14, 0, 80, 28);
            Cell("NameHeader", legend.transform, "УЧАСТНИК", 16, 164, 0, 350, 28);
            var metric = Cell("MetricHeader", legend.transform, "РЕЗУЛЬТАТ", 16, 580, 0, 150, 28, true);
            var points = Cell("PointsHeader", legend.transform, "ОЧКИ", 16, 748, 0, 76, 28, true);
            var total = Cell("TotalHeader", legend.transform, "СЕРИЯ", 16, 842, 0, 76, 28, true);
            var rows = fields.FindProperty("rows");
            rows.arraySize = Capacity;
            for (int i = 0; i < Capacity; i++) rows.GetArrayElementAtIndex(i).objectReferenceValue = Row(card.transform, i);
            var buttonImage = Image("RestartButton", card.transform, MinigameUiStyle.Ink, true);
            Height(buttonImage.gameObject, 62);
            buttonImage.raycastTarget = true;
            var button = buttonImage.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            var colors = button.colors;
            colors.highlightedColor = new Color(.80f, 1, .88f);
            colors.pressedColor = new Color(.55f, .82f, .70f);
            button.colors = colors;
            var buttonText = Label("Label", button.transform, "Ещё раз", 26, MinigameUiStyle.Paper, 0, true);
            buttonText.alignment = TextAlignmentOptions.Center;
            Stretch(buttonText.rectTransform);
            button.gameObject.AddComponent<UiButtonSound>();
            var footer = Label("Transition", card.transform, "", 20, Muted, 34);
            footer.alignment = TextAlignmentOptions.Center;
            Assign(fields, "eyebrow", eyebrow); Assign(fields, "title", title); Assign(fields, "subtitle", subtitle);
            Assign(fields, "metricHeader", metric); Assign(fields, "pointsHeader", points); Assign(fields, "totalHeader", total);
            Assign(fields, "footer", footer);
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            Assign(fields, "roster", roster);
            var faces = fields.FindProperty("portraits");
            faces.arraySize = roster.Characters.Count;
            for (int i = 0; i < faces.arraySize; i++) faces.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Sprite>(CharacterPortraitCapture.Folder + "/" + roster.Characters[i].DisplayName + "_Face.png");
            fields.ApplyModifiedPropertiesWithoutUndo();
            Assign(data, "resultsView", view); Assign(data, "resultsTitle", title); Assign(data, "resultsText", null);
            Assign(data, "restartButton", button); Assign(data, "resultsCamera", Find<ThirdPersonCameraRig>(hud));
            data.FindProperty("resultRows").arraySize = 0;
            data.ApplyModifiedPropertiesWithoutUndo();
            panel.SetActive(false);
        }

        private static ResultRow Row(Transform parent, int index)
        {
            var background = Image("PlayerRow" + index, parent, Color.white, true);
            Height(background.gameObject, RowHeight);
            var row = background.gameObject.AddComponent<ResultRow>();
            var data = new SerializedObject(row);
            var accent = Image("Accent", background.transform, MinigameUiStyle.Ink);
            Position(accent.rectTransform, 8, 0, 4, 36);
            var place = Cell("Place", background.transform, "01", 29, 20, 0, 62, 52);
            var face = Image("Portrait", background.transform, Color.white);
            Position(face.rectTransform, 88, 0, 60, 60); face.preserveAspect = true;
            var name = Cell("PlayerName", background.transform, "", 25, 164, 11, 346, 34);
            name.enableAutoSizing = true; name.fontSizeMin = 20; name.fontSizeMax = 25;
            var note = Cell("Detail", background.transform, "", 16, 164, -19, 400, 24);
            note.color = Muted;
            var mark = Image("LocalMark", background.transform, MinigameUiStyle.Ink, true);
            Position(mark.rectTransform, 518, 11, 48, 27);
            var you = Label("You", mark.transform, "ВЫ", 13, MinigameUiStyle.Paper, 0, true);
            you.alignment = TextAlignmentOptions.Center; Stretch(you.rectTransform);
            var value = Cell("Value", background.transform, "—", 28, 580, 0, 150, 54, true);
            var points = Cell("Points", background.transform, "", 29, 748, 0, 76, 54, true);
            var total = Cell("Total", background.transform, "", 26, 842, 0, 76, 54, true);
            total.color = Muted;
            Assign(data, "background", background); Assign(data, "accent", accent); Assign(data, "placeText", place);
            Assign(data, "portrait", face); Assign(data, "nameText", name); Assign(data, "noteText", note);
            Assign(data, "localMark", mark.gameObject); Assign(data, "valueText", value);
            Assign(data, "pointsText", points); Assign(data, "totalText", total);
            data.ApplyModifiedPropertiesWithoutUndo();
            row.Clear();
            return row;
        }

        private static void BuildPodiumCaption(RoundHud hud, HoleInWallMinigame game, Transform panel)
        {
            var caption = Label("PlatformWinners", panel, "ПОБЕДИТЕЛИ", 40, MinigameUiStyle.Paper, 0, true);
            Position(caption.rectTransform, 100, -340, 690, 130);
            caption.textWrappingMode = TextWrappingModes.Normal;
            var shadow = caption.gameObject.AddComponent<Shadow>();
            shadow.effectColor = MinigameUiStyle.Ink; shadow.effectDistance = new Vector2(2, -2);
            var view = hud.GetComponent<HoleInWallResultsPanel>();
            if (view == null) view = hud.gameObject.AddComponent<HoleInWallResultsPanel>();
            var fields = new SerializedObject(view);
            Assign(fields, "game", game); Assign(fields, "platformCaption", caption);
            fields.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T Find<T>(Component source) where T : Component
        {
            foreach (var root in source.gameObject.scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }

        internal static void Assign(SerializedObject data, string field, Object value) => data.FindProperty(field).objectReferenceValue = value;
        internal static Image Image(string name, Transform parent, Color color, bool rounded = false)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
            if (rounded) { image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiSpriteBaker.Card); image.type = UnityEngine.UI.Image.Type.Sliced; image.pixelsPerUnitMultiplier = 3; }
            return image;
        }
        internal static TMP_Text Label(string name, Transform parent, string value, float size, Color color, float height = 0, bool bold = false)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>(); text.font = bold ? UiFonts.SansBold : UiFonts.SansMedium;
            text.fontSize = size; text.color = color; text.text = value;
            text.alignment = TextAlignmentOptions.MidlineLeft; text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false;
            if (height > 0) Height(go, height);
            return text;
        }
        private static TMP_Text Cell(string name, Transform parent, string value, float size, float x, float y, float width, float height, bool right = false)
        {
            var text = Label(name, parent, value, size, MinigameUiStyle.Ink);
            Position(text.rectTransform, x, y, width, height);
            if (right) text.alignment = TextAlignmentOptions.MidlineRight;
            return text;
        }
        internal static void Height(GameObject go, float height)
        {
            var element = go.AddComponent<LayoutElement>(); element.minHeight = element.preferredHeight = height; element.flexibleHeight = 0;
        }
        internal static void Position(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, .5f);
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
        }
        internal static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
