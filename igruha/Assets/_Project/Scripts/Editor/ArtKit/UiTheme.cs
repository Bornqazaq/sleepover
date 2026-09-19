using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Клубный стиль интерфейса: тёмная кожа и эспрессо, латунные рамки,
    /// кремовый текст, заголовки с засечками.
    ///
    /// Первая игра на нём — «Верю / не верю» (IGR-565). Геймдизайнер решил
    /// привести к одному стилю меню и брифинг всех мини-игр, поэтому палитра,
    /// шрифты и приёмы вынесены сюда, а не зашиты в сборщик одной игры:
    /// следующая игра берёт их отсюда, а не копирует числа.
    ///
    /// Общий <see cref="HudSkin"/> пока рисует прежнюю холодную палитру
    /// <c>UiSkin</c> для всех игр. Эта тема ложится поверх него в сценах, которые
    /// уже переведены, и не трогает остальные.
    /// </summary>
    internal static class UiTheme
    {
        // ---------- Поверхности ----------

        /// <summary>Затемнение под модальной карточкой: тёплый почти-чёрный.</summary>
        internal static readonly Color Scrim = new Color(0.035f, 0.024f, 0.016f, 0.80f);

        /// <summary>
        /// Основная подложка карточек: эспрессо. Непрозрачная: проект в линейном
        /// цвете, и даже 4% прозрачности пропускают сквозь тёмную карточку
        /// светлого персонажа — на брифинге он просвечивал.
        /// </summary>
        internal static readonly Color Surface = new Color(0.086f, 0.063f, 0.047f, 1f);

        /// <summary>Приподнятое: кнопки, клавиши, выбранная строка — тёмная кожа.</summary>
        internal static readonly Color SurfaceRaised = new Color(0.165f, 0.118f, 0.086f, 1f);

        /// <summary>Утопленное: дорожки, неактивные строки.</summary>
        internal static readonly Color SurfaceSunken = new Color(0.047f, 0.035f, 0.027f, 0.82f);

        /// <summary>Плашка прямо поверх сцены, без карточки под ней.</summary>
        internal static readonly Color PlateOnScene = new Color(0.063f, 0.045f, 0.033f, 0.84f);

        // ---------- Латунь ----------

        internal static readonly Color Brass = new Color(0.80f, 0.63f, 0.36f, 1f);
        internal static readonly Color BrassBright = new Color(0.95f, 0.80f, 0.50f, 1f);

        /// <summary>Рамка карточки: латунь, приглушённая наполовину — рамка, а не подсветка.</summary>
        internal static readonly Color BrassEdge = new Color(0.80f, 0.63f, 0.36f, 0.55f);

        /// <summary>Волосяная линия: разделители, дорожки.</summary>
        internal static readonly Color BrassHairline = new Color(0.80f, 0.63f, 0.36f, 0.30f);

        /// <summary>Текст на латунной заливке.</summary>
        internal static readonly Color BrassInk = new Color(0.11f, 0.075f, 0.04f, 1f);

        // ---------- Текст ----------

        internal static readonly Color Cream = new Color(0.965f, 0.925f, 0.835f, 1f);
        internal static readonly Color Parchment = new Color(0.82f, 0.75f, 0.63f, 1f);
        internal static readonly Color Muted = new Color(0.60f, 0.53f, 0.44f, 1f);

        // ---------- Смысловые ----------

        /// <summary>Хороший исход: цвет сукна, высветленный до читаемости на тёмном.</summary>
        internal static readonly Color Good = new Color(0.56f, 0.84f, 0.60f, 1f);

        /// <summary>Плохой исход: бычья кровь, высветленная до читаемости на тёмном.</summary>
        internal static readonly Color Bad = new Color(0.95f, 0.56f, 0.49f, 1f);

        // ---------- Типографика ----------

        /// <summary>Разрядка подписей капителью, в единицах TMP (сотые кегля).</summary>
        internal const float CaptionSpacing = 10f;

        /// <summary>Разрядка заголовков с засечками: чуть свободнее, чем по умолчанию.</summary>
        internal const float TitleSpacing = 1.5f;

        // ---------- Спрайты ----------

        internal const string ChipStroke = UiSpriteBaker.Folder + "/UI_ChipStroke.png";
        internal const string Ornament = UiSpriteBaker.Folder + "/UI_Ornament.png";

        /// <summary>Девятислайс обводки капсулы — как у самой капсулы UI_Chip.</summary>
        private const int ChipBorder = 64;

        // ---------- Приёмы ----------

        internal static void Text(TMP_Text text, TMP_FontAsset font, float size, Color color,
            float spacing = 0f, FontStyles style = FontStyles.Normal)
        {
            if (text == null)
            {
                return;
            }

            if (font != null)
            {
                text.font = font;
                text.fontSharedMaterial = font.material;
            }

            text.fontSize = size;
            text.color = color;
            text.characterSpacing = spacing;
            text.fontStyle = style;
        }

        /// <summary>Подпись капителью: жирный гротеск, заглавные, разрядка, латунь.</summary>
        internal static void Caption(TMP_Text text, float size, Color? color = null)
        {
            Text(text, UiFonts.SansBold, size, color ?? Brass, CaptionSpacing, FontStyles.UpperCase);
        }

        /// <summary>Заголовок с засечками.</summary>
        internal static void Title(TMP_Text text, float size, Color? color = null)
        {
            Text(text, UiFonts.SerifBold, size, color ?? Cream, TitleSpacing);
        }

        /// <summary>
        /// Перекрасить картинку. Множитель скругления по умолчанию не трогается:
        /// его выставил общий скин, и рамка обязана совпасть с подложкой углами.
        /// </summary>
        internal static Image Paint(Image image, string spritePath, Color color,
            Image.Type type = Image.Type.Sliced, float? pixelsPerUnitMultiplier = null)
        {
            if (image == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(spritePath))
            {
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            }

            image.type = image.sprite == null ? Image.Type.Simple : type;
            if (pixelsPerUnitMultiplier.HasValue)
            {
                image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier.Value;
            }

            image.color = color;
            return image;
        }

        /// <summary>
        /// Найти или завести дочернюю картинку, растянутую на родителя и не
        /// участвующую в раскладке. Для рамок поверх подложки.
        /// </summary>
        internal static Image Overlay(Transform parent, string name, string spritePath, Color color,
            Image.Type type = Image.Type.Sliced, float? pixelsPerUnitMultiplier = null)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null
                ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));

            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            // Без ??: в редакторе отсутствующий компонент приходит «поддельным
            // null», которого оператор ?? не видит.
            var layout = go.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = go.AddComponent<LayoutElement>();
            }

            layout.ignoreLayout = true;

            Image image = Paint(go.GetComponent<Image>(), spritePath, color, type, pixelsPerUnitMultiplier);
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// Материал того же шрифта с мягкой тенью — для текста, который висит
        /// прямо над сценой без плашки: без тени он тонет на светлом пятне стола.
        /// </summary>
        internal static Material ShadowMaterial(TMP_FontAsset font)
        {
            string path = UiFonts.Folder + "/" + font.name + " Shadow.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(font.material);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                // Атлас мог пересобраться — тень обязана смотреть в тот же.
                material.CopyPropertiesFromMaterial(font.material);
            }

            material.name = font.name + " Shadow";
            material.EnableKeyword("UNDERLAY_ON");
            material.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.65f));
            material.SetFloat("_UnderlayOffsetX", 0f);
            material.SetFloat("_UnderlayOffsetY", -0.5f);
            material.SetFloat("_UnderlayDilate", 0.1f);
            material.SetFloat("_UnderlaySoftness", 0.55f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Состояния кнопки: подсветка поверх тёмной кожи, как в PanelSkin.
        /// У латунной заливки подсветка мягче: та же прибавка уводит латунь
        /// в кислотно-жёлтый.
        /// </summary>
        internal static void ButtonStates(Selectable selectable, bool brassFill = false)
        {
            if (selectable == null)
            {
                return;
            }

            selectable.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = selectable.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = brassFill ? new Color(1.10f, 1.08f, 1.04f, 1f) : new Color(1.35f, 1.30f, 1.20f, 1f);
            colors.pressedColor = new Color(0.82f, 0.80f, 0.78f, 1f);
            colors.selectedColor = brassFill ? new Color(1.04f, 1.03f, 1.02f, 1f) : new Color(1.20f, 1.16f, 1.10f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            selectable.colors = colors;
        }

        /// <summary>Импорт своих спрайтов интерфейса: девятислайс, без мипов, как у UiSpriteBaker.</summary>
        internal static void EnsureSpriteImports()
        {
            EnsureSprite(ChipStroke, new Vector4(ChipBorder, ChipBorder, ChipBorder, ChipBorder));
            EnsureSprite(Ornament, Vector4.zero);
        }

        internal static void EnsureSprite(string path, Vector4 border, bool mipmaps = false)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }

            if (importer == null)
            {
                throw new System.InvalidOperationException("Нет спрайта: " + path);
            }

            bool changed = importer.textureType != TextureImporterType.Sprite ||
                           importer.spriteBorder != border ||
                           importer.mipmapEnabled != mipmaps ||
                           importer.alphaIsTransparency != true;
            if (!changed)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.mipmapEnabled = mipmaps;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
