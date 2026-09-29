using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Igruha.Core.CameraSystems;
using Igruha.Core.UI;
using Igruha.Minigames.CarryItem;

namespace Igruha.EditorTools
{
    public static class CarryItemRespawnBuilder
    {
        public static void Ensure(CarryItemMinigame game)
        {
            var hud = Object.FindFirstObjectByType<RoundHud>(FindObjectsInactive.Include);
            var cameras = Object.FindFirstObjectByType<MinigameCameraController>(FindObjectsInactive.Include);
            if (game == null || hud == null || cameras == null)
                throw new System.InvalidOperationException("Carry respawn view requires game, HUD and cameras");
            var canvas = hud.GetComponentInParent<Canvas>();
            var spectator = Object.FindFirstObjectByType<SpectatorCamera>(FindObjectsInactive.Include);
            if (spectator == null) spectator = cameras.gameObject.AddComponent<SpectatorCamera>();
            Set(spectator, "cameraController", cameras);
            Set(spectator, "hud", hud);
            Set(spectator, "cameraRig", Object.FindFirstObjectByType<ThirdPersonCameraRig>(FindObjectsInactive.Include));

            var view = game.GetComponent<CarryItemRespawnPresentation>();
            if (view == null) view = game.gameObject.AddComponent<CarryItemRespawnPresentation>();
            var anchor = game.transform.Find("RespawnArenaView");
            if (anchor == null)
            {
                anchor = new GameObject("RespawnArenaView").transform;
                anchor.SetParent(game.transform, false);
            }
            anchor.position = new Vector3(0, 2f, 0);
            anchor.rotation = Quaternion.Euler(0, 90, 0);

            var previous = canvas.transform.Find("CarryRespawnPanel");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var panel = new GameObject("CarryRespawnPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(Canvas));
            var rect = (RectTransform)panel.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(640, 180);
            rect.anchoredPosition = new Vector2(0, -70);
            panel.GetComponent<Canvas>().overrideSorting = true;
            panel.GetComponent<Canvas>().sortingOrder = 600; // Above practice (500), below pause (2000).
            panel.GetComponent<Image>().color = UiSkin.PlateOnScene;
            panel.GetComponent<Image>().raycastTarget = false;
            panel.GetComponent<CanvasGroup>().blocksRaycasts = false;
            var texts = hud.GetComponentsInChildren<TMP_Text>(true);
            TMP_FontAsset font = null;
            for (int i = 0; i < texts.Length; i++) if (texts[i].font != null) { font = texts[i].font; break; }
            Label(rect, "Title", "ВОЗВРАЩЕНИЕ ЧЕРЕЗ", 27, 55, font, UiSkin.TextSecondary);
            var seconds = Label(rect, "Seconds", "5 С", 60, 0, font, UiSkin.Accent);
            Label(rect, "Hint", "ПОКА НАБЛЮДАЕШЬ ЗА ИГРОЙ", 22, -59, font, UiSkin.TextSecondary);
            panel.SetActive(false);
            Set(view, "cameras", cameras);
            Set(view, "spectator", spectator);
            Set(view, "arenaView", anchor);
            Set(view, "panel", panel);
            Set(view, "secondsText", seconds);
            Set(game, "respawnPresentation", view);

            var config = new SerializedObject(game.Config);
            config.FindProperty("respawnDelaySeconds").floatValue = 5f;
            config.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TMP_Text Label(Transform parent, string name, string text, int size, float y, TMP_FontAsset font, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(620, 76);
            rect.anchoredPosition = new Vector2(0, y);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.enableAutoSizing = true;
            label.fontSizeMin = 22;
            label.fontSizeMax = size;
            label.alignment = TextAlignmentOptions.Center;
            label.color = color;
            label.text = text;
            label.raycastTarget = false;
            return label;
        }

        private static void Set(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
