using System.Collections.Generic;
using System.Text;
using Igruha.Minigames.HoleInWall;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Палитра «Экзамена» как ассеты — подфаза 4.2.
    ///
    /// <b>Почему замер, а не пипетка.</b> Паки Synty текстурированы одним
    /// атласом: у модели нет «цвета», есть кусок общей картинки. Пипеткой
    /// снимается случайный пиксель, и на полосатом куске ответ будет либо
    /// чёрный, либо белый — оба неверны. Цвет считается усреднением по UV
    /// (<see cref="SyntyPalette"/>), и тогда крашеный блокаут садится ровно
    /// в тон моделям дресса, а стык коробки с моделью перестаёт читаться.
    ///
    /// <b>Почему ассеты, а не материалы в памяти.</b> Материал, созданный на
    /// лету, уезжает внутрь `.unity` копией на каждый объект: его нельзя ни
    /// переиспользовать, ни запечь на 4.6, а слияние веток разводит копии
    /// молча. Ассет с GUID переживает и то, и другое.
    ///
    /// ⚠️ <b>Замер работает один раз за сессию, и это не наша ошибка.</b>
    /// У импортёра Synty Read/Write выключен, Unity держит вершины меша только
    /// до первой выгрузки, и второй прогон в той же сессии уже ничего не мерит
    /// (разбор — STATE 3.65 и 3.70). Поэтому источник истины — <b>сохранённые
    /// сырые средние</b> ниже, а замер лишь подтверждает их, когда получается.
    /// Числа сняты 04.09 тем же усреднением по UV, с временно открытыми на
    /// чтение мешами; настройки импорта пака после замера возвращены.
    /// </summary>
    internal static class ExamPalette
    {
        /// <summary>Поверхность палитры. Одна на роль, а не на объект.</summary>
        internal enum Tone
        {
            /// <summary>Штукатурка стен выше панелей.</summary>
            Stone,

            /// <summary>Пол зала. Темнее стен: иначе линия примыкания пропадает.</summary>
            Floor,

            /// <summary>Шов между плитами пола: тонкая тёмная линия по модулю зала.</summary>
            FloorSeam,

            /// <summary>Тёмное дерево: панели по низу стен, обвязка доски, карниз кафедры, борта кафедры.</summary>
            Panel,

            /// <summary>Потолок. Светлее стен — зал под перекрытием и без этого глухой.</summary>
            Ceiling,

            /// <summary>Грифель доски. Матовый: блик по нему съедает текст вопроса.</summary>
            Slate,

            /// <summary>Яма под створками. Почти чёрная и ничего не отражает.</summary>
            Pit,

            /// <summary>Крашеный металл: петли, подвесы указателей, вешалка.</summary>
            Metal,

            /// <summary>Шов между половинами створки.</summary>
            Seam,

            /// <summary>Вариант А. Светится: кромка платформы — канал читаемости, а не украшение.</summary>
            SideA,

            /// <summary>Вариант Б.</summary>
            SideB,

            /// <summary>Корпус школьных шкафчиков. Свой тон ровно потому, что родной синий занят вариантом А.</summary>
            LockerBody,

            /// <summary>Дорожка зоны возврата: единственное красное пятно зала.</summary>
            Runner,

            /// <summary>Поле подвесного указателя под букву.</summary>
            SignPlate,

            /// <summary>Экран ретро-монитора на кафедре.</summary>
            ScreenGlow,

            /// <summary>Дневной свет за окном: пересвеченная плоскость, а не стекло.</summary>
            Daylight,

            /// <summary>Позолота рам портретов. Тусклая: яркое золото спорит с янтарным вариантом Б.</summary>
            Gilt
        }

        private const string Folder = "Assets/_Project/Materials/Exam";
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        /// <summary>
        /// Имена файлов. `Exam_HatchMetal`, `Exam_HatchSeam`, `Exam_SideA` и
        /// `Exam_SideB` завёл дресс 4.1, и палитра их не переименовывает,
        /// а переписывает: ссылки на них уже стоят в сцене.
        /// </summary>
        private static readonly Dictionary<Tone, string> Files = new Dictionary<Tone, string>
        {
            { Tone.Stone, "Exam_Stone" },
            { Tone.Floor, "Exam_Floor" },
            { Tone.FloorSeam, "Exam_FloorSeam" },
            { Tone.Panel, "Exam_Panel" },
            { Tone.Ceiling, "Exam_Ceiling" },
            { Tone.Slate, "Exam_Slate" },
            { Tone.Pit, "Exam_Pit" },
            { Tone.Metal, "Exam_HatchMetal" },
            { Tone.Seam, "Exam_HatchSeam" },
            { Tone.SideA, "Exam_SideA" },
            { Tone.SideB, "Exam_SideB" },
            { Tone.LockerBody, "Exam_Locker" },
            { Tone.Runner, "Exam_Runner" },
            { Tone.SignPlate, "Exam_SignPlate" },
            { Tone.ScreenGlow, "Exam_Screen" },
            { Tone.Daylight, "Exam_Daylight" },
            { Tone.Gilt, "Exam_Gilt" }
        };

        /// <summary>С какого предмета пака снимается цвет тона.</summary>
        private static readonly Dictionary<Tone, string> Sources = new Dictionary<Tone, string>
        {
            // Камень базового кита — им же одеты стены, пол и колонны во всех
            // шести паках, где этот кит есть. Стены зала обязаны сесть в него.
            { Tone.Stone, "Assets/Synty/PolygonAncientEmpire/Prefabs/Buildings/Base/SM_Bld_Base_Wall_01.prefab" },

            // Дерево снимается со стола Ведущего, а не с настила: у плиты
            // настила половина площади — бетонная изнанка, и среднее уводит
            // тон в серый. Стол стоит в том же кадре, и панели обязаны
            // совпасть именно с ним.
            { Tone.Panel, "Assets/Synty/PolygonNightclubs/Prefabs/Props/SM_Prop_Desk_01.prefab" },

            // Крашеный металл — с корпуса ЭЛТ: он единственный предмет
            // на арене, у которого металл и есть весь предмет.
            { Tone.Metal, "Assets/Synty/PolygonNightclubs/Prefabs/Props/SM_Prop_TV_02.prefab" }
        };

        /// <summary>
        /// <b>Сырые</b> средние пака, снятые замером 04.09 при временно
        /// открытых на чтение мешах. Не «примерно такие»: это результат того же
        /// усреднения по UV, записанный на диск.
        ///
        /// Хранится сырое среднее, а не готовый тон: приглушение пола и
        /// обесцвечивание шкафчиков считаются в одном месте. Разъедься эти два
        /// пути — и цвет в `.mat` начал бы зависеть от того, в какой момент
        /// сессии нажали пересборку. Ровно это случилось у «Переноски» 04.09.
        /// </summary>
        private static readonly Dictionary<Tone, Color> Fallback = new Dictionary<Tone, Color>
        {
            { Tone.Stone, new Color(0.708f, 0.674f, 0.646f) },
            { Tone.Panel, new Color(0.496f, 0.353f, 0.273f) },
            { Tone.Metal, new Color(0.239f, 0.261f, 0.268f) }
        };

        /// <summary>
        /// Замер шкафчика PolygonKids, 04.09: `#2C67A7`. Цвет варианта А —
        /// `#2A6BB8`. Разница меньше десятой доли по каждому каналу, то есть
        /// синий шкаф в кадре — это второе «синее = А». Число хранится здесь
        /// не для использования, а как обоснование перекраски.
        /// </summary>
        private static readonly Color LockerNative = new Color(0.173f, 0.405f, 0.653f);

        /// <summary>Во сколько раз пол темнее стен. Без разницы яркости линия примыкания пропадает.</summary>
        private const float FloorFactor = 0.78f;

        /// <summary>Во сколько раз шов пола темнее плиты.</summary>
        private const float SeamFloorFactor = 0.58f;

        /// <summary>Насколько потолок светлее стен: зал перекрыт, и на равной яркости он глухой.</summary>
        private const float CeilingLift = 0.28f;

        /// <summary>Во сколько раз корпус шкафчика темнее стены.</summary>
        private const float LockerFactor = 0.58f;

        /// <summary>Сколько цветности остаётся у шкафчика: серая крашеная сталь, а не бежевый камень.</summary>
        private const float LockerChroma = 0.22f;

        /// <summary>Сколько цветности остаётся у дорожки возврата: сукно, а не бархат.</summary>
        private const float RunnerChroma = 0.52f;

        /// <summary>Во сколько раз шов темнее дерева панелей.</summary>
        private const float SeamFactor = 0.24f;

        /// <summary>
        /// Свечение кромки платформы. Втрое слабее неона «Дырки в стене»
        /// (<c>HoleInWallPalette.NeonEmission</c> = 3): там свет был зрелищем,
        /// здесь — подсказкой, и полоса, которая жжёт кадр, спорит с буквой.
        /// </summary>
        private const float EdgeEmission = 0.9f;

        /// <summary>Свечение плоскости за окном. Полдень за стеклом, а не подсветка витрины.</summary>
        private const float DaylightEmission = 2.6f;

        private static readonly Dictionary<Tone, Material> cache = new Dictionary<Tone, Material>(16);
        private static readonly Dictionary<Tone, Color> measured = new Dictionary<Tone, Color>(4);
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
                    Debug.LogError($"Шейдер '{LitShaderName}' не найден — палитру «Экзамена» не собрать");
                    return null;
                }

                EnsureFolder();
                material = new Material(shader) { name = Files[tone] };
                AssetDatabase.CreateAsset(material, path);
            }

            Apply(material, tone);
            EditorUtility.SetDirty(material);
            cache[tone] = material;
            return material;
        }

        /// <summary>Цвет тона: замеренный, если пак читается, иначе сохранённый.</summary>
        internal static Color ColorOf(Tone tone)
        {
            switch (tone)
            {
                case Tone.Stone:
                case Tone.Panel:
                case Tone.Metal:
                    return Raw(tone);

                // Пол — тот же камень, приглушённый. Свой материал ему не нужен:
                // на настоящем полу камень не другой, он просто затоптан.
                case Tone.Floor:
                    return Dim(Raw(Tone.Stone), FloorFactor);

                // Шов между плитами пола. Тёмный настолько, чтобы линия читалась
                // с высоты глаз, и не настолько, чтобы пол распался на клетки.
                case Tone.FloorSeam:
                    return Dim(ColorOf(Tone.Floor), SeamFloorFactor);

                case Tone.Ceiling:
                    return Color.Lerp(Raw(Tone.Stone), Color.white, CeilingLift);

                // Шкафчики. Родной синий пака — `#2C67A7` при варианте А
                // `#2A6BB8`: это второе «синее = А» в кадре, и оно на стороне
                // одной из платформ. Яркость взята от камня, цветность снята
                // почти до нуля — крашеная сталь, а не бежевая стена.
                case Tone.LockerBody:
                    return Desaturate(Dim(Raw(Tone.Stone), LockerFactor), LockerChroma);

                case Tone.Seam:
                    return Dim(Raw(Tone.Panel), SeamFactor);

                case Tone.SideA:
                    return ExamArenaBuilder.SideColor(Minigames.Exam.ExamSide.A);

                case Tone.SideB:
                    return ExamArenaBuilder.SideColor(Minigames.Exam.ExamSide.B);

                // Грифель. Назван брифом, а не снят с пака: классная доска
                // PolygonKids даёт в среднем `#6F5C47` — это её деревянная
                // рама, а не полотно, и мерить по ней грифель бессмысленно.
                case Tone.Slate:
                    return new Color(0.105f, 0.215f, 0.145f);

                case Tone.Pit:
                    return new Color(0.072f, 0.072f, 0.082f);

                // Дорожка зоны возврата. Первый замес — чистый тёмно-красный —
                // рендер приёмки отменил: 18.7 × 3.6 м насыщенного красного
                // во всю ширину зала оттягивали взгляд с платформ, а строгий
                // зал превращали в ковровую дорожку кинопремии. Цветность
                // снята почти вдвое: сукно, а не бархат.
                case Tone.Runner:
                    return Desaturate(new Color(0.40f, 0.145f, 0.135f), RunnerChroma);

                case Tone.SignPlate:
                    return new Color(0.93f, 0.92f, 0.88f);

                // Экран монитора Ведущего. Первый замес — насыщенный зелёный
                // `#47B866` — снят рендером кадра кафедры: экран 0.42 × 0.32 м
                // стоит ровно в центре того, что Ведущий видит всю фазу печати,
                // и ярко-зелёный прямоугольник в тёмном корпусе читался не
                // включённым монитором, а незакрашенной заглушкой блокаута.
                // Взят тёмный люминофор: корпус остаётся тёмным, свечение даёт
                // эмиссия. «Монитор включён» — это подсветка, а не заливка.
                case Tone.ScreenGlow:
                    return new Color(0.11f, 0.26f, 0.16f);

                // Дневной свет за окном. Холодный и почти белый: зал освещён
                // тёплыми лампами, и разница температур — единственное, чем
                // окно отличается от лампы, когда за ним нет улицы.
                case Tone.Daylight:
                    return new Color(0.88f, 0.93f, 1f);

                // Позолота рам. Взята заметно темнее и глуше родной: рама пака
                // приезжает ярко-оранжевой, а это почти цвет варианта Б
                // (`#C78017`). Четыре ярких янтарных пятна на дальней стене
                // спорили бы с тем единственным янтарным, которое в этой игре
                // что-то значит.
                case Tone.Gilt:
                    return new Color(0.50f, 0.39f, 0.21f);

                default:
                    return Color.white;
            }
        }

        private static void Apply(Material material, Tone tone)
        {
            Color color = ColorOf(tone);

            switch (tone)
            {
                // Кромка платформы и экран монитора светятся. Всё остальное
                // в зале — нет: зал строгий, и второй источник света в кадре
                // спорил бы с тем, ради чего первый поставлен.
                case Tone.SideA:
                case Tone.SideB:
                    HoleInWallMaterials.ConfigureEmissive(material, color, EdgeEmission);
                    break;

                // Экран светит с множителем больше кромочного, хотя в кадре
                // он темнее её. Причина в том, что эмиссия считается от цвета
                // в линейном пространстве: тёмный цвет уходит в линейном
                // в тридцать раз ниже светлого, и тот же множитель дал бы
                // выключенный монитор. Итог по замеру — эмиссия примерно
                // вдвое ниже прежней зелёной заливки: экран светится, но
                // пятном в кадре не становится.
                case Tone.ScreenGlow:
                    HoleInWallMaterials.ConfigureEmissive(material, color, EdgeEmission * 3.4f);
                    break;

                // Окно светит заметно сильнее кромки платформы: оно изображает
                // улицу в полдень, и приглушённое читается не окном,
                // а закрашенным белым стеклом.
                case Tone.Daylight:
                    HoleInWallMaterials.ConfigureEmissive(material, color, DaylightEmission);
                    break;

                // Грифель матовый до предела. Урок 3.70: в зале с точечными
                // лампами гладкость материала решает больше, чем его цвет, —
                // тёмно-зелёное полотно с бликом читается серым, а текст
                // вопроса на нём пропадает.
                case Tone.Slate:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.03f, 0f);
                    break;

                // Яма не отражает ничего: любой блик снизу превращает провал
                // из темноты в лакированную поверхность.
                case Tone.Pit:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.02f, 0f);
                    break;

                case Tone.Metal:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.42f, 0.55f);
                    break;

                case Tone.Gilt:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.55f, 0.75f);
                    break;

                // Пол чуть глянцевее стен: натёртый камень зала. Выше 0.2 он
                // начинает ловить лампы бликами во весь кадр.
                case Tone.Floor:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.16f, 0f);
                    break;

                case Tone.Panel:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.18f, 0f);
                    break;

                default:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.06f, 0f);
                    break;
            }
        }

        /// <summary>Сырое среднее: замер, если он прошёл, иначе сохранённое число.</summary>
        private static Color Raw(Tone tone)
        {
            if (measured.TryGetValue(tone, out Color live))
            {
                return live;
            }

            return Fallback.TryGetValue(tone, out Color saved) ? saved : Color.white;
        }

        /// <summary>
        /// Приглушить тон. Умножение идёт в линейном пространстве: «в полтора
        /// раза темнее» — это про свет, а не про коды цветов.
        /// </summary>
        private static Color Dim(Color srgb, float factor)
        {
            Color linear = srgb.linear;
            return new Color(linear.r * factor, linear.g * factor, linear.b * factor, 1f).gamma;
        }

        /// <summary>Снять цветность, сохранив яркость: серый того же тона, а не серый вообще.</summary>
        private static Color Desaturate(Color srgb, float chroma)
        {
            Color linear = srgb.linear;
            float grey = SyntyPalette.Luminance(srgb);
            return new Color(
                Mathf.Lerp(grey, linear.r, chroma),
                Mathf.Lerp(grey, linear.g, chroma),
                Mathf.Lerp(grey, linear.b, chroma), 1f).gamma;
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(Folder))
            {
                return;
            }

            AssetDatabase.CreateFolder("Assets/_Project/Materials", "Exam");
        }

        /// <summary>
        /// Таблица замеров: с какого предмета пака снят каждый цвет и во что он
        /// превратился. Печатается пересборкой — цвет обязан быть проверяемым
        /// числом, а не словом «подобрал».
        /// </summary>
        internal static string Report()
        {
            var report = new StringBuilder();
            report.Append("🎨 «Экзамен», палитра 4.2 — замер по UV моделей пака");

            foreach (Tone tone in System.Enum.GetValues(typeof(Tone)))
            {
                Color color = ColorOf(tone);
                string origin = measured.ContainsKey(tone)
                    ? "замер"
                    : Sources.ContainsKey(tone) ? "сохранённое" : "по брифу";

                report.Append("\n— ").Append(tone.ToString().PadRight(12))
                    .Append(SyntyPalette.Hex(color))
                    .Append("  L=").Append(SyntyPalette.Luminance(color).ToString("F3"))
                    .Append("  ").Append(origin);
            }

            report.Append("\n\nШкафчик пака — ").Append(SyntyPalette.Hex(LockerNative))
                .Append(", вариант А — ").Append(SyntyPalette.Hex(ColorOf(Tone.SideA)))
                .Append(": перекрашен в ").Append(SyntyPalette.Hex(ColorOf(Tone.LockerBody)));

            if (unmeasured.Count > 0)
            {
                report.Append("\n⚠️ не замерено (взяты сохранённые): ").Append(string.Join(", ", unmeasured.ToArray()));
                report.Append("\n   Это норма: меши паков закрыты на чтение, замер живёт один прогон за сессию.");
            }

            return report.ToString();
        }
    }
}
