using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Шрифты интерфейса: засечки для заголовков и кнопок, гротеск для текста
    /// и отдельный набор для цифр.
    ///
    /// <b>Откуда шрифты.</b> Playfair Display и Manrope с Google Fonts, лицензия
    /// SIL OFL 1.1 — её тексты лежат рядом с файлами. OFL разрешает встраивать
    /// шрифт в коммерческую игру; нельзя только продавать сам шрифт отдельно.
    /// До этого прохода в проекте не было ни одного своего шрифта: даже
    /// «кириллические» ассеты других игр собраны из того же LiberationSans.
    ///
    /// <b>Статический атлас.</b> Набор символов известен заранее — латиница,
    /// кириллица и типографика. Атлас печётся один раз и ложится в ассет,
    /// как у <c>EH_Cyrillic</c> и шрифтов хаба: в сборке не нужен исходный TTF
    /// и не бывает «пропал символ, которого нет в атласе» (STATE 3.12).
    ///
    /// <b>Цифры одной ширины.</b> У обеих гарнитур цифры пропорциональные:
    /// единица уже восьмёрки на треть. В тексте это красиво, а таймер на
    /// каждой смене секунды дёргался бы вбок. Поэтому у набора цифр ширина
    /// всех десяти знаков выравнивается по самому широкому, а знак встаёт
    /// в середину своей клетки. Пары кернинга с цифрами из этого набора
    /// убираются по той же причине.
    /// </summary>
    public static class UiFonts
    {
        internal const string Folder = UiSpriteBaker.Folder + "/Fonts";

        internal const string SerifBoldPath = Folder + "/UI_Serif_Bold SDF.asset";
        internal const string SansMediumPath = Folder + "/UI_Sans_Medium SDF.asset";
        internal const string SansBoldPath = Folder + "/UI_Sans_Bold SDF.asset";
        internal const string NumbersPath = Folder + "/UI_Numbers SDF.asset";

        private const string FallbackPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        /// <summary>Сторона атласа текстовых наборов — как у шрифтов хаба и «Экзамена».</summary>
        private const int TextAtlasSize = 1024;

        /// <summary>Атлас цифр: в нём полтора десятка знаков.</summary>
        private const int NumbersAtlasSize = 512;

        /// <summary>Стартовый кегль выборки. Если набор не влез в атлас, кегль снижается шагом ниже.</summary>
        private const int StartSamplingSize = 90;
        private const int MinSamplingSize = 48;
        private const int SamplingStep = 6;

        /// <summary>Отступ вокруг знака в атласе, в долях кегля выборки: ширина «поля» SDF.</summary>
        private const float PaddingShare = 0.1f;

        private const string Typography = "«»—–·…←→№°×‘’“”„•";
        private const string NumbersSet = "0123456789:.,-–+%/ ";

        public static TMP_FontAsset SerifBold => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SerifBoldPath);
        public static TMP_FontAsset SansMedium => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SansMediumPath);
        public static TMP_FontAsset SansBold => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SansBoldPath);
        public static TMP_FontAsset Numbers => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NumbersPath);

        private struct Spec
        {
            public string Source;
            public string Target;
            public string Characters;
            public int AtlasSize;
            public bool TabularDigits;
        }

        [MenuItem("Igruha/Интерфейс/Собрать шрифты")]
        public static void BuildAll()
        {
            EnsureAll(rebuild: true);
        }

        /// <summary>
        /// Собрать недостающие ассеты. Существующие не трогаются: на них
        /// ссылаются сцены, а пересоздание сменило бы GUID. Пересборку с нуля
        /// делает пункт меню — после него ссылки восстанавливают сборщики сцен.
        /// </summary>
        internal static void EnsureAll(bool rebuild = false)
        {
            string text = TextCharacters();
            var specs = new[]
            {
                new Spec { Source = Folder + "/PlayfairDisplay-Bold.ttf", Target = SerifBoldPath, Characters = text, AtlasSize = TextAtlasSize },
                new Spec { Source = Folder + "/Manrope-Medium.ttf", Target = SansMediumPath, Characters = text, AtlasSize = TextAtlasSize },
                new Spec { Source = Folder + "/Manrope-Bold.ttf", Target = SansBoldPath, Characters = text, AtlasSize = TextAtlasSize },
                new Spec { Source = Folder + "/Manrope-ExtraBold.ttf", Target = NumbersPath, Characters = NumbersSet, AtlasSize = NumbersAtlasSize, TabularDigits = true },
            };

            foreach (Spec spec in specs)
            {
                if (!rebuild && AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(spec.Target) != null)
                {
                    continue;
                }

                Build(spec);
            }

            AssetDatabase.SaveAssets();
        }

        private static void Build(Spec spec)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(spec.Source);
            if (font == null)
            {
                throw new System.InvalidOperationException("Нет файла шрифта: " + spec.Source);
            }

            if (AssetDatabase.LoadAssetAtPath<Object>(spec.Target) != null)
            {
                AssetDatabase.DeleteAsset(spec.Target);
            }

            TMP_FontAsset asset = null;
            for (int size = StartSamplingSize; size >= MinSamplingSize; size -= SamplingStep)
            {
                asset = TMP_FontAsset.CreateFontAsset(font, size, Mathf.RoundToInt(size * PaddingShare),
                    GlyphRenderMode.SDFAA, spec.AtlasSize, spec.AtlasSize, AtlasPopulationMode.Dynamic, false);
                if (asset.TryAddCharacters(spec.Characters, out string missing) || OnlyAbsentFromFont(font, missing))
                {
                    break;
                }

                Object.DestroyImmediate(asset);
                asset = null;
            }

            if (asset == null)
            {
                throw new System.InvalidOperationException("Набор не влез в атлас даже на минимальном кегле: " + spec.Target);
            }

            string name = System.IO.Path.GetFileNameWithoutExtension(spec.Target);
            asset.name = name;
            asset.atlasTexture.name = name + " Atlas";
            asset.material.name = name + " Material";

            if (spec.TabularDigits)
            {
                MakeDigitsTabular(asset);
            }

            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackPath);
            if (fallback != null)
            {
                asset.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            }

            asset.atlasPopulationMode = AtlasPopulationMode.Static;

            AssetDatabase.CreateAsset(asset, spec.Target);
            AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
        }

        /// <summary>Недостающие символы — только те, которых нет в самом шрифте, а не те, что не влезли.</summary>
        private static bool OnlyAbsentFromFont(Font font, string missing)
        {
            if (string.IsNullOrEmpty(missing))
            {
                return true;
            }

            foreach (char c in missing)
            {
                if (font.HasCharacter(c))
                {
                    return false;
                }
            }

            return true;
        }

        private static void MakeDigitsTabular(TMP_FontAsset asset)
        {
            var digitGlyphs = new List<Glyph>();
            var digitIndices = new HashSet<uint>();
            float widest = 0f;

            foreach (TMP_Character character in asset.characterTable)
            {
                if (character.unicode < '0' || character.unicode > '9' || character.glyph == null)
                {
                    continue;
                }

                digitGlyphs.Add(character.glyph);
                digitIndices.Add(character.glyph.index);
                widest = Mathf.Max(widest, character.glyph.metrics.horizontalAdvance);
            }

            foreach (Glyph glyph in digitGlyphs)
            {
                GlyphMetrics metrics = glyph.metrics;
                float extra = widest - metrics.horizontalAdvance;
                metrics.horizontalBearingX += extra * 0.5f;
                metrics.horizontalAdvance = widest;
                glyph.metrics = metrics;
            }

            asset.fontFeatureTable.glyphPairAdjustmentRecords.RemoveAll(record =>
                digitIndices.Contains(record.firstAdjustmentRecord.glyphIndex) ||
                digitIndices.Contains(record.secondAdjustmentRecord.glyphIndex));

            asset.ReadFontAssetDefinition();
        }

        private static string TextCharacters()
        {
            var builder = new StringBuilder();
            for (char c = ' '; c <= '~'; c++)
            {
                builder.Append(c);
            }

            for (char c = 'А'; c <= 'я'; c++)
            {
                builder.Append(c);
            }

            builder.Append("Ёё");
            builder.Append(Typography);
            return builder.ToString();
        }
    }
}
