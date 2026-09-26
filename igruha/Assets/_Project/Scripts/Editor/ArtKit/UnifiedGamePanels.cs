using Igruha.Core.UI;
using Igruha.Minigames.CryingAngels;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Igruha.EditorTools
{
    /// <summary>Экранные панели игры: шрифт и нейтральные поверхности. Цвета команд и арена сохраняются.</summary>
    public static class UnifiedGamePanels
    {
        public static void Apply(Scene scene)
        {
            PanelSkin.Apply(scene);
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                {
                    if (canvas.renderMode == RenderMode.WorldSpace || canvas.GetComponentInParent<PauseScreen>() != null ||
                        canvas.GetComponentInParent<RoundResultsView>() != null) continue;
                    foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (Skip(text.transform)) continue;
                        // Прицел — символ точного размера, его метрика относится к наведению.
                        if (text.name == "Reticle") continue;
                        bool bold = (text.fontStyle & FontStyles.Bold) != 0 || (text.font != null && text.font.name.Contains("Bold"));
                        var font = bold ? UiFonts.SansBold : UiFonts.SansMedium;
                        text.font = font; text.fontSharedMaterial = font.material;
                        text.fontStyle &= ~FontStyles.Bold;
                        Color.RGBToHSV(text.color, out _, out float saturation, out float value);
                        if (saturation < .22f && value > .5f)
                            text.color = value > .85f ? MinigameUiStyle.OnDark : MinigameUiStyle.MutedOnDark;
                        EditorUtility.SetDirty(text);
                    }
                    foreach (var image in canvas.GetComponentsInChildren<Image>(true))
                    {
                        if (Skip(image.transform) || image.type == Image.Type.Filled) continue;
                        if (image.color.a < .2f || image.rectTransform.rect.width < 200 || image.rectTransform.rect.height < 38) continue;
                        Color.RGBToHSV(image.color, out _, out _, out float value);
                        if (value > .40f) continue;
                        image.color = MinigameUiStyle.HudSurface;
                        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiSpriteBaker.Card);
                        image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 3;
                        EditorUtility.SetDirty(image);
                    }
                }
                foreach (var feedback in root.GetComponentsInChildren<BeamCaughtFeedback>(true))
                {
                    var data = new SerializedObject(feedback); data.FindProperty("font").objectReferenceValue = UiFonts.SansBold;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach (var phrases in root.GetComponentsInChildren<QuickPhrasePanel>(true))
                {
                    var data = new SerializedObject(phrases); data.FindProperty("rowFont").objectReferenceValue = UiFonts.SansMedium;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static bool Skip(Transform transform)
        {
            for (var node = transform; node != null; node = node.parent)
                if (node.name == "EmoteWheel" || node.name == "_HudPlates" || node.name == "_HudOverlay" ||
                    node.GetComponent<RoundResultsView>() != null || node.GetComponent<PauseScreen>() != null) return true;
            return false;
        }
    }
}
