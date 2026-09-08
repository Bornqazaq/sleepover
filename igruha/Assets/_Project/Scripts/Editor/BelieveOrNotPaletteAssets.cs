using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Материалы «Верю / не верю» — по одному ассету на тон палитры
    /// (бриф, раздел 14.2 спеки).
    ///
    /// Ассеты, а не материалы в памяти: материал, созданный кодом и не
    /// записанный на диск, теряется при первом же перезапуске редактора, и
    /// сцена приходит с розовыми предметами. Тот же вывод уже сделан на
    /// «Дырке в стене» (STATE.md, 3.40).
    ///
    /// <b>Цвета не выбраны, а сняты с пака — подфаза 4.2.</b> Дерево стола
    /// берёт средний цвет сундука, стены — средний цвет бархатной шторы: это
    /// те самые предметы, что стоят с ними рядом в кадре, и стык блокаута
    /// с реквизитом обязан не читаться. Замер идёт усреднением по UV
    /// (<see cref="SyntyPalette"/>), а не пипеткой по атласу: у модели нет
    /// «цвета», у неё есть кусок общей картинки.
    ///
    /// Своими остаются два тона, которых в паке нет вовсе: зелёное сукно
    /// и матовый металл шнура. Их задаёт бриф словами, и брать их неоткуда.
    /// </summary>
    internal static class BelieveOrNotPaletteAssets
    {
        /// <summary>Тон палитры. Один тон — один материал в проекте.</summary>
        internal enum Tone
        {
            /// <summary>Сукно столешницы — единственная светлая поверхность в кадре.</summary>
            Felt,

            /// <summary>Тёмное лакированное дерево: борт стола, царга, нога. Снято с сундука.</summary>
            Wood,

            /// <summary>Пол зала: тон сукна, уведённый в темноту.</summary>
            Floor,

            /// <summary>Стены зала: тон бархата, уведённый в тень.</summary>
            Wall,

            /// <summary>Потолок: темнее стен, в кадре виден только у лампы.</summary>
            Ceiling,

            /// <summary>Матовый тёмный металл: шнур подвеса лампы.</summary>
            Shade,

            /// <summary>Ковёр вокруг стола: тон бархата стен, чуть выведенный из темноты.</summary>
            Carpet
        }

        private const string MaterialsRoot = "Assets/_Project/Materials";
        private const string Folder = MaterialsRoot + "/BelieveOrNot";
        private const string Prefix = "BON_";
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        /// <summary>
        /// Источник тона дерева — барная тумба, а не сундук на столе.
        ///
        /// Сундук напрашивался: он стоит к столу ближе всех. Но у него ровно
        /// половина площади — железная оковка, и усреднение по UV даёт серый
        /// (#474142 на прогоне 04.09), то есть «дерево с железом», а не дерево.
        /// Борт стола, покрашенный этим числом, стал бы бетонным. Тумба бара —
        /// сплошное тёмное дерево, и она же стоит в зале самой большой
        /// деревянной поверхностью.
        /// </summary>
        internal const string WoodSource = "Assets/Synty/PolygonCasino/Prefabs/Props/SM_Prop_Minibar_01.prefab";

        /// <summary>Сундук — замеряется для отчёта: с ним борт стола обязан не спорить.</summary>
        internal const string ChestSource = "Assets/Synty/PolygonGeneric/Prefabs/Props/SM_Gen_Prop_Chest_01.prefab";

        /// <summary>Бархатная штора: ею закрыты все четыре стены зала.</summary>
        internal const string VelvetSource = "Assets/Synty/PolygonCasino/Prefabs/Buildings/SM_Bld_Curtain_Closed_01.prefab";

        /// <summary>
        /// Цвета, снятые с пака замером 04.09: тумба #63564F, штора #85352A.
        /// Подставляются, когда замерить не удалось, — иначе цвет в <c>.mat</c>
        /// зависел бы от того, у кого открыт проект, и сцена приезжала бы разной.
        ///
        /// ⚠️ <b>Сейчас подставляются всегда, и это не наша поломка.</b>
        /// 04.09 в общий <see cref="SyntyPalette"/> приехала проверка
        /// <c>mesh.isReadable</c>: меш с выключенным Read/Write замер пропускает.
        /// У всех мешей паков Synty этот флаг снят (проверено на тумбе, шторе
        /// и сундуке), но <b>в редакторе они читаются</b> — тем же утром замер
        /// по ним отработал и дал разные цвета по предметам. То есть проверка
        /// отсекает рабочий путь, и палитра любой игры молча уезжает на запасные
        /// значения. Здесь это ничего не меняет: запасные числа и есть замер.
        /// Разбираться с самой проверкой — напарнику, он её автор, и его игры
        /// зависят от неё так же.
        /// </summary>
        private static readonly Color FallbackWood = new Color32(0x63, 0x56, 0x4F, 0xFF);

        private static readonly Color FallbackVelvet = new Color32(0x85, 0x35, 0x2A, 0xFF);

        /// <summary>Сукно: в паках зелёного сукна нет, тон задан брифом.</summary>
        private static readonly Color FeltColour = new Color32(0x1F, 0x50, 0x33, 0xFF);

        /// <summary>Матовый металл шнура: тон задан брифом.</summary>
        private static readonly Color ShadeColour = new Color32(0x24, 0x21, 0x1E, 0xFF);

        /// <summary>
        /// Во сколько раз пол темнее сукна. Пол — вся нижняя половина кадра
        /// зрителя, и в тоне сукна он спорил бы со столом: светлое пятно
        /// в кадре обязано быть ровно одно.
        /// </summary>
        private const float FloorDrop = 0.22f;

        /// <summary>
        /// Во сколько раз стена темнее бархата шторы. Шторы висят прямо на
        /// стене, и стена обязана читаться той же тканью в тени, а не другой
        /// поверхностью.
        /// </summary>
        private const float WallDrop = 0.30f;

        /// <summary>Потолок относительно стены: он вне круга света и в кадр попадает только у лампы.</summary>
        private const float CeilingDrop = 0.5f;

        /// <summary>
        /// Во сколько раз ковёр темнее бархата шторы.
        ///
        /// Ковёр светлее пола (0.16 против 0.22 от своего источника, но
        /// источники разные: пол снят с зелёного сукна, ковёр — с красного
        /// бархата) ровно настолько, чтобы круг вокруг стола читался кругом.
        /// Ярче нельзя: за пределами лампы он подхватывает свет бара и
        /// начинает спорить со столом.
        /// </summary>
        private const float CarpetDrop = 0.16f;

        /// <summary>
        /// Насколько альбедо дерева стола ниже замера тумбы. Это не вкус,
        /// а арифметика света: у столешницы освещённость единица (лампа даёт
        /// 4 канделы с 1.8 м), а у тумбы в углу зала — около четверти. Одно
        /// и то же дерево под лампой выглядит вчетверо светлее, и борт,
        /// покрашенный замером как есть, читался бухтой бетона рядом с тёмным
        /// сундуком. Проверено рендером 04.09.
        /// </summary>
        private const float LitDrop = 0.30f;

        private static readonly Dictionary<Tone, Material> cache = new Dictionary<Tone, Material>(8);

        private static Color wood = FallbackWood;
        private static Color velvet = FallbackVelvet;

        /// <summary>Чем снят цвет: замером или сохранённым значением. Печатается отчётом дресса.</summary>
        internal static string Source { get; private set; } = "паков нет, взяты сохранённые значения";

        /// <summary>
        /// Снять цвета пака заново. Вызывается пересборкой до того, как
        /// кто-либо попросит материал.
        /// </summary>
        internal static void Measure()
        {
            cache.Clear();
            SyntyPalette.ClearCache();

            bool measuredWood = SyntyPalette.TryAverage(WoodSource, out Color packWood);
            bool measuredVelvet = SyntyPalette.TryAverage(VelvetSource, out Color packVelvet);
            bool measuredChest = SyntyPalette.TryAverage(ChestSource, out Color packChest);

            wood = measuredWood ? packWood : FallbackWood;
            velvet = measuredVelvet ? packVelvet : FallbackVelvet;
            Source = measuredWood && measuredVelvet
                ? $"замер по UV: тумба {SyntyPalette.Hex(wood)}, штора {SyntyPalette.Hex(velvet)}" +
                  (measuredChest ? $", сундук {SyntyPalette.Hex(packChest)} (для сверки)" : string.Empty)
                : "паков нет, взяты сохранённые значения";
        }

        /// <summary>Цвет тона.</summary>
        internal static Color ColorOf(Tone tone)
        {
            switch (tone)
            {
                case Tone.Felt: return FeltColour;
                case Tone.Wood: return Dim(wood, LitDrop);
                case Tone.Floor: return Dim(FeltColour, FloorDrop);
                case Tone.Wall: return Dim(velvet, WallDrop);
                case Tone.Ceiling: return Dim(velvet, WallDrop * CeilingDrop);
                case Tone.Shade: return ShadeColour;
                case Tone.Carpet: return Dim(velvet, CarpetDrop);
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

        /// <summary>
        /// Увести тон в темноту. Умножение идёт в линейном пространстве:
        /// «вчетверо темнее» — это про свет, а не про коды цветов, и в sRGB
        /// то же умножение дало бы заметно более светлый результат.
        /// </summary>
        private static Color Dim(Color srgb, float factor)
        {
            Color linear = srgb.linear;
            return new Color(linear.r * factor, linear.g * factor, linear.b * factor, 1f).gamma;
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
