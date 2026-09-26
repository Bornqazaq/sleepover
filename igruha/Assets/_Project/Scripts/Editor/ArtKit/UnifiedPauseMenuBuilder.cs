using Igruha.Core.Audio;
using Igruha.Core.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Igruha.EditorTools.UnifiedResultsBuilder;

namespace Igruha.EditorTools
{
    /// <summary>Один префаб меню для всех сцен, включая Boot и Hub.</summary>
    public static class UnifiedPauseMenuBuilder
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/UnifiedPauseMenu.prefab";
        private const string RootName = "UnifiedPauseMenu";
        private const float CardWidth = 740f, InnerWidth = 644f;

        [MenuItem("Igruha/Интерфейс/Единое меню Esc во всех сценах")]
        public static void ApplyAll()
        {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop Play Mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("Save open scenes first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            BuildPrefab();
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project/Scenes" }))
                {
                    var scene = EditorSceneManager.OpenScene(AssetDatabase.GUIDToAssetPath(guid), OpenSceneMode.Single);
                    Install(scene); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        public static void Install(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { BuildPrefab(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
            GameObject installed = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(root) != prefab) continue;
                if (installed == null && root.GetComponent<PauseScreen>() != null) installed = root;
                else Object.DestroyImmediate(root);
            }
            if (installed != null) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var pause in root.GetComponentsInChildren<PauseScreen>(true))
                {
                    var old = new SerializedObject(pause).FindProperty("panel").objectReferenceValue as GameObject;
                    if (old != null && old != pause.gameObject) Object.DestroyImmediate(old);
                    Object.DestroyImmediate(pause);
                }
            PrefabUtility.InstantiatePrefab(prefab, scene);
        }

        public static void BuildPrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "UI");
            var root = new GameObject(RootName, typeof(RectTransform));
            try
            {
                var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2000;
                var scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
                root.AddComponent<GraphicRaycaster>();
                var pause = root.AddComponent<PauseScreen>();
                var panel = Image("PausePanel", root.transform, new Color(.015f, .045f, .05f, .72f));
                Stretch(panel.rectTransform); panel.raycastTarget = true;
                var view = panel.gameObject.AddComponent<PauseMenuView>();
                var card = Image("PauseCard", panel.transform, MinigameUiStyle.Paper, true);
                var rect = card.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(CardWidth, 100);
                var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(48, 48, 36, 32); layout.spacing = 14;
                layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
                card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var brand = Label("Brand", card.transform, "КОМНАТА  /  МЕНЮ", 18, MinigameUiStyle.Ink, 30, true); brand.characterSpacing = 3;
                var line = Image("Rule", card.transform, MinigameUiStyle.Ink); Height(line.gameObject, 4);
                var status = Label("Status", card.transform, "ИГРА НА ПАУЗЕ", 17, MinigameUiStyle.Ink, 28, true);
                var title = Label("Title", card.transform, "Небольшой перерыв", 42, MinigameUiStyle.Ink, 60, true);
                title.enableAutoSizing = true; title.fontSizeMin = 34; title.fontSizeMax = 42;
                var message = Label("Message", card.transform, "", 23, MinigameUiStyle.Ink, 68);
                message.textWrappingMode = TextWrappingModes.Normal;
                var settings = new GameObject("SoundSettings", typeof(RectTransform)); settings.transform.SetParent(card.transform, false);
                Height(settings, 168);
                TMP_Text musicValue, voiceValue;
                var music = Volume(settings.transform, "Музыка", 44, out musicValue);
                var voice = Volume(settings.transform, "Голоса игроков", -42, out voiceValue);
                TMP_Text primaryText, secondaryText, quitText;
                var primary = ActionButton(card.transform, "Resume", "Продолжить", true, out primaryText);
                var secondary = ActionButton(card.transform, "LeaveRound", "Покинуть раунд", false, out secondaryText);
                var quit = ActionButton(card.transform, "QuitGame", "Выйти из игры", false, out quitText);
                var footer = Label("Controls", card.transform, "ESC — вернуться в игру  ·  ↑ ↓ — выбор", 18, MinigameUiStyle.Ink, 32);
                footer.alignment = TextAlignmentOptions.Center;
                var v = new SerializedObject(view);
                Assign(v, "title", title); Assign(v, "message", message); Assign(v, "status", status); Assign(v, "footer", footer);
                Assign(v, "primaryLabel", primaryText); Assign(v, "secondaryLabel", secondaryText); Assign(v, "primary", primary);
                Assign(v, "quit", quit); Assign(v, "settings", settings); Assign(v, "music", music); Assign(v, "voice", voice);
                Assign(v, "musicValue", musicValue); Assign(v, "voiceValue", voiceValue); v.ApplyModifiedPropertiesWithoutUndo();
                var p = new SerializedObject(pause);
                Assign(p, "panel", panel.gameObject); Assign(p, "resumeButton", primary); Assign(p, "exitButton", secondary);
                Assign(p, "quitButton", quit); Assign(p, "presentation", view); p.ApplyModifiedPropertiesWithoutUndo();
                panel.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Button ActionButton(Transform parent, string name, string text, bool primary, out TMP_Text label)
        {
            var surface = Image(name, parent, primary ? MinigameUiStyle.Accent : new Color(.87f, .90f, .82f), true);
            Height(surface.gameObject, 66); surface.raycastTarget = true;
            var button = surface.gameObject.AddComponent<Button>(); button.targetGraphic = surface;
            var colors = button.colors;
            colors.highlightedColor = new Color(1, .81f, .43f); colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(.76f, .65f, .40f); button.colors = colors;
            label = Label("Label", surface.transform, text, 25, MinigameUiStyle.Ink, 0, true);
            Stretch(label.rectTransform); label.alignment = TextAlignmentOptions.Center;
            button.gameObject.AddComponent<UiButtonSound>();
            return button;
        }

        private static Slider Volume(Transform parent, string name, float y, out TMP_Text value)
        {
            var label = Label(name, parent, name, 23, MinigameUiStyle.Ink);
            Position(label.rectTransform, 0, y + 18, 460, 34);
            value = Label(name + "Value", parent, "", 22, MinigameUiStyle.Ink);
            Position(value.rectTransform, InnerWidth - 110, y + 18, 110, 34); value.alignment = TextAlignmentOptions.Right;
            var area = Image(name + "Slider", parent, Color.clear);
            Position(area.rectTransform, 0, y - 20, InnerWidth, 32); area.raycastTarget = true;
            var track = Image("Track", area.transform, new Color(.74f, .80f, .72f), true);
            Stretch(track.rectTransform); track.rectTransform.offsetMin = new Vector2(0, 11); track.rectTransform.offsetMax = new Vector2(0, -11);
            var fillArea = new GameObject("FillArea", typeof(RectTransform)); fillArea.transform.SetParent(area.transform, false);
            Stretch((RectTransform)fillArea.transform);
            ((RectTransform)fillArea.transform).offsetMin = new Vector2(12, 11); ((RectTransform)fillArea.transform).offsetMax = new Vector2(-12, -11);
            var fill = Image("Fill", fillArea.transform, MinigameUiStyle.Ink, true);
            Stretch(fill.rectTransform);
            var handleArea = new GameObject("HandleArea", typeof(RectTransform)); handleArea.transform.SetParent(area.transform, false);
            Stretch((RectTransform)handleArea.transform);
            ((RectTransform)handleArea.transform).offsetMin = new Vector2(12, 0); ((RectTransform)handleArea.transform).offsetMax = new Vector2(-12, 0);
            var handle = Image("Handle", handleArea.transform, MinigameUiStyle.Ink, true);
            handle.rectTransform.sizeDelta = new Vector2(24, 28);
            var slider = area.gameObject.AddComponent<Slider>(); slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle; slider.minValue = 0; slider.maxValue = 1;
            var colors = slider.colors; colors.highlightedColor = colors.selectedColor = MinigameUiStyle.Accent; slider.colors = colors;
            return slider;
        }
    }
}
