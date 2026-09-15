using System;
using System.Linq;
using Igruha.Core.Hub;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Rebuilds only the television's presentation; catalog and session behaviour stay on ConsoleMenu.</summary>
    public static class HubConsoleMenuBuilder
    {
        private static readonly Color Ink = Hex("101E23"), Panel = Hex("1B3035"), Line = Hex("354C4E");
        private static readonly Color Cream = Hex("F4ECD9"), Muted = Hex("A4B8B5"), Accent = Hex("F4BE81");
        private static TMP_FontAsset regular, bold;

        [MenuItem("Igruha/Хаб/ТВ — библиотека игр и камера")]
        public static void Rebuild()
        {
            Apply();
            HubConsoleCameraPass.Apply();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("Console library and TV camera rebuilt.");
        }

        public static void Apply()
        {
            if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != "Assets/_Project/Scenes/Hub.unity")
                throw new InvalidOperationException("Open Hub outside Play Mode.");
            var artwork = HubConsoleAssets.Library();
            regular = HubConsoleAssets.Font(false);
            bold = HubConsoleAssets.Font(true);
            var screen = HubCompactPass.Require("_Pit/TvScreen").GetComponent<RectTransform>();
            foreach (Transform child in screen.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            screen.rotation = Quaternion.identity;
            screen.position = new Vector3(0, .97f, 2.735f);
            screen.sizeDelta = new Vector2(1280, 720);
            screen.localScale = Vector3.one * (3.12f / 1280f);
            var root = Rect(screen, "ConsoleLibrary", 0, 0, 1280, 720);
            var view = root.gameObject.AddComponent<ConsoleLibraryView>();
            Paint(root, Ink);

            Label(root, "Brand", "SLEEPOVER", 48, 25, 220, 37, 28, Cream, true);
            Label(root, "Header", "ИГРЫ ДЛЯ КОМПАНИИ", 280, 35, 550, 23, 17, Muted);
            var page = Label(root, "Page", "01 / 09", 1070, 28, 162, 32, 23, Cream, true);
            page.alignment = TextAlignmentOptions.Right;
            Box(root, "HeaderLine", 48, 78, 1184, 1, Line);

            var hero = Rect(root, "FeaturedGame", 48, 102, 1184, 334);
            Paint(hero, Panel);
            var heroCover = Box(hero, "Cover", 0, 0, 594, 334, Color.white);
            var heroAccent = Box(hero, "Accent", 618, 27, 4, 24, Accent);
            var genre = Label(hero, "Genre", "ПРЯТКИ В ТЕМНОТЕ", 636, 28, 510, 24, 16, Accent, true);
            var title = Label(hero, "Title", "Плачущие ангелы", 630, 64, 525, 77, 39, Cream, true);
            title.enableAutoSizing = true; title.fontSizeMin = 32; title.fontSizeMax = 39;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            var description = Label(hero, "Description", "Подкрадись к ведущему и замри, когда на тебя попадёт свет.", 632, 150, 515, 91, 22, Cream);
            var players = Label(hero, "Players", "3–8 игроков  /  разные роли", 632, 251, 515, 24, 17, Muted);
            var launch = Box(hero, "Launch", 632, 287, 220, 32, Accent);
            var launchLabel = Label(launch.transform, "Label", "ENTER   ИГРАТЬ", 0, 0, 220, 32, 17, Ink, true);
            launchLabel.alignment = TextAlignmentOptions.Center;
            Label(hero, "Together", "ЕЩЁ ОДИН РАУНД?", 874, 293, 274, 23, 14, Muted);

            var position = Label(root, "LibraryHeading", "БИБЛИОТЕКА  /  09 ИГР", 48, 458, 900, 24, 17, Muted, true);
            var previous = Label(root, "PreviousPage", "←", 1155, 452, 30, 34, 27, Cream, true).gameObject;
            var next = Label(root, "NextPage", "→", 1200, 452, 32, 34, 27, Cream, true).gameObject;
            var viewport = Rect(root, "CardViewport", 48, 495, 1184, 158);
            viewport.gameObject.AddComponent<RectMask2D>();
            var strip = Rect(viewport, "CardStrip", 0, 0, 1184, 158);
            var card = CreateCard(strip);
            var track = Box(root, "ProgressTrack", 48, 665, 1184, 2, Line);
            var progress = Box(track.transform, "Progress", 0, 0, 0, 0, Accent);
            progress.rectTransform.anchorMin = Vector2.zero;
            progress.rectTransform.anchorMax = new Vector2(1f / 9f, 1);
            var hint = Label(root, "Controls", "Стрелки — выбор     Enter — играть     Esc — выключить", 48, 683, 1184, 24, 17, Muted);
            hint.alignment = TextAlignmentOptions.Center;

            var data = new SerializedObject(view);
            Set(data, "artwork", artwork); Set(data, "cardTemplate", card); Set(data, "cardStrip", strip);
            Set(data, "heroCover", heroCover); Set(data, "heroAccent", heroAccent);
            Set(data, "title", title); Set(data, "genre", genre); Set(data, "description", description);
            Set(data, "playerCount", players); Set(data, "page", page); Set(data, "positionLabel", position);
            Set(data, "launchLabel", launchLabel); Set(data, "progress", progress);
            Set(data, "previousPage", previous); Set(data, "nextPage", next);
            data.ApplyModifiedPropertiesWithoutUndo();
            var menu = new SerializedObject(HubCompactPass.Require("HubManager").GetComponent<ConsoleMenu>());
            Set(menu, "libraryView", view); Set(menu, "screenRoot", root.gameObject); Set(menu, "hintText", hint);
            Set(menu, "cardsParent", null); Set(menu, "captionText", null);
            menu.ApplyModifiedPropertiesWithoutUndo();
            HubConsoleShellBuilder.Wrap(screen, root, (ConsoleMenu)menu.targetObject);
        }

        private static ConsoleGameCard CreateCard(Transform parent)
        {
            var rect = Rect(parent, "GameCard_00", 0, 0, 224, 158);
            var border = Paint(rect, Line);
            var surface = Box(rect, "Surface", 3, 3, 218, 152, Ink);
            var cover = Box(rect, "Cover", 4, 4, 216, 121, Color.white);
            var title = Label(rect, "Title", "Плачущие ангелы", 12, 128, 200, 25, 17, Cream, true);
            title.enableAutoSizing = true; title.fontSizeMin = 14; title.fontSizeMax = 17;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            var mark = Box(rect, "SelectedMark", 188, 12, 24, 4, Cream);
            var card = rect.gameObject.AddComponent<ConsoleGameCard>();
            var data = new SerializedObject(card);
            Set(data, "cover", cover); Set(data, "border", border); Set(data, "surface", surface);
            Set(data, "title", title); Set(data, "selectedMark", mark.gameObject);
            data.ApplyModifiedPropertiesWithoutUndo();
            return card;
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static Image Paint(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
            return image;
        }

        private static Image Box(Transform parent, string name, float x, float y, float width, float height, Color color)
            => Paint(Rect(parent, name, x, y, width, height), color);

        private static TMP_Text Label(Transform parent, string name, string text, float x, float y, float width, float height, float size, Color color, bool heavy = false)
        {
            var label = Rect(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = heavy ? bold : regular;
            label.text = text; label.fontSize = size; label.color = color;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }

        private static void Set(SerializedObject data, string property, Object value) => data.FindProperty(property).objectReferenceValue = value;
        private static Color Hex(string value) => HubCozyMaterials.Hex(value);
    }
}
