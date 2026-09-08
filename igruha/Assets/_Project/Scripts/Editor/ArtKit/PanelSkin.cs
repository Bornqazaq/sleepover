using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Igruha.Core.UI;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Переодеть панели самих мини-игр: ввод вопроса «Экзамена», окна подсказок
    /// шатра, выбор позы «Дырки в стене» и прочее, что каждая игра строит себе
    /// сама.
    ///
    /// Общий интерфейс раунда собирает <see cref="HudSkin"/>, и он владеет
    /// своими объектами целиком. Здесь наоборот: раскладка чужая и не трогается
    /// вовсе — меняются только подложка, скругление, цвет и состояния нажатия.
    /// Причина простая: геометрию этих панелей выверяли на плейтестах, а
    /// выглядят они серыми прямоугольниками из редактора.
    ///
    /// Что считается чем:
    /// <list type="bullet">
    /// <item>панель — картинка с детьми размером от 300×160;</item>
    /// <item>кнопка, поле ввода и переключатель — по компоненту;</item>
    /// <item>всё остальное не трогается.</item>
    /// </list>
    ///
    /// Насыщенные цвета у некнопок не перекрашиваются: командная полоса и табло
    /// красятся цветом команды, и это смысл, а не оформление.
    /// </summary>
    public static class PanelSkin
    {
        /// <summary>Контейнеры общего интерфейса — их одевает HudSkin.</summary>
        private static readonly string[] Skipped = { "_HudPlates", "_HudOverlay", "EmoteWheel" };

        /// <summary>
        /// Компоненты, чьи ветки трогать нельзя: цвет там означает команду.
        ///
        /// Табло сюда не входит намеренно. Экранная сводка «Секундомера»
        /// собрана на том же <c>WorldScoreboardFace</c>, что и грань табло
        /// на арене, и по имени компонента отличить их нельзя — а подложку
        /// ей переодеть нужно. Строки табло красятся местом, но это картинки
        /// без детей: панелью они не считаются и остаются нетронутыми.
        /// </summary>
        private static readonly string[] MeaningfulColor = { "TeamProgressBar" };

        /// <summary>Слова в имени кнопки, по которым она считается главной.</summary>
        private static readonly string[] PrimaryButtons =
        {
            "done", "confirm", "ready", "start", "готов", "подтверд"
        };

        /// <summary>Меньше этого картинка с детьми — не панель, а иконка или полоска.</summary>
        private static readonly Vector2 MinPanelSize = new Vector2(300f, 160f);

        /// <summary>Насыщеннее этого цвет означает смысл, а не оформление.</summary>
        private const float MeaningfulSaturation = 0.4f;

        /// <summary>
        /// Темнее этого цвет считается подложкой, какой бы насыщенной
        /// её ни считала модель HSV.
        ///
        /// Без порога яркости подложка «Секундомера» RGBA(0.02, 0.02, 0.04)
        /// проходила как «осмысленный цвет»: в HSV у почти чёрного синего
        /// насыщенность 0.5, потому что синий канал вдвое больше остальных.
        /// Смысл цвет несёт только тогда, когда его видно.
        /// </summary>
        private const float MeaningfulValue = 0.35f;

        /// <summary>Делитель радиуса скругления: панели крупные, поля мельче.</summary>
        private const float PanelCorner = 1.6f;

        private const float FieldCorner = 2.4f;

        [MenuItem("Igruha/Интерфейс/Переодеть панели этой сцены")]
        public static void SkinCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            int touched = Apply(scene);
            if (touched > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            Debug.Log($"🎛 Панели сцены «{scene.name}» переодеты: {touched} элементов");
        }

        [MenuItem("Igruha/Интерфейс/Переодеть панели всех сцен")]
        public static void SkinAllScenes()
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project/Scenes" });
            int total = 0;

            foreach (string guid in guids)
            {
                Scene scene = EditorSceneManager.OpenScene(AssetDatabase.GUIDToAssetPath(guid), OpenSceneMode.Single);
                int touched = Apply(scene);
                if (touched == 0)
                {
                    continue;
                }

                total += touched;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log($"🎛 Панели игр переодеты во всех сценах: {total} элементов");
        }

        /// <summary>Переодеть панели сцены. Возвращает число тронутых элементов.</summary>
        public static int Apply(Scene scene)
        {
            int touched = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var canvases = root.GetComponentsInChildren<Canvas>(true);
                for (int i = 0; i < canvases.Length; i++)
                {
                    if (canvases[i].renderMode == RenderMode.WorldSpace)
                    {
                        continue;
                    }

                    foreach (Transform child in canvases[i].transform)
                    {
                        if (IsSkipped(child.name))
                        {
                            continue;
                        }

                        touched += Walk(child);
                    }
                }
            }

            return touched;
        }

        private static bool IsSkipped(string name)
        {
            for (int i = 0; i < Skipped.Length; i++)
            {
                if (name == Skipped[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static int Walk(Transform node)
        {
            if (HasMeaningfulColor(node))
            {
                return 0;
            }

            int touched = Dress(node);
            for (int i = 0; i < node.childCount; i++)
            {
                touched += Walk(node.GetChild(i));
            }

            return touched;
        }

        private static bool HasMeaningfulColor(Transform node)
        {
            var components = node.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    continue;
                }

                string name = components[i].GetType().Name;
                for (int j = 0; j < MeaningfulColor.Length; j++)
                {
                    if (name == MeaningfulColor[j])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int Dress(Transform node)
        {
            var field = node.GetComponent<TMP_InputField>();
            if (field != null)
            {
                DressField(field);
                return 1;
            }

            var toggle = node.GetComponent<Toggle>();
            if (toggle != null)
            {
                DressToggle(toggle);
                return 1;
            }

            var button = node.GetComponent<Button>();
            if (button != null)
            {
                DressButton(button);
                return 1;
            }

            var image = node.GetComponent<Image>();
            if (image != null && IsPanel(node, image))
            {
                Skin(image, UiSpriteBaker.Card, UiSkin.Card, PanelCorner);
                Edge(node, UiSpriteBaker.Stroke, PanelCorner);
                return 1;
            }

            return 0;
        }

        private static bool IsPanel(Transform node, Image image)
        {
            if (node.childCount == 0 || image.color.a < 0.2f)
            {
                return false;
            }

            Color.RGBToHSV(image.color, out float _, out float saturation, out float value);
            if (saturation > MeaningfulSaturation && value > MeaningfulValue)
            {
                return false;
            }

            Vector2 size = ((RectTransform)node).rect.size;
            return size.x >= MinPanelSize.x && size.y >= MinPanelSize.y;
        }

        private static void DressField(TMP_InputField field)
        {
            var image = field.GetComponent<Image>();
            if (image != null)
            {
                Skin(image, UiSpriteBaker.KeyCap, UiSkin.Field, FieldCorner);
                Edge(field.transform, UiSpriteBaker.KeyCapEdge, FieldCorner);
            }

            field.caretColor = UiSkin.Accent;
            field.selectionColor = new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.35f);
            Paint(field.textComponent, UiSkin.TextPrimary);
            Paint(field.placeholder as TMP_Text, UiSkin.TextMuted);
            Transition(field);
        }

        /// <summary>
        /// Переключатель: выбранное состояние — акцентная обводка, а не заливка.
        ///
        /// Заливка была бы заметнее, но подпись переключателя рисуется поверх
        /// галочки, и на жёлтом фоне белая подпись пропадает. Обводка говорит
        /// то же самое и не трогает цвет текста.
        /// </summary>
        private static void DressToggle(Toggle toggle)
        {
            if (toggle.targetGraphic is Image background)
            {
                Skin(background, UiSpriteBaker.Chip, UiSkin.Plate);
            }

            if (toggle.graphic is Image checkmark)
            {
                var rect = checkmark.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                Skin(checkmark, UiSpriteBaker.Stroke, UiSkin.Accent, 2f);
            }

            Transition(toggle);
            PaintChildText(toggle.transform, UiSkin.TextPrimary);
        }

        private static void DressButton(Button button)
        {
            bool primary = IsPrimary(button.name);
            if (button.targetGraphic is Image image)
            {
                Skin(image, UiSpriteBaker.Chip, primary ? UiSkin.Accent : UiSkin.Plate);
            }

            Transition(button);
            PaintChildText(button.transform, primary ? UiSkin.AccentInk : UiSkin.TextPrimary);
        }

        private static bool IsPrimary(string name)
        {
            string lower = name.ToLowerInvariant();
            for (int i = 0; i < PrimaryButtons.Length; i++)
            {
                if (lower.Contains(PrimaryButtons[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Состояния нажатия: наведение светлее, нажатие темнее, недоступное
        /// бледнее. Оттенки заданы множителем к цвету картинки, поэтому одна
        /// настройка годится и жёлтой кнопке, и серой.
        /// </summary>
        private static void Transition(Selectable selectable)
        {
            ColorBlock colors = selectable.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            colors.fadeDuration = 0.08f;
            selectable.colors = colors;
            EditorUtility.SetDirty(selectable);
        }

        private static void Skin(Image image, string sprite, Color color, float corner = 1f)
        {
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(sprite);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.pixelsPerUnitMultiplier = corner;
            EditorUtility.SetDirty(image);
        }

        /// <summary>Обводка поверх подложки: тонкая рамка того же скругления.</summary>
        private static void Edge(Transform parent, string sprite, float corner)
        {
            Transform existing = parent.Find("Edge");
            GameObject go = existing != null
                ? existing.gameObject
                : new GameObject("Edge", typeof(RectTransform), typeof(Image));

            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // Первым ребёнком: рамка рисуется под содержимым панели и не
            // накрывает собой поля ввода и кнопки.
            rect.SetAsFirstSibling();

            // Рамка не участвует в компоновке: у панели с группой компоновки
            // она иначе встала бы в общий столбец и сдвинула содержимое.
            var element = go.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = go.AddComponent<LayoutElement>();
            }

            element.ignoreLayout = true;

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            Skin(image, sprite, UiSkin.CardEdge, corner);
        }

        private static void PaintChildText(Transform node, Color color)
        {
            var texts = node.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                Paint(texts[i], color);
            }
        }

        private static void Paint(TMP_Text text, Color color)
        {
            if (text == null)
            {
                return;
            }

            text.color = color;
            EditorUtility.SetDirty(text);
        }
    }
}
