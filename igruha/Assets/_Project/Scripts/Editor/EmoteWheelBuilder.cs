using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Igruha.Core.CameraSystems;
using Igruha.Core.Hub;
using Igruha.Core.Player;
using Igruha.Core.UI;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Собирает круглое меню эмоций в UI активной сцены и связывает его
    /// с HubBootstrap и ригом камеры. Пересобираемо: старый объект сносится,
    /// поэтому раскладку можно править константами здесь, а не мышью.
    /// </summary>
    internal static class EmoteWheelBuilder
    {
        private const string RootObjectName = "EmoteWheel";
        private const string CanvasPath = "_UI/Canvas";

        private const float WheelRadius = 190f;
        private const float SlotWidth = 150f;
        private const float SlotHeight = 58f;
        private const float PointerSize = 26f;

        private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color SlotColor = new Color(0.09f, 0.09f, 0.12f, 0.85f);
        private static readonly Color PointerColor = new Color(0.98f, 0.75f, 0.15f, 1f);
        private static readonly Color HintColor = new Color(1f, 1f, 1f, 0.55f);

        [MenuItem("Igruha/UI/Build Emote Wheel")]
        private static void Build()
        {
            GameObject canvas = GameObject.Find(CanvasPath);
            if (canvas == null)
            {
                Debug.LogError($"EmoteWheelBuilder: в активной сцене нет {CanvasPath} — открой Hub и повтори.");
                return;
            }

            Transform existing = canvas.transform.Find(RootObjectName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            TMP_FontAsset font = FindSceneFont();
            GameObject root = CreateUIObject(RootObjectName, canvas.transform);
            EmoteWheel wheel = root.AddComponent<EmoteWheel>();

            GameObject panel = CreateUIObject("Panel", root.transform);
            Stretch(panel.GetComponent<RectTransform>());
            Image backdrop = panel.AddComponent<Image>();
            backdrop.color = BackdropColor;
            // Колесо — оверлей поверх игры, а не модалка: клики оно не ловит,
            // персонаж под ним продолжает ходить.
            backdrop.raycastTarget = false;

            var slots = new EmoteWheelSlot[PlayerEmoteAbility.SectorCount];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = CreateSlot(panel.transform, i, font);
            }

            RectTransform pointer = CreatePointer(panel.transform);
            CreateHint(panel.transform, font);

            var serialized = new SerializedObject(wheel);
            serialized.FindProperty("panel").objectReferenceValue = panel;
            serialized.FindProperty("pointer").objectReferenceValue = pointer;
            serialized.FindProperty("pointerRadius").floatValue = WheelRadius;
            serialized.FindProperty("cameraRig").objectReferenceValue = Object.FindAnyObjectByType<ThirdPersonCameraRig>();

            SerializedProperty slotsProperty = serialized.FindProperty("slots");
            slotsProperty.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                slotsProperty.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            panel.SetActive(false);
            BindToBootstrap(wheel);

            EditorSceneManager.MarkSceneDirty(canvas.scene);
            EditorSceneManager.SaveScene(canvas.scene);
            Debug.Log($"EmoteWheelBuilder: колесо собрано в сцене {canvas.scene.name} ({slots.Length} секторов).");
        }

        /// <summary>Сектор i стоит по кругу, начиная сверху и дальше по часовой — так же, как их нумерует PlayerEmoteAbility.</summary>
        private static EmoteWheelSlot CreateSlot(Transform parent, int index, TMP_FontAsset font)
        {
            GameObject slotObject = CreateUIObject($"Slot_{index + 1}", parent);
            RectTransform rect = slotObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(SlotWidth, SlotHeight);

            float angle = index * (360f / PlayerEmoteAbility.SectorCount) * Mathf.Deg2Rad;
            rect.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * WheelRadius;

            Image background = slotObject.AddComponent<Image>();
            background.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            background.type = Image.Type.Sliced;
            background.color = SlotColor;
            background.raycastTarget = false;

            GameObject labelObject = CreateUIObject("Label", slotObject.transform);
            Stretch(labelObject.GetComponent<RectTransform>());
            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = $"Танец {index + 1}";
            label.fontSize = 22f;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            EmoteWheelSlot slot = slotObject.AddComponent<EmoteWheelSlot>();
            var serialized = new SerializedObject(slot);
            serialized.FindProperty("background").objectReferenceValue = background;
            serialized.FindProperty("label").objectReferenceValue = label;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return slot;
        }

        private static RectTransform CreatePointer(Transform parent)
        {
            GameObject pointerObject = CreateUIObject("Pointer", parent);
            RectTransform rect = pointerObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(PointerSize, PointerSize);

            Image image = pointerObject.AddComponent<Image>();
            image.sprite = BuiltinSprite("UI/Skin/Knob.psd");
            image.color = PointerColor;
            image.raycastTarget = false;
            return rect;
        }

        private static void CreateHint(Transform parent, TMP_FontAsset font)
        {
            GameObject hintObject = CreateUIObject("Hint", parent);
            RectTransform rect = hintObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(320f, 40f);
            rect.anchoredPosition = Vector2.zero;

            TextMeshProUGUI hint = hintObject.AddComponent<TextMeshProUGUI>();
            hint.font = font;
            hint.text = "Отпусти Tab";
            hint.fontSize = 18f;
            hint.color = HintColor;
            hint.alignment = TextAlignmentOptions.Center;
            hint.raycastTarget = false;
        }

        private static void BindToBootstrap(EmoteWheel wheel)
        {
            HubBootstrap bootstrap = Object.FindAnyObjectByType<HubBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogWarning("EmoteWheelBuilder: в сцене нет HubBootstrap — колесо не к чему привязать, свяжи вручную.");
                return;
            }

            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("emoteWheel").objectReferenceValue = wheel;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Шрифт берём тот же, что уже в сцене, — чтобы колесо не выбивалось из остального UI.</summary>
        private static TMP_FontAsset FindSceneFont()
        {
            TextMeshProUGUI existing = Object.FindAnyObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
            return existing != null ? existing.font : TMP_Settings.defaultFontAsset;
        }

        private static Sprite BuiltinSprite(string path) =>
            AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
    }
}
