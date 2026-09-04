using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Материалы «Верю / не верю» — по одному ассету на тон палитры
    /// (бриф, раздел 14.2 спеки).
    ///
    /// Тонов здесь ровно столько, сколько нужно собранным из примитивов
    /// предметам: сукно, дерево и матовый металл шнура. Реквизит пака
    /// (сундук, стул, абажур) приезжает в собственных материалах и в них
    /// и остаётся — бордо обивки и латунь защёлок уже те, что просит бриф.
    /// Остальные тона заводятся тогда, когда появится, чему их назначить.
    ///
    /// Ассеты, а не материалы в памяти: материал, созданный кодом и не
    /// записанный на диск, теряется при первом же перезапуске редактора, и
    /// сцена приходит с розовыми предметами. Тот же вывод уже сделан на
    /// «Дырке в стене» (STATE.md, 3.40).
    ///
    /// <b>Цвета здесь — рабочие, снятые с концепта геймдизайнера.</b> На
    /// подфазе 4.2 они пересчитываются усреднением по UV мешей пака, чтобы
    /// блокаут сел в тон реквизиту и стык не читался; имена тонов и пути
    /// ассетов при этом не меняются, поэтому пересчёт не тронет ни дресс,
    /// ни сцену.
    /// </summary>
    internal static class BelieveOrNotPaletteAssets
    {
        /// <summary>Тон палитры. Один тон — один материал в проекте.</summary>
        internal enum Tone
        {
            /// <summary>Сукно столешницы.</summary>
            Felt,

            /// <summary>Тёмное лакированное дерево: борт стола, царга, нога.</summary>
            Wood,

            /// <summary>Матовый тёмный металл: шнур подвеса лампы.</summary>
            Shade
        }

        private const string MaterialsRoot = "Assets/_Project/Materials";
        private const string Folder = MaterialsRoot + "/BelieveOrNot";
        private const string Prefix = "BON_";
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        private static readonly Dictionary<Tone, Material> cache = new Dictionary<Tone, Material>(8);

        /// <summary>Цвет тона. Значения — с концепта; 4.2 пересчитает их замером.</summary>
        internal static Color ColorOf(Tone tone)
        {
            switch (tone)
            {
                case Tone.Felt: return new Color32(0x1F, 0x50, 0x33, 0xFF);
                case Tone.Wood: return new Color32(0x3A, 0x22, 0x18, 0xFF);
                case Tone.Shade: return new Color32(0x24, 0x21, 0x1E, 0xFF);
                default: return Color.magenta;
            }
        }

        /// <summary>
        /// Материал тона. Ассет заводится при первом обращении и дальше живёт
        /// в проекте; свойства переписываются из палитры каждую пересборку —
        /// иначе правка цвета в коде не доезжала бы до готовой сцены.
        /// </summary>
        internal static Material Get(Tone tone)
        {
            if (cache.TryGetValue(tone, out Material cached) && cached != null)
            {
                return cached;
            }

            string path = $"{Folder}/{Prefix}{tone}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find(LitShaderName);
                if (shader == null)
                {
                    Debug.LogError($"Шейдер '{LitShaderName}' не найден — материалы «Верю / не верю» не собрать");
                    return null;
                }

                EnsureFolder();
                material = new Material(shader) { name = Prefix + tone };
                AssetDatabase.CreateAsset(material, path);
            }

            Apply(material, tone);
            EditorUtility.SetDirty(material);
            cache[tone] = material;
            return material;
        }

        /// <summary>Сбросить кэш перед пересборкой: ассеты могли переехать или быть удалены.</summary>
        internal static void ClearCache()
        {
            cache.Clear();
        }

        /// <summary>Дописать заведённые материалы на диск. Вызывать в конце пересборки.</summary>
        internal static void Flush()
        {
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Настроить материал под тон.
        ///
        /// Гладкость задаётся вручную у каждого тона, а не берётся по умолчанию:
        /// в зале ровно один источник света, и весь объём предметов читается
        /// бликом от него. Сукно обязано быть матовым (0.05), иначе оно
        /// бликует тканью, которой не бывает; лакированное дерево борта, —
        /// наоборот, полуглянцевым, иначе стол в кадре плоская заливка.
        /// </summary>
        private static void Apply(Material material, Tone tone)
        {
            Color color = ColorOf(tone);

            switch (tone)
            {
                case Tone.Felt:
                    Opaque(material, color, 0.05f, 0f);
                    break;
                case Tone.Wood:
                    Opaque(material, color, 0.45f, 0f);
                    break;
                case Tone.Shade:
                    Opaque(material, color, 0.25f, 0.6f);
                    break;
            }
        }

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Color1 = Shader.PropertyToID("_Color");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");

        /// <summary>
        /// Непрозрачный тон. Пишутся оба свойства цвета: URP Lit читает
        /// <c>_BaseColor</c>, а инспектор и часть инструментов — <c>_Color</c>,
        /// и материал с расхождением между ними показывает в редакторе не то,
        /// что попадает в кадр.
        /// </summary>
        private static void Opaque(Material material, Color color, float smoothness, float metallic)
        {
            material.SetColor(BaseColor, color);
            material.SetColor(Color1, color);
            material.SetFloat(Smoothness, smoothness);
            material.SetFloat(Metallic, metallic);
            material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder(MaterialsRoot, "BelieveOrNot");
            }
        }
    }
}
