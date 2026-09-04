using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.HoleInWall;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Палитра «Рейса на память» — подфаза 4.2.
    ///
    /// Цвета снимаются <b>усреднением по UV мешей</b> конкретных предметов пака
    /// (<see cref="SyntyPalette"/>), а не подбираются на глаз: атласы Synty
    /// тайлить нельзя, и единственный способ посадить крашеный блокаут в тон
    /// стоящему рядом реквизиту — взять цвет с самого реквизита.
    ///
    /// <b>Главное число этой подфазы — не цвет, а контраст.</b> Плита обязана
    /// быть заметно светлее пропасти: игра устроена так, что семеро смотрят на
    /// идущего с сорока метров, и всё, что съедает разницу между опорой и
    /// пустотой, отменяет саму механику наблюдения. Отношение яркостей
    /// считается и печатается пересборкой, а падение ниже
    /// <see cref="MinPlateToPitContrast"/> — это предупреждение в консоли,
    /// а не вопрос вкуса. Ровно на этом отвалился вариант A на подфазе 4.0:
    /// тёмный цех выглядел лучше и не читался.
    ///
    /// <b>Материалы лежат ассетами, а не в сцене.</b> До 4.2 блокаут красился
    /// материалами, созданными на лету: они уезжали в YAML сцены, множились при
    /// каждой пересборке и не поддавались настройке. Теперь тон правится в
    /// одном файле на диске.
    /// </summary>
    internal static class MemoryRunPalette
    {
        /// <summary>Что красим. Один тон — один материал на диске.</summary>
        internal enum Tone
        {
            /// <summary>Сталь плиты. Тридцать плит делят ровно один экземпляр.</summary>
            Plate,

            /// <summary>Сталь несущих балок и рельсов под рядами — тоном темнее плиты.</summary>
            Structure,

            /// <summary>Настил площадки ожидания и выходной площадки.</summary>
            Deck,

            /// <summary>Стены и торцы цеха.</summary>
            Wall,

            /// <summary>Панель по низу стены: цоколь, без которого стена читается пустым листом.</summary>
            Wainscot,

            /// <summary>Перекрытие цеха. Темнее стен: верх кадра не должен спорить с маршрутом.</summary>
            Ceiling,

            /// <summary>Стены провала. Светлее дна, темнее цеха — по ним и считывается глубина.</summary>
            PitWall,

            /// <summary>Грязь и пыль на настилах. Темнее бетона, иначе это не грязь, а песок.</summary>
            Grime,

            /// <summary>Дно пропасти. Самое тёмное в кадре.</summary>
            Pit,

            /// <summary>Кромка провала: предупреждающий красный, только в цеху.</summary>
            PitRim,

            /// <summary>Выходная дверь.</summary>
            Door,

            /// <summary>Зелёная лампа над дверью — единственная зелёная точка кадра.</summary>
            ExitLamp,

            /// <summary>Стекло барьера очереди. Почти бесцветное: сквозь него смотрят весь раунд.</summary>
            Gate
        }

        private const string Folder = "Assets/_Project/Materials/MemoryRun";
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        private static readonly Dictionary<Tone, string> Files = new Dictionary<Tone, string>
        {
            { Tone.Plate, "MR_PlateSteel" },
            { Tone.Structure, "MR_Structure" },
            { Tone.Deck, "MR_Deck" },
            { Tone.Wall, "MR_Wall" },
            { Tone.Wainscot, "MR_Wainscot" },
            { Tone.Ceiling, "MR_Ceiling" },
            { Tone.PitWall, "MR_PitWall" },
            { Tone.Grime, "MR_Grime" },
            { Tone.Pit, "MR_Pit" },
            { Tone.PitRim, "MR_PitRim" },
            { Tone.Door, "MR_Door" },
            { Tone.ExitLamp, "MR_ExitLamp" },
            { Tone.Gate, "MR_Gate" }
        };

        private const string Buildings = "Assets/Synty/PolygonConstruction/Prefabs/Buildings/";
        private const string Props = "Assets/Synty/PolygonConstruction/Prefabs/Props/";

        /// <summary>С какого предмета пака снимается цвет тона.</summary>
        private static readonly Dictionary<Tone, string> Sources = new Dictionary<Tone, string>
        {
            // Леса — единственная в паке крупная серая сталь без ржавчины и без
            // строительного жёлтого. Двутавр SM_Prop_I_Beam_01 сюда не годится:
            // он тёмно-красный, и плиты вышли бы ржавыми.
            { Tone.Plate, Props + "SM_Prop_Scaffold_01.prefab" },

            // Тот же настил, которым 4.1 стелет площадки: краска обязана совпасть
            // с моделью, на которую ложится.
            { Tone.Deck, Buildings + "SM_Bld_Concrete_Floor_01.prefab" },

            { Tone.Wall, Buildings + "SM_Bld_Concrete_Wall_02.prefab" },
            { Tone.Door, Props + "SM_Prop_Fence_MetalSheet_01.prefab" }
        };

        /// <summary>
        /// Сохранённые сырые замеры — на случай, когда паков в проекте нет.
        ///
        /// Нужны по той же причине, что и у «Верю / не верю» (STATE 3.70):
        /// <b>замер работает один раз за сессию.</b> Дальше текстуры пака уже
        /// разобраны, повторный прогон ничего не мерит, и без сохранённых чисел
        /// цвет в `.mat` зависел бы от того, в какой момент нажали пересборку.
        ///
        /// Хранится сырое среднее, а не готовый тон: обесцвечивание и затемнение
        /// считаются в одном месте, иначе замеренный и сохранённый пути
        /// разъезжаются.
        /// </summary>
        private static readonly Dictionary<Tone, Color> Fallback = new Dictionary<Tone, Color>
        {
            { Tone.Plate, new Color(0.414f, 0.407f, 0.416f) },
            { Tone.Deck, new Color(0.526f, 0.488f, 0.439f) },
            { Tone.Wall, new Color(0.526f, 0.488f, 0.439f) },
            { Tone.Door, new Color(0.420f, 0.394f, 0.394f) }
        };

        /// <summary>
        /// Во сколько раз плита обязана быть светлее дна пропасти. Ниже этого
        /// маршрут перестаёт читаться силуэтом с дальнего конца зоны ожидания.
        /// </summary>
        private const float MinPlateToPitContrast = 6f;

        /// <summary>
        /// Во сколько раз дно пропасти темнее стен. Дно — самое тёмное в кадре,
        /// но не чёрная дыра: на 0.13 оно уходило в чистый <c>#0E0E0E</c>, и
        /// провал переставал читаться глубиной. Читаемость глубины дальше
        /// держат стены провала и дым подфазы 4.3, а не сам тон дна.
        /// </summary>
        private const float PitFactor = 0.19f;

        /// <summary>Во сколько раз конструкция темнее плиты: иначе плита сливается с балкой под ней.</summary>
        private const float StructureFactor = 0.74f;

        /// <summary>Во сколько раз стена темнее настила: граница пола и стены обязана читаться.</summary>
        private const float WallFactor = 0.86f;

        /// <summary>Во сколько раз дверь темнее стены: цель обязана выделяться силуэтом.</summary>
        private const float DoorFactor = 0.62f;

        /// <summary>
        /// Сколько цветности остаётся у бетона и стали. Замер пака даёт тёплый
        /// песочный тон, и на кадрах 4.1 площадка читалась земляной, а не
        /// бетонной — ровно то же было у «Переноски» (STATE 3.65). Яркость
        /// берётся из замера, цветность снимается почти до нуля.
        /// </summary>
        private const float ConcreteChroma = 0.20f;

        /// <summary>У стали цветности остаётся ещё меньше: холодный металл против тёплого бетона.</summary>
        private const float SteelChroma = 0.12f;

        /// <summary>Во сколько раз цоколь темнее стены.</summary>
        private const float WainscotFactor = 0.72f;

        /// <summary>Во сколько раз перекрытие темнее стен: верх кадра уходит в тень.</summary>
        private const float CeilingFactor = 0.40f;

        /// <summary>Во сколько раз стена провала темнее цеха. Между ней и дном остаётся ступенька.</summary>
        private const float PitWallFactor = 0.34f;

        /// <summary>Во сколько раз грязь темнее настила, на котором лежит.</summary>
        private const float GrimeFactor = 0.74f;

        private static readonly Dictionary<Tone, Material> cache = new Dictionary<Tone, Material>(12);
        private static readonly Dictionary<Tone, Color> measured = new Dictionary<Tone, Color>(8);
        private static readonly List<string> unmeasured = new List<string>(4);

        /// <summary>Замерить пак и завести на диске всю палитру целиком.</summary>
        internal static void Begin()
        {
            cache.Clear();
            measured.Clear();
            unmeasured.Clear();
            SyntyPalette.ClearCache();

            foreach (KeyValuePair<Tone, string> pair in Sources)
            {
                if (SyntyPalette.TryAverage(pair.Value, out Color average))
                {
                    measured[pair.Key] = average;
                }
                else
                {
                    unmeasured.Add(pair.Key.ToString());
                }
            }

            foreach (Tone tone in System.Enum.GetValues(typeof(Tone)))
            {
                Get(tone);
            }
        }

        /// <summary>Дописать заведённые материалы на диск. Вызывать в конце пересборки.</summary>
        internal static void Flush()
        {
            AssetDatabase.SaveAssets();
        }

        /// <summary>Цвет тона: замеренный, если пак на месте, иначе сохранённый.</summary>
        internal static Color ColorOf(Tone tone)
        {
            switch (tone)
            {
                case Tone.Plate:
                    return Desaturate(Raw(Tone.Plate), SteelChroma);

                case Tone.Structure:
                    return Dim(ColorOf(Tone.Plate), StructureFactor);

                case Tone.Deck:
                    return Desaturate(Raw(Tone.Deck), ConcreteChroma);

                // Стена и настил у пака один и тот же бетон, и замер даёт им
                // один цвет — на кадре они слились бы в сплошное пятно, и
                // граница пола перестала бы читаться.
                case Tone.Wall:
                    return Dim(Desaturate(Raw(Tone.Wall), ConcreteChroma), WallFactor);

                case Tone.Pit:
                    return Dim(ColorOf(Tone.Wall), PitFactor);

                case Tone.Wainscot:
                    return Dim(ColorOf(Tone.Wall), WainscotFactor);

                case Tone.Ceiling:
                    return Dim(ColorOf(Tone.Wall), CeilingFactor);

                // Между цехом и дном обязана быть ступенька: стена провала
                // светлее дна и темнее цеха, и ровно эта разница читается
                // глубиной. Одним тоном с дном провал становится чёрной дырой,
                // одним тоном с цехом — перестаёт быть провалом вовсе.
                case Tone.PitWall:
                    return Dim(ColorOf(Tone.Wall), PitWallFactor);

                // Пятно на полу обязано быть темнее пола. Модель пыли из пака
                // идёт песочной и светлее бетона, и на кадре 4.3 восемь таких
                // пятен читались лужами песка посреди цеха, а не грязью.
                case Tone.Grime:
                    return Dim(ColorOf(Tone.Deck), GrimeFactor);

                case Tone.Door:
                    return Dim(Desaturate(Raw(Tone.Door), SteelChroma), DoorFactor);

                // Предупреждающий красный из брифа. Живёт только на кромке
                // провала и больше нигде: на плитах его быть не может по
                // правилу неразличимости, а других опасных объектов в игре нет.
                case Tone.PitRim:
                    return new Color32(0xC4, 0x36, 0x2E, 0xFF);

                case Tone.ExitLamp:
                    return new Color32(0x35, 0xC2, 0x4A, 0xFF);

                // Почти бесцветное стекло с холодным отливом. Жёлтым барьер
                // уводил всю арену за собой в оливковый — а смотрят сквозь него
                // весь раунд.
                case Tone.Gate:
                    return new Color(0.78f, 0.84f, 0.90f, 0.09f);

                default:
                    return Color.white;
            }
        }

        /// <summary>Материал тона. Ассет заводится при первом обращении, свойства переписываются всегда.</summary>
        internal static Material Get(Tone tone)
        {
            if (cache.TryGetValue(tone, out Material cached) && cached != null)
            {
                return cached;
            }

            string path = $"{Folder}/{Files[tone]}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find(LitShaderName);
                if (shader == null)
                {
                    Debug.LogError($"Шейдер '{LitShaderName}' не найден — палитру «Рейса» не собрать");
                    return null;
                }

                EnsureFolder();
                material = new Material(shader) { name = Files[tone] };
                AssetDatabase.CreateAsset(material, path);
            }

            // Грязным материал помечается только при настоящем изменении:
            // безусловный SetDirty переписывал файл каждой пересборкой при
            // тех же самых числах, а сходятся в репозитории три ветки.
            Color before = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.clear;
            float smoothBefore = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : -1f;
            float metalBefore = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : -1f;

            Apply(material, tone);

            Color after = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.clear;
            float smoothAfter = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : -1f;
            float metalAfter = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : -1f;

            bool changed = before != after
                || !Mathf.Approximately(smoothBefore, smoothAfter)
                || !Mathf.Approximately(metalBefore, metalAfter);

            if (changed)
            {
                EditorUtility.SetDirty(material);
            }

            cache[tone] = material;
            return material;
        }

        /// <summary>
        /// Таблица замеров и контраст плиты к пропасти. Печатается пересборкой,
        /// потому что цвет обязан быть проверяемым числом, а не словом
        /// «подобрал», а контраст здесь — не украшение, а условие механики.
        /// </summary>
        internal static string Report()
        {
            var report = new StringBuilder();
            report.Append("🎨 «Рейс на память», палитра 4.2 — замер по UV моделей пака");

            foreach (Tone tone in System.Enum.GetValues(typeof(Tone)))
            {
                Color color = ColorOf(tone);
                report.Append("\n— ").Append(tone.ToString().PadRight(10))
                    .Append(SyntyPalette.Hex(color))
                    .Append(" → ").Append(Files[tone]);

                if (measured.ContainsKey(tone))
                {
                    string source = Sources[tone];
                    report.Append("  (замер: ")
                        .Append(source.Substring(source.LastIndexOf('/') + 1).Replace(".prefab", string.Empty))
                        .Append(')');
                }
                else if (Sources.ContainsKey(tone))
                {
                    report.Append("  (замер недоступен, сохранённое значение)");
                }
            }

            float plate = SyntyPalette.Luminance(ColorOf(Tone.Plate));
            float pit = SyntyPalette.Luminance(ColorOf(Tone.Pit));
            float contrast = pit > 0.0001f ? plate / pit : 999f;
            report.Append("\n— контраст плита / пропасть: ").Append(contrast.ToString("F1"))
                .Append("× при пороге ").Append(MinPlateToPitContrast.ToString("F0")).Append('×')
                .Append(contrast >= MinPlateToPitContrast ? " ✔" : " ✘ маршрут не читается с сорока метров");

            if (unmeasured.Count > 0)
            {
                report.Append("\n⚠️ не замерено: ").Append(string.Join(", ", unmeasured));
            }

            return report.ToString();
        }

        private static void Apply(Material material, Tone tone)
        {
            Color color = ColorOf(tone);

            switch (tone)
            {
                // Стекло барьера намеренно матовое. На 0.75 оно ловило блик от
                // каждого плафона и на кадре сбоку читалось сплошной белёсой
                // плоскостью поперёк арены — то есть делало ровно то, от чего
                // его спасали прозрачностью. Смотреть сквозь барьер обязаны все
                // и весь раунд.
                case Tone.Gate:
                    HoleInWallMaterials.ConfigureTransparent(material, color, 0.18f);
                    break;

                case Tone.ExitLamp:
                    HoleInWallMaterials.ConfigureEmissive(material, color, 2.4f);
                    break;

                // Сталь блестит, но сдержанно: зеркальный настил во весь маршрут
                // ловил бы блики от каждой лампы и мешал считывать ряды.
                case Tone.Plate:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.45f, 0.70f);
                    break;

                case Tone.Structure:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.35f, 0.80f);
                    break;

                case Tone.Door:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.40f, 0.65f);
                    break;

                // Бетон и дно намеренно матовые: блик на полу спорит с плитами,
                // а в этой игре блестеть имеет право только маршрут.
                default:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.06f, 0f);
                    break;
            }
        }

        /// <summary>Сырой замер тона или сохранённое значение, если пака нет.</summary>
        private static Color Raw(Tone tone)
        {
            if (measured.TryGetValue(tone, out Color value))
            {
                return value;
            }

            return Fallback.TryGetValue(tone, out Color saved) ? saved : Color.grey;
        }

        /// <summary>Снять цветность, оставив яркость: так тёплый замер пака становится честным серым.</summary>
        private static Color Desaturate(Color color, float chroma)
        {
            float grey = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
            return new Color(
                Mathf.Lerp(grey, color.r, chroma),
                Mathf.Lerp(grey, color.g, chroma),
                Mathf.Lerp(grey, color.b, chroma),
                color.a);
        }

        private static Color Dim(Color color, float factor)
        {
            return new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials"))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Materials");
            }

            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Materials", "MemoryRun");
            }
        }
    }
}
