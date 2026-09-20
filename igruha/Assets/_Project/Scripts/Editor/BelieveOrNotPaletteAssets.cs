using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Stable local palette. Table tones retained; architecture uses the original club kit.</summary>
    internal static class BelieveOrNotPaletteAssets
    {
        /// <summary>Тон палитры. Один тон — один материал в проекте.</summary>
        internal enum Tone
        {
            /// <summary>Сукно столешницы — единственная светлая поверхность в кадре.</summary>
            Felt,

            /// <summary>Тёмное лакированное дерево: борт стола, царга, нога. Сохранённый тон стола.</summary>
            Wood,

            /// <summary>Подложка под собственным тканым ковром.</summary>
            Floor,

            /// <summary>Тёмно-синяя подложка за модульными панелями.</summary>
            Wall,

            /// <summary>Потолок: темнее стен, в кадре виден только у лампы.</summary>
            Ceiling,

            /// <summary>Матовый тёмный металл: шнур подвеса лампы.</summary>
            Shade,

            /// <summary>Бордовый тон подложки круглого ковра.</summary>
            Carpet
        }

        private const string MaterialsRoot = "Assets/_Project/Materials";
        private const string Folder = MaterialsRoot + "/BelieveOrNot";
        private const string Prefix = "BON_";
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        private static readonly Dictionary<Tone, Material> cache = new Dictionary<Tone, Material>(8);
        internal static string Source => "собственная палитра клуба; цвета стола сохранены";
        internal static void Measure() => cache.Clear();

        /// <summary>Цвет тона.</summary>
        internal static Color ColorOf(Tone tone)
        {
            switch (tone)
            {
                case Tone.Felt: return new Color32(0x1F, 0x50, 0x33, 0xFF);
                case Tone.Wood: return new Color(.21264955f, .18160641f, .16607869f, 1);
                case Tone.Floor: return new Color32(18, 18, 24, 255);
                case Tone.Wall: return new Color32(24, 34, 53, 255);
                case Tone.Ceiling: return new Color32(17, 22, 31, 255);
                case Tone.Shade: return new Color32(36, 33, 30, 255);
                case Tone.Carpet: return new Color32(30, 13, 22, 255);
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
        /// бликом от него. Сукно обязано быть матовым (0.05), иначе оно бликует
        /// тканью, которой не бывает; лакированное дерево борта — наоборот,
        /// полуглянцевым, иначе стол в кадре плоская заливка.
        /// </summary>
        private static void Apply(Material material, Tone tone)
        {
            Color color = ColorOf(tone);

            switch (tone)
            {
                case Tone.Felt:
                    Opaque(material, color, 0.05f, 0f);
                    break;
                // Лак борта пришлось убавить с 0.45 до 0.18. Лампа над столом
                // одна и висит прямо над бортом, поэтому широкий блик ложится
                // ровно на кольцо целиком: борт с альбедо 0.21 светился в кадре
                // как белый камень, и его яркость задавал блик, а не цвет.
                // Проверено рендером 04.09.
                case Tone.Wood:
                    Opaque(material, color, 0.18f, 0f);
                    break;
                // Пол матовый: при 0.20 холодная заливка зала ложилась на него
                // скользящим бликом во весь кадр, и тёмно-зелёный пол читался
                // светло-синим. Ковровый пол блика и не должен давать.
                case Tone.Floor:
                    Opaque(material, color, 0.03f, 0f);
                    break;
                case Tone.Wall:
                case Tone.Ceiling:
                    Opaque(material, color, 0.10f, 0f);
                    break;
                case Tone.Shade:
                    Opaque(material, color, 0.25f, 0.6f);
                    break;
                // Ковёр матовее пола: ворс блика не даёт вовсе, а любой блик
                // на нём стал бы вторым светлым пятном прямо под столом.
                case Tone.Carpet:
                    Opaque(material, color, 0.02f, 0f);
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
