using Igruha.Minigames.OneBullet;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    public static class OneBulletWeaponHudBuilder
    {
        public static void Configure(OneBulletPresentation view, OneBulletMinigame game, Transform parent)
        {
            foreach (string name in new[] { "WeaponStatus", "WeaponCountdown" })
            {
                var old = parent.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            var root = new GameObject("WeaponCountdown", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, 0); rect.anchoredPosition = new Vector2(0, 28); rect.sizeDelta = new Vector2(528, 106);
            var group = root.GetComponent<CanvasGroup>(); group.alpha = 0; group.interactable = group.blocksRaycasts = false;
            var hud = root.AddComponent<OneBulletWeaponHud>();
            var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Panel(rect, "Edge", Vector2.zero, new Vector2(528, 106), new Color(.77f, .58f, .31f, .65f), sprite);
            Panel(rect, "Card", Vector2.zero, new Vector2(524, 102), new Color(.055f, .069f, .080f, .95f), sprite);
            var accent = Panel(rect, "CounterPlate", new Vector2(-219, 2), new Vector2(64, 68), new Color(1, .76f, .36f), sprite);
            var number = Label(rect, "Seconds", new Vector2(-219, 9), new Vector2(64, 42), 34, new Color(.13f, .12f, .10f), TextAlignmentOptions.Center);
            var unit = Label(rect, "Unit", new Vector2(-219, -18), new Vector2(64, 18), 12, new Color(.19f, .16f, .11f), TextAlignmentOptions.Center);
            var title = Label(rect, "Title", new Vector2(38, 17), new Vector2(416, 30), 22, new Color(1, .88f, .64f), TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold; title.characterSpacing = 2;
            var detail = Label(rect, "Hint", new Vector2(38, -9), new Vector2(416, 24), 16, new Color(.78f, .80f, .79f), TextAlignmentOptions.Left);
            Panel(rect, "Track", new Vector2(38, -29), new Vector2(412, 3), new Color(1, 1, 1, .10f), null);
            var fill = Panel(rect, "Progress", new Vector2(38, -29), new Vector2(412, 3), new Color(1, .76f, .36f), null);
            // A sprite is required for Image.fillAmount to affect its geometry.
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
            OneBulletArenaBuilder.Set(hud, "game", game); OneBulletArenaBuilder.Set(hud, "visibility", group);
            OneBulletArenaBuilder.Set(hud, "title", title); OneBulletArenaBuilder.Set(hud, "detail", detail);
            OneBulletArenaBuilder.Set(hud, "counter", number); OneBulletArenaBuilder.Set(hud, "unit", unit);
            OneBulletArenaBuilder.Set(hud, "progress", fill); OneBulletArenaBuilder.Set(hud, "accent", accent);
        }
        public static void ConfigureAmmo(Transform parent)
        {
            var old = parent.Find("AmmoPlate");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var panel = Panel(parent, "AmmoPlate", Vector2.zero, new Vector2(320, 86), new Color(.055f, .069f, .080f, .92f), AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"));
            panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(1, 0);
            panel.rectTransform.pivot = new Vector2(1, 0);
            panel.rectTransform.anchoredPosition = new Vector2(-28, 28);
            panel.transform.SetAsFirstSibling();
        }
        private static Image Panel(Transform parent, string name, Vector2 position, Vector2 size, Color color, Sprite sprite)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            image.transform.SetParent(parent, false); image.rectTransform.anchoredPosition = position; image.rectTransform.sizeDelta = size;
            image.color = color; image.sprite = sprite; image.type = Image.Type.Sliced; image.raycastTarget = false; return image;
        }
        private static TMP_Text Label(Transform parent, string name, Vector2 position, Vector2 size, float fontSize, Color color, TextAlignmentOptions alignment)
        {
            var label = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false); label.rectTransform.anchoredPosition = position; label.rectTransform.sizeDelta = size;
            label.font = UiFonts.SansMedium; label.fontSize = fontSize; label.color = color; label.alignment = alignment; label.raycastTarget = false; return label;
        }
    }
}
