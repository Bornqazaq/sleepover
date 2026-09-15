using System;
using System.Linq;
using Igruha.Core.Player;
using Igruha.Core.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Rebuilds only the existing character modal, without replacing its controller or the roster.</summary>
    public static class CharacterSelectBuilder
    {
        private static readonly Color Ink = Hex("101E23"), Panel = Hex("1B3035"), Line = Hex("354C4E");
        private static readonly Color Cream = Hex("F4ECD9"), Muted = Hex("A4B8B5"), Gold = Hex("F4BE81");
        private static TMP_FontAsset regular, bold;
        private static readonly string[] Colours = { "9EB8C6", "D9B475", "C99886", "E5C766", "D89178", "D9BFE5", "78C4AD", "7EB6E8" };
        private static readonly string[] Taglines = { "СПОКОЙСТВИЕ. ПОЧТИ ВСЕГДА.", "В КОМПАНИИ ВСЕГДА ЕСТЬ БОСС", "НА СВОЕЙ ВОЛНЕ", "ХОРОШЕГО ЧЕЛОВЕКА МНОГО", "ЗНАЕТ, КАК СОБРАТЬ КОМПАНИЮ", "ДОБАВИТ ХАРАКТЕРА ЭТОЙ НОЧИ", "ПОЙМАЙ СВОЙ РИТМ", "ЕЩЁ ОДИН РАУНД?" };

        [MenuItem("Igruha/Хаб/Выбор персонажа — обновить экран")]
        public static void Rebuild()
        {
            if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().name != "Hub")
                throw new InvalidOperationException("Open Hub outside Play Mode.");
            regular = HubConsoleAssets.Font(false); bold = HubConsoleAssets.Font(true);
            var screen = Object.FindFirstObjectByType<CharacterSelectScreen>();
            var data = new SerializedObject(screen);
            var panel = (GameObject)data.FindProperty("panel").objectReferenceValue;
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            // Resolve assets before touching the accepted scene, so missing portraits cannot leave a partial screen.
            var faces = roster.Characters.Select(c => CharacterPortraitCapture.Load(c.DisplayName, "Face")).ToArray();
            var bodies = roster.Characters.Select(c => CharacterPortraitCapture.Load(c.DisplayName, "Body")).ToArray();
            foreach (Transform child in panel.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            var background = panel.GetComponent<Image>();
            background.sprite = null; background.color = new Color(Ink.r, Ink.g, Ink.b, .985f); background.raycastTarget = true;
            var root = Rect(panel.transform, "CharacterSelectionBoard", 0, 0, 1280, 720);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.anchoredPosition = Vector2.zero;
            root.gameObject.AddComponent<CharacterSelectionLayout>();
            var view = root.gameObject.AddComponent<CharacterSelectionView>();
            Label(root, "Brand", "SLEEPOVER", 48, 29, 250, 29, 24, Cream, true);
            Label(root, "Step", "01  /  ЗНАКОМСТВО", 303, 36, 380, 24, 15, Muted);
            Label(root, "Heading", "Кем будешь сегодня?", 46, 73, 940, 55, 43, Cream, true);
            Label(root, "Subtitle", "Выбирай своего. Вечер только начинается.", 49, 132, 850, 27, 21, Muted);
            var timerCaption = Label(root, "TimerCaption", "ДО АВТОВЫБОРА", 930, 68, 196, 24, 15, Gold, true);
            timerCaption.alignment = TextAlignmentOptions.Right;
            Label(root, "TimerHint", "СЛУЧАЙНЫЙ ГЕРОЙ", 925, 96, 201, 21, 12, Muted).alignment = TextAlignmentOptions.Right;
            Ring(root, "TimerTrack", 1148, 43, 84, 5, Line);
            var ring = Ring(root, "Countdown", 1148, 43, 84, 5, Gold);
            var seconds = Label(root, "Seconds", "30", 1148, 58, 84, 40, 33, Cream, true);
            seconds.alignment = TextAlignmentOptions.Center;
            Label(root, "SecondsUnit", "СЕК", 1148, 99, 84, 17, 10, Muted).alignment = TextAlignmentOptions.Center;
            Box(root, "HeaderLine", 48, 175, 1184, 1, Line);

            var poster = Rect(root, "HeroPoster", 48, 198, 430, 415);
            var backdrop = Paint(poster, Hex("293E46"));
            var halo = Ring(poster, "Halo", 53, 32, 324, 2, new Color(1, 1, 1, .10f));
            Ring(poster, "HaloInner", 87, 66, 256, 1, new Color(1, 1, 1, .055f));
            var number = Label(poster, "Number", "01 / 08", 20, 17, 100, 21, 14, Cream, true);
            Label(poster, "HeroLabel", "ТВОЙ ПЕРСОНАЖ", 227, 20, 181, 20, 12, Muted).alignment = TextAlignmentOptions.Right;
            var hero = Box(poster, "Model", 68, 8, 294, 350, Color.white); hero.preserveAspect = true;
            Box(poster, "NamePlate", 0, 335, 430, 80, new Color(Ink.r, Ink.g, Ink.b, .94f));
            var heroName = Label(poster, "Name", "Karlan", 20, 340, 365, 44, 36, Cream, true);
            var tagline = Label(poster, "Tagline", Taglines[0], 22, 388, 395, 19, 12, Gold, true);
            var heroAccent = Box(poster, "Accent", 0, 0, 4, 415, Gold);
            var available = Label(root, "Availability", "КОМПАНИЯ / СВОБОДНО 08", 506, 193, 726, 24, 14, Muted, true);
            var buttons = new CharacterSlotButton[roster.Characters.Count];
            // This roster has eight characters; rows adapt if a slot is added without hardcoding player count.
            int columns = 4;
            float height = Mathf.Min(190, 386f / Mathf.Max(1, Mathf.CeilToInt(buttons.Length / (float)columns)) - 6);
            for (int i = 0; i < buttons.Length; i++)
                buttons[i] = Card(root, i, 506 + (i % columns) * 184, 227 + (i / columns) * (height + 12), height);
            var playSurface = Box(root, "PlayButton", 48, 633, 430, 43, Gold);
            playSurface.raycastTarget = true;
            var play = playSurface.gameObject.AddComponent<Button>();
            var playLabel = Label(playSurface.transform, "Label", "ИГРАТЬ ЗА KARLAN", 10, 0, 410, 43, 18, Ink, true);
            playLabel.alignment = TextAlignmentOptions.Center;
            var status = Label(root, "Status", "Нажми на героя — и присоединяйся к компании.", 506, 639, 726, 36, 17, Cream);
            Box(root, "ProgressTrack", 48, 694, 1184, 3, Line);
            var track = Rect(root, "ProgressBounds", 48, 694, 1184, 3);
            var progress = Box(track, "Progress", 0, 0, 0, 0, Gold);
            progress.rectTransform.anchorMin = Vector2.zero; progress.rectTransform.anchorMax = Vector2.one;

            var art = new SerializedObject(view);
            Set(art, "hero", hero); Set(art, "heroBackdrop", backdrop); Set(art, "heroAccent", heroAccent);
            Set(art, "heroName", heroName); Set(art, "heroTagline", tagline); Set(art, "heroNumber", number);
            Set(art, "availableLabel", available); Set(art, "secondsLabel", seconds); Set(art, "timerCaption", timerCaption);
            Set(art, "statusLabel", status); Set(art, "ring", ring); Set(art, "progress", progress);
            Set(art, "playButton", play); Set(art, "playLabel", playLabel);
            var entries = art.FindProperty("portraits"); entries.arraySize = buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Name").stringValue = roster.Characters[i].DisplayName;
                entry.FindPropertyRelative("Tagline").stringValue = i < Taglines.Length ? Taglines[i] : "ВЕЧЕР ТОЛЬКО НАЧИНАЕТСЯ";
                entry.FindPropertyRelative("Face").objectReferenceValue = faces[i];
                entry.FindPropertyRelative("Body").objectReferenceValue = bodies[i];
                entry.FindPropertyRelative("Accent").colorValue = Hex(Colours[i % Colours.Length]);
            }
            art.ApplyModifiedPropertiesWithoutUndo();
            data.Update(); Set(data, "presentation", view); Set(data, "countdownLabel", null);
            var slots = data.FindProperty("slots"); slots.arraySize = buttons.Length;
            for (int i = 0; i < buttons.Length; i++) slots.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            panel.SetActive(false);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("Character selection rebuilt with roster portraits and countdown.");
        }

        private static CharacterSlotButton Card(Transform parent, int index, float x, float y, float height)
        {
            var rect = Rect(parent, "Character_" + index.ToString("00"), x, y, 174, height);
            var border = Paint(rect, Line); border.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.transition = Selectable.Transition.None;
            Box(rect, "Surface", 3, 3, 168, height - 6, Panel);
            var face = Box(rect, "Portrait", 8, 3, 158, height - 50, Color.white); face.preserveAspect = true;
            Box(rect, "Caption", 3, height - 50, 168, 47, Ink);
            var label = Label(rect, "Name", "Karlan", 13, height - 49, 148, 29, 22, Cream, true);
            var status = Label(rect, "State", "ВЫБРАТЬ", 14, height - 19, 147, 15, 10, Muted, true);
            var mark = Box(rect, "Focus", 144, 13, 15, 3, Gold);
            var slot = rect.gameObject.AddComponent<CharacterSlotButton>();
            var data = new SerializedObject(slot);
            Set(data, "label", label); Set(data, "statusLabel", status); Set(data, "portrait", face); Set(data, "border", border); Set(data, "selectionMark", mark);
            data.ApplyModifiedPropertiesWithoutUndo();
            return slot;
        }

        private static CountdownRing Ring(Transform parent, string name, float x, float y, float size, float thickness, Color color)
        {
            var ring = Rect(parent, name, x, y, size, size).gameObject.AddComponent<CountdownRing>(); ring.color = color; ring.raycastTarget = false;
            var data = new SerializedObject(ring); data.FindProperty("thickness").floatValue = thickness; data.ApplyModifiedPropertiesWithoutUndo(); return ring;
        }
        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
        }
        private static Image Paint(RectTransform rect, Color color) { var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }
        private static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color) => Paint(Rect(parent, name, x, y, w, h), color);
        private static TMP_Text Label(Transform parent, string name, string text, float x, float y, float w, float h, float size, Color color, bool heavy = false)
        {
            var label = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>(); label.font = heavy ? bold : regular;
            label.text = text; label.fontSize = size; label.color = color; label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.Normal; label.raycastTarget = false; return label;
        }
        private static void Set(SerializedObject data, string field, Object value) => data.FindProperty(field).objectReferenceValue = value;
        private static Color Hex(string value) => HubCozyMaterials.Hex(value);
    }
}
