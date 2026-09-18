using Igruha.Core.UI;
using Igruha.Minigames.HoleInWall;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.EditorTools
{
    /// <summary>Керамическая карточка результатов поверх стандартного HUD.</summary>
    internal static class HoleInWallResultsBuilder
    {
        private static readonly Color Ink = new Color(.055f, .22f, .22f);
        private static readonly Color Muted = new Color(.24f, .39f, .37f);
        private static readonly Color Cream = new Color(.98f, .96f, .87f);
        private const int Capacity = 8;

        internal static void Build()
        {
            var hud = Object.FindFirstObjectByType<RoundHud>();
            var game = Object.FindFirstObjectByType<HoleInWallMinigame>();
            var data = new SerializedObject(hud);
            var panel = (GameObject)data.FindProperty("resultsPanel").objectReferenceValue;
            var font = panel.GetComponentInChildren<TMP_Text>(true).font;
            for (int i = panel.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(panel.transform.GetChild(i).gameObject);
            var previous = hud.GetComponent<HoleInWallResultsPanel>();
            if (previous != null) Object.DestroyImmediate(previous);

            var scrim = Image("Scrim", panel.transform, new Color(.025f, .12f, .14f, .12f));
            Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;

            var card = Image("Bathhouse results", panel.transform, Cream, UiSpriteBaker.Card);
            var rect = card.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, .5f);
            rect.anchoredPosition = new Vector2(-56, 0);
            rect.sizeDelta = new Vector2(800, 100);
            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(38, 38, 30, 26);
            layout.spacing = 10;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var accent = Image("Ceramic rule", card.transform, Ink);
            Height(accent.gameObject, 5);
            Label("Eyebrow", card.transform, font, "ДЫРКА В СТЕНЕ  /  ИТОГИ", 19, Muted, 28);
            var title = Label("Title", card.transform, font, "Раунд завершён", 46, Ink, 62);
            title.fontStyle = FontStyles.Bold;
            var subtitle = Label("Subtitle", card.transform, font, "", 22, Muted, 30);
            var legend = new GameObject("Columns", typeof(RectTransform));
            legend.transform.SetParent(card.transform, false);
            Height(legend, 28);
            Position(Label("Place header", legend.transform, font, "МЕСТО", 16, Muted).rectTransform, 22, 0, 75, 28);
            Position(Label("Player header", legend.transform, font, "УЧАСТНИК", 16, Muted).rectTransform, 150, 0, 350, 28);
            var wallsHeader = Label("Walls header", legend.transform, font, "СТЕНЫ", 16, Muted);
            Position(wallsHeader.rectTransform, 590, 0, 110, 28);
            wallsHeader.alignment = TextAlignmentOptions.Right;

            var caption = Label("Platform winners", panel.transform, font, "ПОБЕДИТЕЛИ", 42, Cream);
            caption.fontStyle = FontStyles.Bold;
            caption.textWrappingMode = TextWrappingModes.Normal;
            Position(caption.rectTransform, 110, -340, 750, 130);
            var shadow = caption.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(.015f, .08f, .08f, .9f);
            shadow.effectDistance = new Vector2(2, -2);
            var view = hud.gameObject.AddComponent<HoleInWallResultsPanel>();
            var viewData = new SerializedObject(view);
            viewData.FindProperty("game").objectReferenceValue = game;
            viewData.FindProperty("roundPlates").objectReferenceValue = hud.GetComponentInParent<Canvas>().transform.Find("_HudPlates").gameObject;
            viewData.FindProperty("title").objectReferenceValue = title;
            viewData.FindProperty("platformCaption").objectReferenceValue = caption;
            var roster = AssetDatabase.LoadAssetAtPath<Igruha.Core.Player.CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            viewData.FindProperty("roster").objectReferenceValue = roster;
            var portraits = viewData.FindProperty("portraits");
            portraits.arraySize = roster.Characters.Count;
            for (int i = 0; i < portraits.arraySize; i++)
                portraits.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(
                    CharacterPortraitCapture.Folder + "/" + roster.Characters[i].DisplayName + "_Face.png");
            viewData.FindProperty("subtitle").objectReferenceValue = subtitle;
            viewData.FindProperty("displaySeconds").floatValue = new SerializedObject(game).FindProperty("resultsDisplaySeconds").floatValue;
            var rows = viewData.FindProperty("rows");
            rows.arraySize = Capacity;
            for (int i = 0; i < Capacity; i++)
            {
                var row = Image("Standing " + i, card.transform, Color.white, UiSpriteBaker.Card);
                row.pixelsPerUnitMultiplier = 4;
                Height(row.gameObject, 58);
                var stripe = Image("Lane accent", row.transform, Color.white);
                Position(stripe.rectTransform, 10, 0, 4, 34);
                var place = Label("Place", row.transform, font, "01", 30, Ink);
                Position(place.rectTransform, 22, 0, 60, 54);
                place.fontStyle = FontStyles.Bold;
                var portrait = Image("Character", row.transform, Color.white);
                Position(portrait.rectTransform, 88, 0, 54, 54);
                portrait.preserveAspect = true;
                var name = Label("Player", row.transform, font, "", 26, Ink);
                Position(name.rectTransform, 154, 9, 400, 32);
                name.richText = false;
                name.overflowMode = TextOverflowModes.Ellipsis;
                var lane = Label("Lane", row.transform, font, "", 13, Muted);
                Position(lane.rectTransform, 154, -17, 400, 19);
                var score = Label("Walls", row.transform, font, "", 31, Ink);
                Position(score.rectTransform, 590, 0, 110, 50);
                score.alignment = TextAlignmentOptions.Right;
                var r = rows.GetArrayElementAtIndex(i);
                r.FindPropertyRelative("root").objectReferenceValue = row.gameObject;
                r.FindPropertyRelative("place").objectReferenceValue = place;
                r.FindPropertyRelative("playerName").objectReferenceValue = name;
                r.FindPropertyRelative("score").objectReferenceValue = score;
                r.FindPropertyRelative("lane").objectReferenceValue = lane;
                r.FindPropertyRelative("background").objectReferenceValue = row;
                r.FindPropertyRelative("accent").objectReferenceValue = stripe;
                r.FindPropertyRelative("portrait").objectReferenceValue = portrait;
                row.gameObject.SetActive(false);
            }
            var buttonImage = Image("RestartButton", card.transform, Ink, UiSpriteBaker.Card);
            buttonImage.pixelsPerUnitMultiplier = 3;
            buttonImage.raycastTarget = true;
            Height(buttonImage.gameObject, 66);
            var button = buttonImage.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            var colors = button.colors;
            colors.highlightedColor = new Color(.78f, 1, .92f);
            colors.pressedColor = new Color(.55f, .8f, .72f);
            button.colors = colors;
            var buttonText = Label("Label", buttonImage.transform, font, "Ещё раз", 29, Cream);
            buttonText.fontStyle = FontStyles.Bold;
            buttonText.alignment = TextAlignmentOptions.Center;
            Stretch(buttonText.rectTransform);
            var footer = Label("Return countdown", card.transform, font, "", 19, Muted, 30);
            footer.alignment = TextAlignmentOptions.Center;
            viewData.FindProperty("footer").objectReferenceValue = footer;
            viewData.ApplyModifiedPropertiesWithoutUndo();
            data.FindProperty("restartButton").objectReferenceValue = button;
            data.FindProperty("resultsCamera").objectReferenceValue = Object.FindFirstObjectByType<Igruha.Core.CameraSystems.ThirdPersonCameraRig>(FindObjectsInactive.Include);
            data.FindProperty("resultRows").arraySize = 0;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Image Image(string name, Transform parent, Color color, string sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null)
            {
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(sprite);
                image.type = UnityEngine.UI.Image.Type.Sliced;
            }
            return image;
        }

        private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, string value, float size, Color color, float height = 0)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.color = color; text.text = value;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            if (height > 0) Height(go, height);
            return text;
        }

        private static void Height(GameObject go, float height)
        {
            var element = go.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = height;
            element.flexibleHeight = 0;
        }

        private static void Position(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, .5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
