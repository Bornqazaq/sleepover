using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Igruha.Core.Arena;
using Igruha.Core.Spawning;
using Igruha.Core.UI;
using Igruha.Minigames.Circus;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Пересборка цирковой арены по CircusArenaConfig. Арена общая с «Порядком
    /// банок», и правки размеров здесь неизбежны: параметрический билдер стоит
    /// ровно затем, чтобы смена радиуса ямы была одним числом в конфиге,
    /// а не переделкой сцены на полдня.
    ///
    /// Ноль по Y — дно ямы (опилки), от него считаются все высоты.
    /// Пол шатра лежит выше на глубину ямы.
    ///
    /// <b>Арт живёт здесь же (подфаза 4.1).</b> Каталог моделей — в
    /// <see cref="CircusDress"/>, цвета — в <see cref="CircusPalette"/>, оба
    /// общие на две игры. Дресс не заменяет блокаут, а надевается на него:
    /// коробка остаётся на месте со своим коллайдером и слоем, у неё гаснет
    /// рендерер, внутрь садится модель. Выверенная фазами 2–3 геометрия
    /// физически не может сдвинуться от арта.
    /// </summary>
    internal static class CircusArenaBuilder
    {
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/CircusArenaConfig.asset";
        private const string GroundLayerName = "Ground";

        /// <summary>
        /// Зерно дресса. <b>Свой генератор, а не общий с билдером:</b> общий
        /// сдвинул бы последовательность, по которой раскладываются
        /// геймплейные объекты, и проверенная планировка поехала бы от одной
        /// лишь смены модели.
        /// </summary>
        private const int DressSeed = 8241;

        private static System.Random dressRandom;

        // Толщины — от читаемости блокаута, а не от баланса: в конфиг не вынесены.
        private const float FloorThickness = 0.2f;
        private const float RimThickness = 0.4f;
        private const float TentWallThickness = 0.4f;
        private const float BarThickness = 0.08f;
        private const float CageFloorThickness = 0.1f;
        private const float ChainThickness = 0.1f;
        private const float ScoreboardDepth = 0.3f;

        // Табло. Единицы канваса — сантиметры (масштаб 0.01), поэтому кегль
        // читается прямо в метрах: 30 единиц = 0.30 м высоты символа.
        private const float BoardUnitsPerMeter = 100f;
        private const float BoardCanvasScale = 0.01f;
        private const float BoardTitleBand = 52f;
        private const float BoardSubtitleBand = 40f;
        private const float BoardTitleFont = 46f;
        private const float BoardSubtitleFont = 34f;
        private const float BoardRowFont = 26f;
        private const float BoardRowHeight = 30f;
        private const float BoardPadding = 12f;
        private const int BoardColumns = 2;
        private const int BoardRowCapacity = 8;

        private const int RimSegments = 48;
        private const int TentFloorSegments = 48;
        private const int TentWallSegments = 32;
        private const int RiggingSegments = 32;
        private const int BarsPerCageSide = 5;

        // ---- Арт 4.1. Всё ниже — только вид: коллайдеров не несёт, слой Default.

        /// <summary>Насколько купол поднимается над фермой, доля радиуса шатра.</summary>
        private const float CanopyRiseFactor = 0.42f;

        /// <summary>Толщина полотнища купола. Оно видно снизу, и нулевая толщина дала бы просвечивающий лист.</summary>
        private const float CanopyThickness = 0.25f;

        /// <summary>Ширина клина купола относительно шага сегмента: с запасом, иначе у вершины расходятся щели.</summary>
        private const float CanopyOverlap = 0.78f;

        /// <summary>Поясок поверх кирпичного борта: выступ на сторону, м.</summary>
        private const float CopeOverhang = 0.08f;

        private const float CopeThickness = 0.14f;

        /// <summary>Толщина пояса фермы и раскоса.</summary>
        private const float TrussBarThickness = 0.14f;

        /// <summary>Разнос поясов фермы по вертикали, м.</summary>
        private const float TrussSpread = 0.35f;

        /// <summary>Угловая стойка клетки. Толще прутьев: она и держит силуэт клетки на расстоянии.</summary>
        private const float CagePostThickness = 0.16f;

        /// <summary>Поперечный пояс стены клетки. Без него прутья читаются частоколом, а не решёткой.</summary>
        private const float CageRailThickness = 0.10f;

        /// <summary>Глубина панели табло после дресса. Было 0.30 — плита; стало 0.10 — щит в раме.</summary>
        private const float BoardPanelDepth = 0.10f;

        /// <summary>Сечение рамы табло.</summary>
        private const float BoardFrameThickness = 0.16f;
        /// <summary>Сторона, смотрящая наружу арены — противоположная, 180°.</summary>
        private const int OuterWallSide = 2;
        /// <summary>Встроенный слой Unity «Ignore Raycast»: физика работает, лучи проходят насквозь.</summary>
        private const int IgnoreRaycastLayer = 2;
        private const int SlatsPerCageDoor = 4;

        /// <summary>Нахлёст сегментов кольца: встык они расходятся на округлении и оставляют щели.</summary>
        private const float SegmentOverlap = 1.03f;

        [MenuItem("Igruha/Minigames/Rebuild Circus Arena")]
        private static void Rebuild()
        {
            var config = AssetDatabase.LoadAssetAtPath<CircusArenaConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError("CircusArenaBuilder: не найден " + ConfigPath);
                return;
            }

            GameObject arenaRoot = GameObject.Find("_Arena");
            GameObject spawnsRoot = GameObject.Find("_Spawns");
            if (arenaRoot == null || spawnsRoot == null)
            {
                // Арена общая: этот билдер работает и в Stopwatch, и в CansOrder.
                Debug.LogError("CircusArenaBuilder: открой Stopwatch.unity или CansOrder.unity — не найдены _Arena/_Spawns.");
                return;
            }

            int groundLayer = LayerMask.NameToLayer(GroundLayerName);
            if (groundLayer < 0)
            {
                Debug.LogError($"CircusArenaBuilder: не найден слой {GroundLayerName} — прыжок и обход камерой сломаются.");
                return;
            }

            dressRandom = new System.Random(DressSeed);
            CircusDress.Begin();
            CircusPalette.Begin();

            BuildPitFloor(arenaRoot.transform, config, groundLayer);
            BuildPitRim(arenaRoot.transform, config, groundLayer);
            BuildTentFloor(arenaRoot.transform, config, groundLayer);
            BuildTentWall(arenaRoot.transform, config, groundLayer);
            BuildCanopy(arenaRoot.transform, config);
            BuildRigging(arenaRoot.transform, config);
            BuildScoreboard(arenaRoot.transform, config);
            BuildCages(arenaRoot.transform, config, groundLayer);
            BuildBear(arenaRoot.transform, config);
            CircusEnvironment.Build(arenaRoot.transform, config, dressRandom);
            BuildSpawns(spawnsRoot.transform, config);
            BuildBounds(config);

            // Эффекты после всего: им нужны готовые клетки и медведь, чтобы
            // повеситься на их события.
            CageStation[] builtCages = arenaRoot.GetComponentsInChildren<CageStation>(true);
            PitBear builtBear = arenaRoot.GetComponentInChildren<PitBear>(true);

            // Вернуть контроллеру ссылки на клетки и медведя. Обязательно и
            // до всего остального: пересборка пересоздала эти объекты, и без
            // этого шага игра встаёт с «клетка 0 не назначена».
            CircusWiring.Apply(builtCages, builtBear);

            CircusVfx.Build(arenaRoot.transform, config, builtCages, builtBear);

            // Звук — после эффектов и по тем же событиям. Кнопки на этот
            // момент ещё не расставлены (их ставит билдер реквизита), поэтому
            // он зовёт CircusSfx повторно и дозаполняет их.
            CircusSfx.Build(arenaRoot.transform, builtCages, builtBear);

            Validate(config);

            CircusPalette.Flush();
            ReportMissingModels();
            Debug.Log(CircusDress.Report(), arenaRoot);
            Debug.Log(CircusPalette.Report(), arenaRoot);

            // Физика декора — последним шагом сборки. Дресс срезает коллайдеры
            // моделей, и всё, что поставлено в зал само по себе, без коробки
            // блокаута, до этого шага проходилось насквозь.
            PropColliders.Build(arenaRoot);

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            Debug.Log($"Цирковая арена пересобрана в сцене «{scene.name}»: шатёр R={config.TentRadius:F2}, яма R={config.PitRadius:F2}, " +
                      $"кольцо клеток R={config.CageRingRadius:F2}, клеток {config.CageAnchorCount}, " +
                      $"зазор между клетками {config.GetCageGap():F2} м, " +
                      $"игрок достаёт до {config.GetPlayerReachFromCentre():F2} м от центра (яма {config.PitRadius:F2}), " +
                      $"дно клетки на верхней ступени {config.GetCageBottomHeight(config.MaxLevelSteps):F2} м.");
        }

        /// <summary>
        /// Модели, которых не оказалось в проекте. Паки Synty каждый ставит
        /// себе сам и в репозиторий они не идут, поэтому молчать об этом
        /// нельзя: без сообщения арена просто выйдет наполовину серой,
        /// и виноватым будет выглядеть дресс.
        /// </summary>
        private static void ReportMissingModels()
        {
            IReadOnlyList<string> missing = CircusDress.Missing;
            if (missing.Count == 0)
            {
                return;
            }

            Debug.LogWarning("CircusArenaBuilder: моделей пака не найдено — " + string.Join(", ", missing));
        }

        /// <summary>Покрасить коробку блокаута тоном палитры, не трогая её коллайдер и слой.</summary>
        private static void Paint(Transform target, CircusPalette.Tone tone)
        {
            var renderer = target.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }

            Material material = CircusPalette.Get(tone);
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        /// <summary>
        /// Декоративная коробка: видна, но ни во что не упирается.
        /// Коллайдер снимается сразу, слой — Default.
        ///
        /// Всё, что добавил арт поверх блокаута — поясок борта, пояса фермы,
        /// стойки и рейки клетки, рама табло, клинья купола, — обязано быть
        /// именно таким. Иначе арт меняет физику, и договор фазы 4 нарушен.
        /// </summary>
        private static Transform MakeDecor(Transform parent, string name, CircusPalette.Tone tone)
        {
            Transform t = MakeBox(parent, name, 0);
            StripCollider(t);
            Paint(t, tone);
            return t;
        }

        /// <summary>Опилки. Цилиндр — это диск, отдельная геометрия не нужна.</summary>
        private static void BuildPitFloor(Transform root, CircusArenaConfig config, int groundLayer)
        {
            Transform floor = ResetPrimitive(root, "PitFloor", PrimitiveType.Cylinder);
            floor.gameObject.layer = groundLayer;
            floor.position = new Vector3(0f, -FloorThickness * 0.5f, 0f);
            floor.localScale = new Vector3(config.PitRadius * 2f, FloorThickness * 0.5f, config.PitRadius * 2f);

            // Примитив-цилиндр приезжает с CapsuleCollider, а капсула не переживает
            // неравномерный масштаб: вместо плоского круга получается блюдце,
            // по краям которого игрок съезжает. Меняем на меш пола.
            StripCollider(floor);
            var pitCollider = floor.gameObject.AddComponent<MeshCollider>();
            pitCollider.sharedMesh = floor.GetComponent<MeshFilter>().sharedMesh;

            Paint(floor, CircusPalette.Tone.Sawdust);
            BuildSawdust(floor, config);
        }

        /// <summary>
        /// Опилки поверх крашеного дна: диски грунта пака вразброс.
        ///
        /// Поверх, а не вместо: диск квадратный в габарите и углов круглой ямы
        /// не закрывает, поэтому рендерер дна гасить нельзя — под опилками
        /// останется дыра. Ровно так дно пропасти «Переноски» было дырявым
        /// с 4.1 до 04.09.
        ///
        /// Диски лежат <b>на дне, а не над ним</b>: подняты на толщину своего
        /// же меша, иначе выпавший приземляется в сантиметре над опилками.
        /// </summary>
        private static void BuildSawdust(Transform floor, CircusArenaConfig config)
        {
            // ResetGroup, а не new GameObject: без него каждая пересборка
            // клала бы поверх ещё один слой дисков.
            Transform group = ResetGroup(floor.parent, "Sawdust");

            // Кольцо вразброс плюс диск в центре закрывают яму Ø 17.3 м без шва
            // по центру — там, где стоит медведь и куда падают выбывшие.
            const int Count = 7;
            float ring = config.PitRadius * 0.52f;
            for (int i = 0; i < Count; i++)
            {
                float angle = 360f / Count * i + 11f;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 at = i == 0 ? Vector3.zero : dir * ring;
                GameObject disc = CircusDress.Prop(group, $"Sawdust_{i + 1:00}",
                    "Assets/Synty/PolygonHorrorCarnival/Prefabs/Environment/SM_Env_Ground_Dirt_Round_01.prefab",
                    at + Vector3.up * 0.01f, angle * 2.3f, 0f, false);

                // Единственная модель каталога, которую перекрашиваем.
                // Родной грунт пака серо-бурый: рядом с крашеным дном ямы
                // он читается не опилками, а лужами грязи поверх них. Форма
                // диска с рваным краем при этом остаётся — ради неё он и взят.
                if (disc != null)
                {
                    DressKit.Repaint(disc, CircusPalette.Get(CircusPalette.Tone.Sawdust));
                }
            }
        }

        /// <summary>
        /// Кирпичный борт. Изнутри он выше, чем снаружи, на глубину ямы —
        /// и это единственное, что не даёт выпавшему выпрыгнуть из ямы и убежать
        /// от медведя по полу шатра: прыжок берёт 1.47 м, борт изнутри — 2.16 м.
        /// </summary>
        private static void BuildPitRim(Transform root, CircusArenaConfig config, int groundLayer)
        {
            Transform rim = ResetGroup(root, "PitRim");
            float ringRadius = config.PitRadius + RimThickness * 0.5f;
            float height = config.PitRimTop;
            float width = 2f * Mathf.PI * ringRadius / RimSegments * SegmentOverlap;

            for (int i = 0; i < RimSegments; i++)
            {
                Vector3 dir = DirectionAt(i, RimSegments);
                Transform segment = MakeBox(rim, $"Rim_{i + 1:00}", groundLayer);
                segment.position = dir * ringRadius + Vector3.up * (height * 0.5f);
                segment.rotation = Quaternion.LookRotation(dir);
                segment.localScale = new Vector3(width, height, RimThickness);
                Paint(segment, CircusPalette.Tone.Brick);

                // Поясок поверх борта. Он не только украшение: борт изнутри —
                // это шкала, по которой игрок читает, насколько его клетка
                // опустилась, и без светлой линии по верху у шкалы нет отметки.
                //
                // Коллайдера у пояска нет намеренно. Прыжок берёт 1.47 м, борт
                // изнутри 2.16 м, и запас в 0.69 м держит именно борт;
                // добавлять к нему высоту нельзя — это меняло бы договор.
                Transform cope = MakeDecor(rim, $"Cope_{i + 1:00}", CircusPalette.Tone.BrickCope);
                cope.position = dir * ringRadius + Vector3.up * (height + CopeThickness * 0.5f);
                cope.rotation = Quaternion.LookRotation(dir);
                cope.localScale = new Vector3(width, CopeThickness, RimThickness + CopeOverhang * 2f);
            }
        }

        /// <summary>
        /// Деревянный пол шатра — кольцо вокруг ямы. Сегменты идут с нахлёстом,
        /// иначе между ними остаются щели; чётные приподняты на миллиметр,
        /// потому что у совпадающих горизонтальных граней иначе мерцает z-fighting.
        /// На физику миллиметр не влияет, глазом не виден.
        /// </summary>
        private static void BuildTentFloor(Transform root, CircusArenaConfig config, int groundLayer)
        {
            Transform floor = ResetGroup(root, "TentFloor");
            float inner = config.PitRadius + RimThickness;
            float outer = config.TentRadius;
            if (outer <= inner)
            {
                return;
            }

            float span = outer - inner;
            float mid = (inner + outer) * 0.5f;
            float width = 2f * Mathf.PI * outer / TentFloorSegments * SegmentOverlap;
            float top = config.TentFloorHeight;

            for (int i = 0; i < TentFloorSegments; i++)
            {
                Vector3 dir = DirectionAt(i, TentFloorSegments);
                Transform segment = MakeBox(floor, $"TentFloor_{i + 1:00}", groundLayer);
                float y = top - FloorThickness * 0.5f + (i % 2) * 0.001f;
                segment.position = dir * mid + Vector3.up * y;
                segment.rotation = Quaternion.LookRotation(dir);
                segment.localScale = new Vector3(width, FloorThickness, span);
                Paint(segment, CircusPalette.Tone.Deck);
            }
        }

        /// <summary>
        /// Стена шатра. Полосами через сегмент: это и есть полотнище шапито.
        ///
        /// Моделью не одевается принципиально — сегмент 3.7 м длиной, и любая
        /// модель, растянутая на него, читается бревном (правило подфазы 4.1).
        /// Полосатой ткани в паках нет вовсе: `SM_Prop_Tent_Large_01` — это
        /// наружный шатёр 26.6 м, изнутри у него backface.
        ///
        /// Чётность полосы считается от индекса сегмента, поэтому число
        /// сегментов обязано остаться чётным: на нечётном две тёмные полосы
        /// сходятся в стык и кольцо теряет ритм.
        /// </summary>
        private static void BuildTentWall(Transform root, CircusArenaConfig config, int groundLayer)
        {
            Transform wall = ResetGroup(root, "TentWall");
            float ringRadius = config.TentRadius + TentWallThickness * 0.5f;
            float bottom = config.TentFloorHeight;
            float height = config.RiggingHeight - bottom;
            float width = 2f * Mathf.PI * ringRadius / TentWallSegments * SegmentOverlap;

            for (int i = 0; i < TentWallSegments; i++)
            {
                Vector3 dir = DirectionAt(i, TentWallSegments);
                Transform segment = MakeBox(wall, $"TentWall_{i + 1:00}", groundLayer);
                segment.position = dir * ringRadius + Vector3.up * (bottom + height * 0.5f);
                segment.rotation = Quaternion.LookRotation(dir);
                segment.localScale = new Vector3(width, height, TentWallThickness);
                Paint(segment, StripeTone(i));
            }
        }

        /// <summary>Тон полосы полотнища по индексу сегмента.</summary>
        private static CircusPalette.Tone StripeTone(int index)
        {
            return index % 2 == 0 ? CircusPalette.Tone.CanvasStripe : CircusPalette.Tone.CanvasCream;
        }

        /// <summary>
        /// Купол: клинья от верха стены к вершине, полосами через один — так,
        /// чтобы полоса купола продолжала полосу стены под собой.
        ///
        /// Коллайдеров нет: под куполом ходят только цепи, а клетка выше фермы
        /// не поднимается (это проверяет <see cref="Validate"/>). Тени купол
        /// отбрасывает — он и есть крыша, и без неё интерьер освещался бы так,
        /// будто шатра над ним нет.
        /// </summary>
        private static void BuildCanopy(Transform root, CircusArenaConfig config)
        {
            Transform canopy = ResetGroup(root, "Canopy");
            float radius = config.TentRadius;
            var apex = new Vector3(0f, config.RiggingHeight + radius * CanopyRiseFactor, 0f);
            float width = 2f * Mathf.PI * radius / TentWallSegments * SegmentOverlap;

            for (int i = 0; i < TentWallSegments; i++)
            {
                Vector3 dir = DirectionAt(i, TentWallSegments);
                Vector3 edge = dir * radius + Vector3.up * config.RiggingHeight;
                Vector3 axis = apex - edge;

                Transform panel = MakeDecor(canopy, $"Canopy_{i + 1:00}", StripeTone(i));
                panel.position = (apex + edge) * 0.5f;
                panel.rotation = Quaternion.LookRotation(axis.normalized);
                // Клин уже шага сегмента: у вершины все 32 сходятся в точку,
                // и на полной ширине они уходили бы друг в друга насквозь.
                panel.localScale = new Vector3(width * CanopyOverlap, CanopyThickness, axis.magnitude);
            }
        }

        /// <summary>
        /// Ферма под куполом: к ней крепятся цепи клеток. Коллайдеры сняты —
        /// под ней только летящие вниз.
        ///
        /// Дресс 4.1 делает из бруса решётчатую ферму: два пояса и раскос
        /// между ними на каждый сегмент. Модели у неё нет — плоские
        /// `SM_Bld_House_Truss_01/02` пака это стропила двускатной крыши,
        /// а кольцевой фермы в паках нет вовсе.
        /// </summary>
        private static void BuildRigging(Transform root, CircusArenaConfig config)
        {
            Transform rigging = ResetGroup(root, "Rigging");
            float ringRadius = config.CageRingRadius;
            float width = 2f * Mathf.PI * ringRadius / RiggingSegments * SegmentOverlap;

            for (int i = 0; i < RiggingSegments; i++)
            {
                Vector3 dir = DirectionAt(i, RiggingSegments);
                Transform segment = MakeBox(rigging, $"Rigging_{i + 1:00}", 0);
                StripCollider(segment);
                segment.position = dir * ringRadius + Vector3.up * config.RiggingHeight;
                segment.rotation = Quaternion.LookRotation(dir);
                segment.localScale = new Vector3(width, 0.3f, 0.4f);
                Paint(segment, CircusPalette.Tone.CageMetal);

                Transform lower = MakeDecor(rigging, $"RiggingLower_{i + 1:00}", CircusPalette.Tone.CageMetal);
                lower.position = dir * ringRadius + Vector3.up * (config.RiggingHeight - TrussSpread * 2f);
                lower.rotation = Quaternion.LookRotation(dir);
                lower.localScale = new Vector3(width, TrussBarThickness, TrussBarThickness);

                // Раскос через сегмент, а не на каждом: на каждом решётка
                // забивается в сплошную полосу и ферма снова читается брусом.
                if (i % 2 != 0)
                {
                    continue;
                }

                Transform brace = MakeDecor(rigging, $"RiggingBrace_{i + 1:00}", CircusPalette.Tone.CageMetal);
                brace.position = dir * ringRadius + Vector3.up * (config.RiggingHeight - TrussSpread);
                brace.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, 34f);
                brace.localScale = new Vector3(TrussBarThickness, TrussSpread * 3.2f, TrussBarThickness);
            }
        }

        /// <summary>
        /// Четырёхгранное табло над центром ямы: одна плоскость не читается
        /// с противоположной стороны кольца. Наполнение граней — WorldScoreboard,
        /// здесь только геометрия и точки крепления.
        /// </summary>
        private static void BuildScoreboard(Transform root, CircusArenaConfig config)
        {
            Transform board = ResetGroup(root, "Scoreboard");
            board.position = new Vector3(0f, config.ScoreboardHeight, 0f);

            TMP_FontAsset font = FindFont();
            var screens = new List<WorldScoreboardFace>(4);
            float half = config.ScoreboardFaceWidth * 0.5f;

            for (int i = 0; i < 4; i++)
            {
                Vector3 dir = DirectionAt(i, 4);

                Transform face = MakeBox(board, $"Face_{i + 1}", 0);
                StripCollider(face);
                face.localPosition = dir * half;
                face.localRotation = Quaternion.LookRotation(dir);
                face.localScale = new Vector3(config.ScoreboardFaceWidth, config.ScoreboardFaceHeight, BoardPanelDepth);
                Paint(face, CircusPalette.Tone.BoardPanel);
                BuildBoardFrame(board, config, dir, i);

                // Канвас — не ребёнок панели: у панели неравномерный масштаб,
                // и текст на ней растянуло бы вместе с ней.
                screens.Add(BuildScoreboardScreen(board, config, font, dir, i));
            }

            var scoreboard = board.GetComponent<WorldScoreboard>();
            if (scoreboard == null)
            {
                scoreboard = board.gameObject.AddComponent<WorldScoreboard>();
            }

            scoreboard.SetFaces(screens);
            scoreboard.Clear();
        }

        /// <summary>
        /// Рама грани табло с гирляндой лампочек по верху и низу — ретро-щит,
        /// как на референсах.
        ///
        /// <b>Зачем это, а не просто покрасить.</b> Разведка 4.0 нашла, что
        /// табло стоит в центре кольца ровно на высоте клеток и шириной 5.76 м
        /// перекрывает противоположную пару. Ширина и высота грани — числа
        /// спеки (8 × 3 ШП), трогать их нельзя, а высота табло тоже из спеки.
        /// Значит лечим видом: глубина панели ужата с 0.30 до 0.10 м, панель
        /// стала почти чёрной, а по контуру пошла светящаяся рама. Силуэт
        /// читается рамкой, а не плитой, и глаз перестаёт принимать его за
        /// стену. Остаточное перекрытие двух клеток из восьми — геометрия,
        /// а не арт; решение геймдизайнера 04.09 — оставить так.
        /// </summary>
        private static void BuildBoardFrame(Transform board, CircusArenaConfig config, Vector3 dir, int index)
        {
            float halfW = config.ScoreboardFaceWidth * 0.5f;
            float halfH = config.ScoreboardFaceHeight * 0.5f;
            float depth = BoardPanelDepth + BoardFrameThickness;
            Quaternion facing = Quaternion.LookRotation(dir);
            Vector3 origin = dir * (config.ScoreboardFaceWidth * 0.5f);

            for (int side = 0; side < 4; side++)
            {
                bool horizontal = side < 2;
                float sign = side % 2 == 0 ? 1f : -1f;
                Transform bar = MakeDecor(board, $"BoardFrame_{index + 1}_{side + 1}", CircusPalette.Tone.BoardFrame);
                bar.localRotation = facing;
                bar.localScale = horizontal
                    ? new Vector3(config.ScoreboardFaceWidth + BoardFrameThickness, BoardFrameThickness, depth)
                    : new Vector3(BoardFrameThickness, config.ScoreboardFaceHeight + BoardFrameThickness, depth);
                Vector3 offset = horizontal
                    ? new Vector3(0f, sign * (halfH + BoardFrameThickness * 0.5f), 0f)
                    : new Vector3(sign * (halfW + BoardFrameThickness * 0.5f), 0f, 0f);
                bar.localPosition = origin + facing * offset;
            }

            // Лампочки — двумя нитками по верху и низу. Гирлянда пака 2.49 м,
            // грань 5.76 м: две нитки внахлёст закрывают её целиком.
            for (int row = 0; row < 2; row++)
            {
                float y = (row == 0 ? 1f : -1f) * (halfH + BoardFrameThickness * 0.5f);
                for (int part = 0; part < 2; part++)
                {
                    float x = (part == 0 ? -1f : 1f) * config.ScoreboardFaceWidth * 0.25f;
                    // Наружу, а не внутрь: локальный +Z рамы смотрит от центра
                    // арены, и минус увёл бы гирлянду за панель — с кольца
                    // клеток её не было бы видно вовсе.
                    Vector3 at = board.position + origin + facing * new Vector3(x, y, depth * 0.5f);
                    GameObject bulbs = CircusDress.Prop(board, $"BoardBulbs_{index + 1}_{row + 1}_{part + 1}",
                        CircusDress.BulbStringPath, at, 0f, 0f, false);
                    if (bulbs != null)
                    {
                        bulbs.transform.rotation = facing;
                    }
                }
            }
        }

        /// <summary>
        /// Экран одной грани: world-space канвас с заголовком, строкой задания
        /// и строками игроков в два столбца.
        ///
        /// Столбца два, а не один, из-за высоты грани: 8 строк в 2.16 м дают
        /// по 0.19 м на строку, и с дальней клетки (12 м) это уже нечитаемо.
        /// Два столбца по четыре строки поднимают строку до 0.30 м.
        /// </summary>
        private static WorldScoreboardFace BuildScoreboardScreen(Transform parent, CircusArenaConfig config,
            TMP_FontAsset font, Vector3 dir, int index)
        {
            float width = config.ScoreboardFaceWidth * BoardUnitsPerMeter;
            float height = config.ScoreboardFaceHeight * BoardUnitsPerMeter;

            var go = new GameObject($"Screen_{index + 1}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.localPosition = dir * (config.ScoreboardFaceWidth * 0.5f + ScoreboardDepth * 0.5f + 0.01f);
            // Канвас читается со стороны своего -Z, поэтому наружу смотрит
            // LookRotation(-dir), а не (dir): с (dir) текст выходит зеркальным.
            rect.localRotation = Quaternion.LookRotation(-dir);
            rect.localScale = Vector3.one * BoardCanvasScale;
            rect.sizeDelta = new Vector2(width, height);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var face = go.AddComponent<WorldScoreboardFace>();

            TMP_Text title = MakeBoardText(rect, "Title", font, BoardTitleFont, TextAlignmentOptions.Center,
                new Vector2(0f, height * 0.5f - BoardTitleBand * 0.5f), new Vector2(width - BoardPadding * 2f, BoardTitleBand));
            TMP_Text subtitle = MakeBoardText(rect, "Subtitle", font, BoardSubtitleFont, TextAlignmentOptions.Center,
                new Vector2(0f, height * 0.5f - BoardTitleBand - BoardSubtitleBand * 0.5f), new Vector2(width - BoardPadding * 2f, BoardSubtitleBand));

            int rowsPerColumn = Mathf.CeilToInt(BoardRowCapacity / (float)BoardColumns);
            float columnWidth = width / BoardColumns;
            float rowsTop = height * 0.5f - BoardTitleBand - BoardSubtitleBand;

            var labels = new TMP_Text[BoardRowCapacity];
            var values = new TMP_Text[BoardRowCapacity];
            for (int i = 0; i < BoardRowCapacity; i++)
            {
                int column = i / rowsPerColumn;
                int row = i % rowsPerColumn;
                float columnCentre = -width * 0.5f + columnWidth * (column + 0.5f);
                float y = rowsTop - BoardRowHeight * (row + 0.5f);

                labels[i] = MakeBoardText(rect, $"Row_{i + 1}_Label", font, BoardRowFont, TextAlignmentOptions.MidlineLeft,
                    new Vector2(columnCentre - columnWidth * 0.16f, y), new Vector2(columnWidth * 0.6f, BoardRowHeight));
                values[i] = MakeBoardText(rect, $"Row_{i + 1}_Value", font, BoardRowFont, TextAlignmentOptions.MidlineRight,
                    new Vector2(columnCentre + columnWidth * 0.32f, y), new Vector2(columnWidth * 0.3f, BoardRowHeight));
            }

            var serialized = new SerializedObject(face);
            serialized.FindProperty("title").objectReferenceValue = title;
            serialized.FindProperty("subtitle").objectReferenceValue = subtitle;
            SerializedProperty labelsProperty = serialized.FindProperty("rowLabels");
            SerializedProperty valuesProperty = serialized.FindProperty("rowValues");
            labelsProperty.arraySize = BoardRowCapacity;
            valuesProperty.arraySize = BoardRowCapacity;
            for (int i = 0; i < BoardRowCapacity; i++)
            {
                labelsProperty.GetArrayElementAtIndex(i).objectReferenceValue = labels[i];
                valuesProperty.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return face;
        }

        private static TMP_Text MakeBoardText(RectTransform parent, string name, TMP_FontAsset font,
            float fontSize, TextAlignmentOptions alignment, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }

            // Автокегль вниз от заданного: длина заголовка зависит от игры
            // («ПОДРАУНД 4 — НЕ МЕНЬШЕ» против «ИТОГИ»), и без него длинный
            // вариант вылезает за панель. Порядок важен: присваивание fontSize
            // после включения автокегля сбрасывает режим, и текст снова растёт.
            text.fontSizeMin = fontSize * 0.45f;
            text.fontSizeMax = fontSize;
            text.enableAutoSizing = true;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        private static TMP_FontAsset FindFont()
        {
            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            TextMeshProUGUI existing = Object.FindAnyObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
            return existing != null ? existing.font : null;
        }

        private static void BuildCages(Transform root, CircusArenaConfig config, int groundLayer)
        {
            Transform cages = ResetGroup(root, "Cages");
            Transform chains = ResetGroup(root, "Chains");
            for (int i = 0; i < config.CageAnchorCount; i++)
            {
                BuildCage(cages, config, groundLayer, i);
                BuildChain(chains, config, i);

                // Цепь живёт отдельно от клетки, но обязана до неё доставать
                // на любой ступени. Привязываем к крыше — по ней CageChain
                // и вытравливает звенья, когда клетка едет вниз.
                Transform cage = cages.Find($"Cage_{i + 1:00}");
                Transform links = chains.Find($"ChainLinks_{i + 1:00}");
                if (cage != null && links != null)
                {
                    CircusDress.BindChain(links.gameObject, cage.Find("Roof"));
                }
            }
        }

        /// <summary>
        /// Зверь в яме. Вид живёт под отдельным объектом Visual, и в этом весь
        /// смысл: логика в PitBear про внешность ничего не знает и правок при
        /// смене модели не требует.
        ///
        /// Видов три, по убыванию качества — анимированный зверь, статуя,
        /// коробки, — и берётся первый, который собрался.
        ///
        /// 🔴 <b>Менять вид зверя полной пересборкой арены не нужно — и не
        /// стоит.</b> Для этого есть «Igruha/Цирк/Переодеть зверя в яме»
        /// (<see cref="CircusBeast.RedressInScene"/>): он трогает только
        /// содержимое <c>Visual</c> и ничьих ссылок не рвёт.
        ///
        /// А <b>после полной пересборки арены обязательно прогнать</b>
        /// «Igruha/Minigames/Rebuild Stopwatch Props» и «Rebuild Cans Order
        /// Props» — либо «Igruha/Цирк/Перевязать интерфейс». Пересборка
        /// пересоздаёт клетки и табло с новыми <c>fileID</c>, и ссылки
        /// контроллеров на них обнуляются молча: ни одной ошибки в консоли,
        /// просто в игре пропадает весь интерфейс. Обе игры шатра простояли
        /// так с 04.09 — разбор в STATE.md, раздел 3.78.
        ///
        /// Размеры взяты «на глаз от персонажа»: зверь заметно крупнее
        /// капсулы 0.72 м, иначе в яме радиусом 8.64 он теряется.
        /// </summary>
        private static void BuildBear(Transform root, CircusArenaConfig config)
        {
            Transform bearRoot = ResetGroup(root, "PitBear");
            bearRoot.position = new Vector3(0f, 0f, config.PitRadius * 0.55f);

            var visualGo = new GameObject("Visual");
            Transform visual = visualGo.transform;
            visual.SetParent(bearRoot, false);

            // Три вида зверя по убыванию качества, и берётся первый доступный.
            // Так сделано затем, чтобы яма не осталась пустой ни на одной
            // машине: «медведь не заспавнился» искали бы в сетевом коде.
            //
            // 1. Анимированный зверь (CircusBeast) — humanoid Synty на клипах
            //    игрока. Он единственный из трёх умеет ходить: у статуи и
            //    коробок костей нет, и по яме они ездят, не переставляя лап.
            Animator beast = CircusBeast.Build(visual, CircusDress.BearHeight);
            if (beast != null)
            {
                FinishBear(bearRoot, visual, true, beast);
                return;
            }

            // 2. Статуя SM_Prop_Bear_Statue_01. Вопреки имени это не изваяние,
            //    а медведь на задних лапах с открытой пастью — поза, которую
            //    спека 3.5 просит на нижней ступени. Выбрана геймдизайнером
            //    04.09 из трёх вариантов брифа 14.4 и остаётся запасным видом.
            GameObject model = CircusDress.Prop(visual, "BearModel", CircusDress.BearPath,
                bearRoot.position, 180f, CircusDress.BearHeight);
            if (model != null)
            {
                // Перекрашиваем: у пака это витринная фигура, и родной материал
                // у неё серо-каменный. В яме такой медведь читается валуном,
                // а бриф просит бурого и мультяшного (спека 8.4 — «отмахивается
                // лапой как кот от игрушки»).
                DressKit.Repaint(model, CircusPalette.Get(CircusPalette.Tone.BearFur));
                FinishBear(bearRoot, visual, true, null);
                return;
            }

            // 3. Коробки блокаута.

            const float bodyLength = 1.9f;
            const float bodyWidth = 1.0f;
            const float bodyHeight = 1.0f;
            const float legHeight = 0.6f;

            Transform body = MakeBox(visual, "Body", 0);
            StripCollider(body);
            body.localPosition = new Vector3(0f, legHeight + bodyHeight * 0.5f, 0f);
            body.localScale = new Vector3(bodyWidth, bodyHeight, bodyLength);

            Transform head = MakeBox(visual, "Head", 0);
            StripCollider(head);
            head.localPosition = new Vector3(0f, legHeight + bodyHeight * 0.9f, bodyLength * 0.55f);
            head.localScale = new Vector3(0.62f, 0.58f, 0.62f);

            Transform snout = MakeBox(visual, "Snout", 0);
            StripCollider(snout);
            snout.localPosition = new Vector3(0f, legHeight + bodyHeight * 0.8f, bodyLength * 0.55f + 0.4f);
            snout.localScale = new Vector3(0.3f, 0.26f, 0.3f);

            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * bodyWidth * 0.35f;
                float z = (i < 2 ? 1f : -1f) * bodyLength * 0.32f;
                Transform leg = MakeBox(visual, $"Leg_{i + 1}", 0);
                StripCollider(leg);
                leg.localPosition = new Vector3(x, legHeight * 0.5f, z);
                leg.localScale = new Vector3(0.28f, legHeight, 0.28f);
            }

            FinishBear(bearRoot, visual, false, null);
        }

        /// <summary>Габарит капсулы медведя-заготовки: тело 1.9 длиной, 1.0 шириной, лапы 0.6.</summary>
        private const float BoxBearLength = 1.9f;
        private const float BoxBearWidth = 1.0f;
        private const float BoxBearCentre = 1.1f;

        /// <summary>Рост медведя-модели по вертикали и его толщина в плане, м.</summary>
        private const float ModelBearRadius = 0.55f;

        /// <summary>
        /// Коллайдер и сетевые компоненты медведя. Отдельно от вида, потому
        /// что вид у него теперь два: модель пака и запасная сборка из коробок.
        ///
        /// 🔴 <b>Коллайдер меняется вместе с видом — и это объявлено.</b>
        /// Заготовка была медведем на четырёх лапах, и капсула лежала вдоль Z:
        /// <c>direction 2, height 2.50, radius 0.50, center (0, 1.10, 0)</c>,
        /// то есть габарит X ±0.50, Y 0.60…1.60, Z ±1.25. Модель — медведь
        /// <b>на задних лапах</b> 2.40 м ростом и 1.12 м в плане: под лежачую
        /// капсулу он не попадает ни одной осью. Игрок упирался бы в пустоту
        /// на 1.25 м впереди медведя и проходил бы сквозь его голову.
        ///
        /// Стоячая капсула: <c>direction 1, height 2.40, radius 0.55,
        /// center (0, 1.20, 0)</c> — габарит X и Z ±0.55, Y 0…2.40. Она
        /// повторяет то, что видно.
        ///
        /// <b>Урон это не трогает.</b> Радиус удара — 1.5 м из
        /// <c>CircusBearConfig</c>, он считается от позиции медведя и от формы
        /// капсулы не зависит. Меняется только объём, которым медведь пихается.
        /// </summary>
        /// <param name="animator">
        /// Аниматор вида, если вид умеет анимироваться. Null у статуи и коробок:
        /// <c>PitBear</c> проверяет поле на null сам и молча работает без
        /// анимаций — логика от вида не зависит.
        /// </param>
        private static void FinishBear(Transform bearRoot, Transform visual, bool upright, Animator animator)
        {
            var collider = bearRoot.gameObject.AddComponent<CapsuleCollider>();
            if (upright)
            {
                collider.direction = 1;
                collider.height = CircusDress.BearHeight;
                collider.radius = ModelBearRadius;
                collider.center = new Vector3(0f, CircusDress.BearHeight * 0.5f, 0f);
            }
            else
            {
                collider.direction = 2;
                collider.height = BoxBearLength + 0.6f;
                collider.radius = BoxBearWidth * 0.5f;
                collider.center = new Vector3(0f, BoxBearCentre, 0f);
            }

            var bear = bearRoot.gameObject.AddComponent<PitBear>();
            var serialized = new SerializedObject(bear);
            serialized.FindProperty("visualRoot").objectReferenceValue = visual;
            serialized.FindProperty("animator").objectReferenceValue = animator;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Медведь — единственный на арене, кем не владеет ни один игрок:
            // его ведёт сервер. NetworkTransform здесь обычный, серверный,
            // а не ClientNetworkTransform — владельца-клиента у него нет.
            // Объект сценовый, поэтому NGO заспавнит его сам при загрузке
            // сцены, и регистрировать префаб не требуется.
            bearRoot.gameObject.AddComponent<Unity.Netcode.NetworkObject>();
            bearRoot.gameObject.AddComponent<Unity.Netcode.Components.NetworkTransform>();
        }

        /// <summary>
        /// Одна клетка. Строится вокруг якоря, у которого ноль — дно клетки:
        /// уровень клетки в игре меняется движением якоря, а не пересчётом
        /// внутренностей.
        ///
        /// Пол сразу собран двумя створками с петлями по внешним краям — 8.7
        /// остаётся только повернуть их вокруг оси, а не переделывать пол.
        /// </summary>
        private static void BuildCage(Transform parent, CircusArenaConfig config, int groundLayer, int index)
        {
            var anchorGo = new GameObject($"Cage_{index + 1:00}");
            Transform anchor = anchorGo.transform;
            anchor.SetParent(parent, false);
            anchor.position = config.GetAnchorPosition(index);
            // Локальный +Z смотрит в центр арены: так «внутрь» и «наружу» у всех клеток одинаковы.
            anchor.rotation = Quaternion.LookRotation(-new Vector3(anchor.position.x, 0f, anchor.position.z).normalized);

            float half = config.CageInnerSize * 0.5f;
            float outer = half + BarThickness;
            float height = config.CageInnerHeight;

            BuildCageFloor(anchor, config, groundLayer, half, outer);

            Transform roof = MakeBox(anchor, "Roof", groundLayer);
            roof.localPosition = new Vector3(0f, height + CageFloorThickness * 0.5f, 0f);
            roof.localScale = new Vector3(outer * 2f, CageFloorThickness, outer * 2f);
            Paint(roof, CircusPalette.Tone.CageWood);

            for (int side = 0; side < 4; side++)
            {
                BuildCageWall(anchor, groundLayer, side, half, height);
            }

            BuildCageFrame(anchor, half, outer, height);

            // Слот реквизита: сюда «Секундомер» ставит кнопку, «Порядок банок» — полку с банками.
            // Сама клетка про содержимое слота ничего не знает.
            var slot = new GameObject("PropSlot");
            slot.transform.SetParent(anchor, false);
            slot.transform.localPosition = Vector3.zero;

            // Точка респавна — ребёнок клетки, поэтому едет вниз вместе с ней
            // сама собой. Отдельная от точки спавна в _Spawns: ту собирает
            // SpawnPointSet, и она обязана лежать под его объектом.
            var respawn = new GameObject("RespawnPoint");
            respawn.transform.SetParent(anchor, false);
            respawn.transform.localPosition = new Vector3(0f, 0.05f, -config.CageInnerSize * 0.25f);
            respawn.transform.localRotation = Quaternion.identity;

            var rigidbody = anchorGo.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;

            anchorGo.AddComponent<RidePlatform>();
            // Пол на створках — общий компонент Core: та же механика служит
            // платформам «Экзамена». Клетка про его устройство не знает,
            // она только говорит «открой» и «закрой».
            var hatch = anchorGo.AddComponent<HingedFloorHatch>();
            var hatchSerialized = new SerializedObject(hatch);
            hatchSerialized.FindProperty("doorLeft").objectReferenceValue = anchor.Find("Floor/DoorLeft");
            hatchSerialized.FindProperty("doorRight").objectReferenceValue = anchor.Find("Floor/DoorRight");
            hatchSerialized.ApplyModifiedPropertiesWithoutUndo();

            var station = anchorGo.AddComponent<CageStation>();
            var serialized = new SerializedObject(station);
            serialized.FindProperty("hatch").objectReferenceValue = hatch;
            serialized.FindProperty("propSlot").objectReferenceValue = slot.transform;
            serialized.FindProperty("respawnPoint").objectReferenceValue = respawn.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Каркас клетки поверх блокаута: четыре угловые стойки и два
        /// поперечных пояса на каждой стене с прутьями.
        ///
        /// Коллайдеров не несёт вовсе. Стену держит невидимая коробка
        /// <c>Collider</c> внутри <see cref="BuildCageWall"/>, и договор фазы 4
        /// в том, что арт её не трогает: стойка снаружи прутьев физически
        /// ничего не меняет, а силуэт клетки собирает именно она. Без стоек
        /// и поясов пять вертикальных прутьев читаются частоколом, а не
        /// клеткой, — и это заметно ровно с той дистанции, с которой игрок
        /// сравнивает свою клетку с соседской.
        /// </summary>
        private static void BuildCageFrame(Transform anchor, float half, float outer, float height)
        {
            Transform frame = ResetGroup(anchor, "Frame");

            for (int corner = 0; corner < 4; corner++)
            {
                float x = (corner % 2 == 0 ? -1f : 1f) * outer;
                float z = (corner < 2 ? -1f : 1f) * outer;
                Transform post = MakeDecor(frame, $"Post_{corner + 1}", CircusPalette.Tone.CageMetal);
                post.localPosition = new Vector3(x, height * 0.5f, z);
                post.localScale = new Vector3(CagePostThickness, height, CagePostThickness);
            }

            for (int side = 0; side < 4; side++)
            {
                // Пояса ставим только там, где есть прутья: на внешней стене
                // их нет намеренно (см. BuildCageWall), и пояс поперёк неё
                // оказался бы ровно между камерой и игроком.
                if (side == OuterWallSide)
                {
                    continue;
                }

                Vector3 dir = DirectionAt(side, 4);
                for (int rail = 0; rail < 2; rail++)
                {
                    // Верхний пояс под самой крышей, нижний на трети высоты:
                    // так пояс не приходится на уровень глаз и не режет обзор
                    // на яму и соседние клетки.
                    float y = rail == 0 ? height - CageRailThickness : height * 0.34f;
                    Transform bar = MakeDecor(frame, $"Rail_{side + 1}_{rail + 1}", CircusPalette.Tone.CageMetal);
                    bar.localPosition = dir * (half + BarThickness * 0.5f) + Vector3.up * y;
                    bar.localRotation = Quaternion.LookRotation(dir);
                    bar.localScale = new Vector3(outer * 2f, CageRailThickness, CageRailThickness);
                }
            }
        }

        /// <summary>
        /// Цепь от фермы к крыше клетки. Живёт отдельно от клетки: если бы цепь
        /// была её дочерним объектом, при спуске она уезжала бы вниз вместе
        /// с клеткой вместо того, чтобы вытравливаться.
        ///
        /// Дресс 4.1 заменяет цилиндр звеньями пака и вешает их <b>от фермы
        /// вниз</b>. Пивот у `SM_Gen_Prop_Chain_01` наверху
        /// (<c>pivotOffset.y = −1.10</c>), и посадка снизу увела бы всю цепь
        /// под клетку — на макете 4.0 так все восемь свесились в яму.
        /// </summary>
        private static void BuildChain(Transform parent, CircusArenaConfig config, int index)
        {
            Vector3 anchor = config.GetAnchorPosition(index);
            float bottom = anchor.y + config.CageInnerHeight + CageFloorThickness;
            float length = Mathf.Max(0.1f, config.RiggingHeight - bottom);
            var top = new Vector3(anchor.x, config.RiggingHeight, anchor.z);

            Transform chain = ResetPrimitive(parent, $"Chain_{index + 1:00}", PrimitiveType.Cylinder);
            StripCollider(chain);
            chain.position = new Vector3(anchor.x, bottom + length * 0.5f, anchor.z);
            chain.localScale = new Vector3(ChainThickness, length * 0.5f, ChainThickness);
            Paint(chain, CircusPalette.Tone.CageMetal);

            // Цилиндр блокаута гасим: настоящая цепь его заменяет целиком.
            // Оставить оба значило бы вести сквозь звенья серый стержень,
            // и он был бы виден ровно на просвете между клеткой и ямой —
            // там, где смотреть обязаны.
            var cylinderRenderer = chain.GetComponent<MeshRenderer>();

            // Заготавливаем на нижнюю ступень: клетка едет вниз за каждую
            // ошибку, и цепь обязана дотянуться до неё в самом низу.
            float maxLength = config.RiggingHeight
                              - (config.GetCageBottomHeight(0) + config.CageInnerHeight + CageFloorThickness);
            GameObject links = CircusDress.Chain(parent, $"ChainLinks_{index + 1:00}", top, maxLength);
            if (links == null)
            {
                // Пака нет — цилиндр остаётся единственным подвесом.
                return;
            }

            if (cylinderRenderer != null)
            {
                cylinderRenderer.enabled = false;
            }

            CircusDress.Prop(links.transform, "Anchor", CircusDress.ChainAnchorPath,
                top, 0f, 0f, false, true);
        }

        /// <summary>
        /// Решётчатый пол из двух створок. Решётка — не украшение: сквозь неё
        /// видно яму, и по тому, насколько медведь стал ближе, игрок читает
        /// своё положение без интерфейса.
        /// </summary>
        private static void BuildCageFloor(Transform anchor, CircusArenaConfig config, int groundLayer, float half, float outer)
        {
            Transform floor = ResetGroup(anchor, "Floor");
            floor.localPosition = Vector3.zero;

            for (int door = 0; door < 2; door++)
            {
                float sign = door == 0 ? -1f : 1f;
                var hingeGo = new GameObject(door == 0 ? "DoorLeft" : "DoorRight");
                Transform hinge = hingeGo.transform;
                hinge.SetParent(floor, false);
                // Петля по внешнему краю створки: створки распахиваются вниз наружу.
                hinge.localPosition = new Vector3(sign * outer, 0f, 0f);

                Transform blocker = MakeBox(hinge, "Collider", groundLayer);
                StripRenderer(blocker);
                blocker.localPosition = new Vector3(-sign * outer * 0.5f, -CageFloorThickness * 0.5f, 0f);
                blocker.localScale = new Vector3(outer, CageFloorThickness, outer * 2f);

                for (int slat = 0; slat < SlatsPerCageDoor; slat++)
                {
                    float t = (slat + 0.5f) / SlatsPerCageDoor;
                    Transform bar = MakeBox(hinge, $"Slat_{slat + 1}", groundLayer);
                    StripCollider(bar);
                    bar.localPosition = new Vector3(-sign * outer * t, -CageFloorThickness * 0.5f, 0f);
                    bar.localScale = new Vector3(BarThickness * 2f, CageFloorThickness, outer * 2f);
                    Paint(bar, CircusPalette.Tone.CageWood);
                }
            }
        }

        /// <summary>
        /// Стена из прутьев: сквозь них видно табло и соседние клетки, иначе
        /// игрок сидит в глухой коробке и половина игры пропадает. Коллайдер
        /// при этом сплошной — между прутьями не пролезают.
        /// </summary>
        private static void BuildCageWall(Transform anchor, int groundLayer, int side, float half, float height)
        {
            Vector3 dir = DirectionAt(side, 4);
            var wallGo = new GameObject($"Wall_{side + 1}");
            Transform wall = wallGo.transform;
            wall.SetParent(anchor, false);
            wall.localPosition = dir * (half + BarThickness * 0.5f) + Vector3.up * (height * 0.5f);
            wall.localRotation = Quaternion.LookRotation(dir);

            Transform blocker = MakeBox(wall, "Collider", groundLayer);
            StripRenderer(blocker);
            blocker.localPosition = Vector3.zero;
            blocker.localScale = new Vector3(half * 2f + BarThickness * 2f, height, BarThickness);

            if (side == OuterWallSide)
            {
                // Внешняя стена уходит с пути лучей камеры: физика её держит
                // по-прежнему (слой на столкновения не влияет), а вот
                // деоклюдер Cinemachine видел в ней препятствие и вжимал
                // камеру внутрь клетки — кадр упирался игроку в ноги.
                blocker.gameObject.layer = IgnoreRaycastLayer;
            }

            // Прутьев нет только на внешней стене — той, что за спиной игрока.
            // Они оказывались между камерой и персонажем, и один вставал ровно
            // посреди экрана. Внутреннюю решётку оставляем: сквозь неё игрок
            // смотрит на арену, и клетка обязана выглядеть клеткой. Читать
            // результаты сквозь прутья не нужно — они дублируются на экране.
            if (side == OuterWallSide)
            {
                return;
            }

            for (int bar = 0; bar < BarsPerCageSide; bar++)
            {
                float t = (bar + 0.5f) / BarsPerCageSide;

                // Круглый прут, а не брусок: игрок стоит к нему вплотную весь
                // подраунд, и на этой дистанции квадратное сечение читается
                // рейкой опалубки, а не клеткой. Цилиндр Unity высотой 2 ед.,
                // поэтому масштаб по Y — половина высоты.
                Transform pole = ResetPrimitive(wall, $"Bar_{bar + 1}", PrimitiveType.Cylinder);
                StripCollider(pole);
                pole.localPosition = new Vector3(Mathf.Lerp(-half, half, t), 0f, 0f);
                pole.localScale = new Vector3(BarThickness, height * 0.5f, BarThickness);
                Paint(pole, CircusPalette.Tone.CageMetal);
            }
        }

        /// <summary>
        /// Точка спавна в каждой клетке. Смещена к внешней стенке и развёрнута
        /// внутрь: игрок появляется лицом к кнопке, а за ней — табло и яма.
        /// Спавн не в центре, потому что в центре стоит реквизит.
        /// </summary>
        private static void BuildSpawns(Transform spawnsRoot, CircusArenaConfig config)
        {
            for (int i = spawnsRoot.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(spawnsRoot.GetChild(i).gameObject);
            }

            float back = config.CageInnerSize * 0.25f;
            for (int i = 0; i < config.CageAnchorCount; i++)
            {
                Vector3 anchor = config.GetAnchorPosition(i);
                Vector3 inward = -new Vector3(anchor.x, 0f, anchor.z).normalized;

                var go = new GameObject($"Spawn_{i + 1:00}");
                go.transform.SetParent(spawnsRoot, false);
                go.transform.position = anchor - inward * back + Vector3.up * 0.05f;
                go.transform.rotation = Quaternion.LookRotation(inward);
                go.AddComponent<SpawnPoint>();
            }

            if (spawnsRoot.GetComponent<SpawnPointSet>() == null)
            {
                spawnsRoot.gameObject.AddComponent<SpawnPointSet>();
            }
        }

        /// <summary>Страховка от провала сквозь геометрию: ниже дна ямы ловим и возвращаем на спавн.</summary>
        private static void BuildBounds(CircusArenaConfig config)
        {
            GameObject bounds = GameObject.Find("_Bounds");
            if (bounds == null)
            {
                Debug.LogWarning("CircusArenaBuilder: в сцене нет _Bounds — провалившийся сквозь геометрию будет падать вечно.");
                return;
            }

            var zones = bounds.GetComponentsInChildren<KillZone>(true);
            for (int i = 0; i < zones.Length; i++)
            {
                Transform t = zones[i].transform;
                t.position = new Vector3(0f, -8f, 0f);
                t.localScale = new Vector3(config.TentRadius * 2.4f, 1f, config.TentRadius * 2.4f);
            }
        }

        /// <summary>
        /// Проверки геометрии. Каждая ловит поломку, которую глазом в сцене
        /// не видно, а в игре она стоит целой механики.
        /// </summary>
        private static void Validate(CircusArenaConfig config)
        {
            // Проверяем по достижимой позиции игрока, а не по габариту клетки.
            // Пустые углы квадратной клетки торчат за круглую яму на 0.12 м,
            // но встать в них капсула радиуса 0.36 не может, а падает именно она.
            float playerReach = config.GetPlayerReachFromCentre();
            if (playerReach > config.PitRadius)
            {
                Debug.LogWarning($"CircusArenaBuilder: игрок в клетке достаёт до {playerReach:F2} м от центра " +
                                 $"при радиусе ямы {config.PitRadius:F2} м. Выпавший приземлится на борт или " +
                                 "на пол шатра мимо медведя. Увеличь радиус ямы или сожми кольцо клеток.");
            }

            float gap = config.GetCageGap();
            if (gap < config.MinCageGap)
            {
                Debug.LogWarning($"CircusArenaBuilder: зазор между соседними клетками {gap:F2} м при минимуме " +
                                 $"{config.MinCageGap:F2} м. Кольцо читается сплошной стеной, и пустая клетка " +
                                 "перестаёт означать «отсюда выпали».");
            }

            float rimInside = config.PitRimTop;
            if (rimInside <= config.MeasuredJumpHeight)
            {
                Debug.LogWarning($"CircusArenaBuilder: борт ямы изнутри {rimInside:F2} м при прыжке " +
                                 $"{config.MeasuredJumpHeight:F2} м — выпавший выпрыгнет из ямы и убежит от медведя.");
            }

            if (config.CageInnerHeight <= config.MeasuredJumpHeight)
            {
                Debug.LogWarning($"CircusArenaBuilder: высота клетки {config.CageInnerHeight:F2} м при прыжке " +
                                 $"{config.MeasuredJumpHeight:F2} м — из клетки выпрыгнут.");
            }

            float cageTop = config.GetCageBottomHeight(config.MaxLevelSteps) + config.CageInnerHeight;
            if (cageTop >= config.RiggingHeight)
            {
                Debug.LogWarning($"CircusArenaBuilder: верх клетки {cageTop:F2} м упирается в ферму на " +
                                 $"{config.RiggingHeight:F2} м — цепи не помещаются.");
            }

            float boardReach = config.ScoreboardFaceWidth * 0.5f * Mathf.Sqrt(2f);
            float cageInnerReach = config.CageRingRadius - config.CageInnerSize * 0.5f;
            if (boardReach >= cageInnerReach)
            {
                Debug.LogWarning($"CircusArenaBuilder: угол табло на {boardReach:F2} м от центра задевает клетки, " +
                                 $"которые начинаются с {cageInnerReach:F2} м.");
            }

            if (config.PitRadius + RimThickness >= config.TentRadius)
            {
                Debug.LogWarning("CircusArenaBuilder: яма с бортом не помещается в шатёр — пола шатра не осталось.");
            }
        }

        private static Vector3 DirectionAt(int index, int count)
        {
            return Quaternion.Euler(0f, 360f / count * index, 0f) * Vector3.forward;
        }

        private static Transform MakeBox(Transform parent, string name, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void StripCollider(Transform target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }
        }

        private static void StripRenderer(Transform target)
        {
            var renderer = target.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Object.DestroyImmediate(renderer);
            }

            var filter = target.GetComponent<MeshFilter>();
            if (filter != null)
            {
                Object.DestroyImmediate(filter);
            }
        }

        private static Transform ResetPrimitive(Transform parent, string name, PrimitiveType type)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Transform ResetGroup(Transform parent, string name)
        {
            Transform group = parent.Find(name);
            if (group == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                return go.transform;
            }

            for (int i = group.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(group.GetChild(i).gameObject);
            }

            // Компоненты на самом узле снимаем тоже. Раньше чистились только
            // дети, а компоненты билдер добавлял заново — и каждая пересборка
            // клала ещё один слой. На медведе так набралось по три PitBear
            // и три CapsuleCollider: три коллайдера в одной точке — это уже
            // не косметика, а тройной удар по игроку.
            var components = group.GetComponents<Component>();
            for (int i = components.Length - 1; i >= 0; i--)
            {
                if (components[i] is Transform)
                {
                    continue;
                }

                Object.DestroyImmediate(components[i]);
            }

            group.localPosition = Vector3.zero;
            group.localRotation = Quaternion.identity;
            return group;
        }
    }
}
