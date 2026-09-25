using System;
using Igruha.Core.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Styles existing HUD references without rebuilding game panels or the emote wheel.
    /// Used both by arena builders and the migration of existing scenes.
    /// </summary>
    public static class UnifiedHudStyle
    {
        private const string ScenesFolder = "Assets/_Project/Scenes/Minigames";
        private const string TemplatePath = "Assets/_Project/Scenes/MinigameTemplate.unity";
        private const string DefinitionsFolder = "Assets/_Project/Settings/Gameplay/Minigames";
        private const float TimerWidth = 228f, TimerHeight = 88f, TimerTop = 24f;
        private const float TimerSize = 44f, CaptionSize = 16f, BodySize = 26f;
        private const float CornerScale = 3f;

        [MenuItem("Igruha/Интерфейс/Единый HUD во всех мини-играх")]
        public static void ApplyAll()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before styling scenes.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the open scene before styling all minigames.");

            UnifiedResultsBuilder.BuildPrefab();
            UnifiedPauseMenuBuilder.BuildPrefab();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { ScenesFolder }))
                    ApplyAndSave(AssetDatabase.GUIDToAssetPath(guid));
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TemplatePath) != null) ApplyAndSave(TemplatePath);
                foreach (string guid in AssetDatabase.FindAssets("t:MinigameDefinition", new[] { DefinitionsFolder }))
                {
                    var definition = AssetDatabase.LoadAssetAtPath<Igruha.Core.Minigame.MinigameDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    var data = new SerializedObject(definition);
                    data.FindProperty("tutorialFont").objectReferenceValue = Require(UiFonts.SansMedium);
                    if (data.ApplyModifiedPropertiesWithoutUndo()) AssetDatabase.SaveAssetIfDirty(definition);
                }
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        private static void ApplyAndSave(string path)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Apply(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void Apply(Scene scene)
        {
            UnifiedPauseMenuBuilder.Install(scene);
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (RoundHud hud in root.GetComponentsInChildren<RoundHud>(true)) Apply(hud);
            UnifiedGamePanels.Apply(scene);
        }

        private static void Apply(RoundHud hud)
        {
            var data = new SerializedObject(hud);
            var timer = data.FindProperty("timerText").objectReferenceValue as TMP_Text;
            var status = data.FindProperty("statusText").objectReferenceValue as TMP_Text;
            var spectator = data.FindProperty("spectatorText").objectReferenceValue as TMP_Text;
            var countdown = data.FindProperty("countdownText").objectReferenceValue as TMP_Text;
            Text(timer, Require(UiFonts.Numbers), TimerSize, MinigameUiStyle.OnDark);
            Text(status, Require(UiFonts.SansMedium), BodySize, MinigameUiStyle.OnDark);
            Text(spectator, Require(UiFonts.SansMedium), BodySize, MinigameUiStyle.MutedOnDark);
            if (countdown != null) Text(countdown, Require(UiFonts.Numbers), countdown.fontSize, MinigameUiStyle.OnDark);

            var timerPlate = Plate(timer, "TimerPlate");
            var statusPlate = Plate(status, "StatusPlate");
            Plate(spectator, "SpectatorPlate");
            if (timerPlate != null)
            {
                data.FindProperty("timerPlate").objectReferenceValue = timerPlate.gameObject;
                var rect = timerPlate.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -TimerTop);
                rect.sizeDelta = new Vector2(TimerWidth, TimerHeight);
                Center(timer.rectTransform, new Vector2(0f, -10f), new Vector2(TimerWidth - 24f, 54f));
                timer.alignment = TextAlignmentOptions.Center;
                TMP_Text caption = Caption(rect);
                Text(caption, Require(UiFonts.SansBold), CaptionSize, MinigameUiStyle.MutedOnDark);
                caption.text = "ВРЕМЯ РАУНДА";
                caption.alignment = TextAlignmentOptions.Center;
                caption.characterSpacing = 2f;
                Center(caption.rectTransform, new Vector2(0f, 25f), new Vector2(TimerWidth - 24f, 20f));
            }
            if (statusPlate != null)
            {
                data.FindProperty("statusPlate").objectReferenceValue = statusPlate.gameObject;
                var rect = statusPlate.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1f);
                rect.anchoredPosition = new Vector2(0, -128f);
                rect.sizeDelta = new Vector2(1100f, 104f);
                status.rectTransform.anchorMin = Vector2.zero; status.rectTransform.anchorMax = Vector2.one;
                status.rectTransform.offsetMin = new Vector2(24, 10); status.rectTransform.offsetMax = new Vector2(-24, -10);
                status.textWrappingMode = TextWrappingModes.Normal;
                status.enableAutoSizing = true; status.fontSizeMin = 20; status.fontSizeMax = BodySize;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            UnifiedResultsBuilder.Apply(hud);
        }

        private static TMP_FontAsset Require(TMP_FontAsset font)
        {
            if (font == null) throw new InvalidOperationException("Shared UI font is missing; run Igruha/Интерфейс/Собрать шрифты.");
            return font;
        }

        private static void Text(TMP_Text text, TMP_FontAsset font, float size, Color color)
        {
            if (text == null) return;
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.fontStyle = FontStyles.Normal;
            text.fontSize = size;
            text.color = color;
            text.characterSpacing = 0f;
            text.raycastTarget = false;
            EditorUtility.SetDirty(text);
        }

        private static Image Plate(TMP_Text text, string name)
        {
            if (text == null || text.transform.parent == null || text.transform.parent.name != name) return null;
            var image = text.transform.parent.GetComponent<Image>();
            if (image == null) return null;
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiSpriteBaker.Card);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = CornerScale;
            image.color = MinigameUiStyle.HudSurface;
            image.raycastTarget = false;
            // A previously applied scene theme can leave a decorative border.
            foreach (Image child in image.GetComponentsInChildren<Image>(true))
                if (child != image && (child.name == "ClubEdge" || child.name == "Edge"))
                {
                    child.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiSpriteBaker.Stroke);
                    child.type = Image.Type.Sliced;
                    child.pixelsPerUnitMultiplier = CornerScale;
                    child.color = MinigameUiStyle.HudEdge;
                }
            EditorUtility.SetDirty(image);
            return image;
        }

        private static TMP_Text Caption(Transform parent)
        {
            var existing = parent.Find("TimerCaption");
            if (existing != null) return existing.GetComponent<TMP_Text>();
            var go = new GameObject("TimerCaption", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            return go.GetComponent<TMP_Text>();
        }

        private static void Center(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
