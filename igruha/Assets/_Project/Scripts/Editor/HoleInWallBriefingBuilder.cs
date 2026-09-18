using Igruha.Core.UI;
using Igruha.Minigames.HoleInWall;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.EditorTools
{
    /// <summary>Scene-local ceramic briefing; the shared tutorial still owns timing and input.</summary>
    public static class HoleInWallBriefingBuilder
    {
        private static readonly Color Ink = new Color(.025f, .12f, .12f);
        private static readonly Color Muted = new Color(.075f, .21f, .19f);
        private static readonly Color Cream = new Color(.98f, .96f, .87f);
        private static readonly Color Teal = new Color(.035f, .30f, .25f);
        private static readonly Color Mint = new Color(.86f, .92f, .85f);

        [MenuItem("Tools/Igruha/Hole in Wall/Rebuild Briefing")]
        public static void Build()
        {
            var game = Object.FindFirstObjectByType<HoleInWallMinigame>();
            var tutorial = Object.FindFirstObjectByType<TutorialScreen>();
            if (game == null || tutorial == null || Application.isPlaying) return;
            var data = new SerializedObject(tutorial);
            var panel = (GameObject)data.FindProperty("panel").objectReferenceValue;
            var font = panel.GetComponentInChildren<TMP_Text>(true).font;
            for (int i = panel.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(panel.transform.GetChild(i).gameObject);

            var scrim = Box("Scrim", panel.transform, new Color(.025f, .12f, .14f, .25f));
            Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;
            var card = Box("Bathhouse briefing", panel.transform, Cream, UiSpriteBaker.Card);
            card.rectTransform.anchorMin = card.rectTransform.anchorMax = card.rectTransform.pivot = new Vector2(.5f, .5f);
            card.rectTransform.sizeDelta = new Vector2(1420, 880);
            var pop = card.gameObject.AddComponent<UiPop>();
            var rule = Box("Ceramic rule", card.transform, Teal);
            Place(rule.rectTransform, 48, 34, 1324, 5);
            Label("Eyebrow", card.transform, font, "АКВАПАВИЛЬОН  /  ПРАВИЛА", 20, Muted, 48, 52, 900, 30);
            var title = Label("Title", card.transform, font, "Дырка в стене", 52, Ink, 48, 92, 1324, 70);
            title.fontStyle = FontStyles.Bold;
            Label("Objective", card.transform, font, "Встань в свой вырез и повтори позу на стене.", 30, Ink, 48, 169, 1324, 44);

            var hints = data.FindProperty("hintRows");
            hints.arraySize = 4;
            string[] names = { "руки вверх", "руки в стороны", "присед", "наклон вбок" };
            for (int i = 0; i < 4; i++)
            {
                var tile = Box("Pose " + (i + 1), card.transform, Mint, UiSpriteBaker.Card);
                tile.pixelsPerUnitMultiplier = 3;
                Place(tile.rectTransform, 48 + i * 337, 237, 313, 326);
                var baseline = Box("Floor", tile.transform, new Color(.59f, .74f, .65f));
                Place(baseline.rectTransform, 42, 229, 229, 2);
                var icon = new GameObject("Wall cutout", typeof(RectTransform), typeof(HoleInWallCutoutGraphic)).GetComponent<HoleInWallCutoutGraphic>();
                icon.transform.SetParent(tile.transform, false);
                icon.color = Ink; icon.raycastTarget = false;
                var shapeData = new SerializedObject(icon);
                shapeData.FindProperty("config").objectReferenceValue = game.Config;
                shapeData.FindProperty("pose").enumValueIndex = i + 1;
                shapeData.ApplyModifiedPropertiesWithoutUndo();
                Place(icon.rectTransform, 35, -4, 243, 243);

                var key = Key(tile.transform, font, (i + 1).ToString(), 20, 257, 54, out TMP_Text keyText);
                var caption = Label("Action", tile.transform, font, names[i], 24, Ink, 86, 256, 214, 54);
                var row = tile.gameObject.AddComponent<TutorialHintRow>();
                var rowData = new SerializedObject(row);
                rowData.FindProperty("keyCap").objectReferenceValue = key.gameObject;
                rowData.FindProperty("keyText").objectReferenceValue = keyText;
                rowData.FindProperty("actionText").objectReferenceValue = caption;
                rowData.ApplyModifiedPropertiesWithoutUndo();
                hints.GetArrayElementAtIndex(i).objectReferenceValue = row;
            }

            Key(card.transform, font, "WASD", 48, 592, 136, out _);
            Label("Movement", card.transform, font, "Найди свой вырез", 27, Ink, 204, 587, 495, 40);
            Label("Movement note", card.transform, font, "Движение снимает позу", 23, Muted, 204, 627, 495, 34);
            Label("Pose instruction", card.transform, font, "Остановись и нажми 1–4", 27, Ink, 756, 587, 616, 40);
            Label("Crouch note", card.transform, font, "Присед — клавиша 3, а не Ctrl", 23, Muted, 756, 627, 616, 34);
            var pairNote = Box("Rope note", card.transform, new Color(.92f, .90f, .78f), UiSpriteBaker.Card);
            pairNote.pixelsPerUnitMultiplier = 4;
            Place(pairNote.rectTransform, 48, 696, 1324, 70);
            Label("Pair label", pairNote.transform, font, "В ПАРЕ", 18, Teal, 22, 17, 108, 36).fontStyle = FontStyles.Bold;
            Label("Pair rule", pairNote.transform, font, "Трос держит вас вместе. Ошибка одного — в воду падают оба.", 24, Ink, 145, 15, 1157, 40);

            var track = Box("Countdown track", card.transform, Mint, UiSpriteBaker.Chip);
            Place(track.rectTransform, 48, 818, 714, 8);
            var fill = Box("Countdown", track.transform, Teal, UiSpriteBaker.Chip);
            Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0; fill.fillAmount = 1;
            var skip = Label("Start hint", card.transform, font, "Любая клавиша — начать", 23, Muted, 826, 795, 546, 50);
            skip.alignment = TextAlignmentOptions.MidlineRight;

            data.FindProperty("titleText").objectReferenceValue = title;
            data.FindProperty("objectiveText").objectReferenceValue = null;
            data.FindProperty("categoryText").objectReferenceValue = null;
            data.FindProperty("card").objectReferenceValue = card.rectTransform;
            data.FindProperty("cardPop").objectReferenceValue = pop;
            data.FindProperty("timerFill").objectReferenceValue = fill;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(tutorial.gameObject.scene);
        }

        private static Image Key(Transform parent, TMP_FontAsset font, string value, float x, float y, float width, out TMP_Text text)
        {
            var edge = Box("Key " + value, parent, Ink, UiSpriteBaker.KeyCap);
            Place(edge.rectTransform, x, y, width, 54);
            var face = Box("Enamel", edge.transform, Teal, UiSpriteBaker.KeyCap);
            Place(face.rectTransform, 2, 1, width - 4, 47);
            text = Label("Key text", face.transform, font, value, 27, Cream, 0, 0, width - 4, 47);
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            return edge;
        }

        private static Image Box(string name, Transform parent, Color color, string sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
            if (sprite != null) { image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(sprite); image.type = Image.Type.Sliced; }
            return image;
        }

        private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, string value, float size, Color color, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.color = color; text.text = value;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.raycastTarget = false;
            Place(text.rectTransform, x, y, width, height);
            return text;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
