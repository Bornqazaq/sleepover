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
    /// </summary>
    internal static class CircusArenaBuilder
    {
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/CircusArenaConfig.asset";
        private const string GroundLayerName = "Ground";

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
        /// <summary>Сторона клетки, смотрящая наружу арены: локальный +Z смотрит в центр, значит наружу — 180°.</summary>
        private const int OuterWallSide = 2;
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
                Debug.LogError("CircusArenaBuilder: открой сцену Stopwatch — не найдены _Arena/_Spawns.");
                return;
            }

            int groundLayer = LayerMask.NameToLayer(GroundLayerName);
            if (groundLayer < 0)
            {
                Debug.LogError($"CircusArenaBuilder: не найден слой {GroundLayerName} — прыжок и обход камерой сломаются.");
                return;
            }

            BuildPitFloor(arenaRoot.transform, config, groundLayer);
            BuildPitRim(arenaRoot.transform, config, groundLayer);
            BuildTentFloor(arenaRoot.transform, config, groundLayer);
            BuildTentWall(arenaRoot.transform, config, groundLayer);
            BuildRigging(arenaRoot.transform, config);
            BuildScoreboard(arenaRoot.transform, config);
            BuildCages(arenaRoot.transform, config, groundLayer);
            BuildSpawns(spawnsRoot.transform, config);
            BuildBounds(config);

            Validate(config);

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            Debug.Log($"Цирковая арена пересобрана: шатёр R={config.TentRadius:F2}, яма R={config.PitRadius:F2}, " +
                      $"кольцо клеток R={config.CageRingRadius:F2}, клеток {config.CageAnchorCount}, " +
                      $"зазор между клетками {config.GetCageGap():F2} м, " +
                      $"игрок достаёт до {config.GetPlayerReachFromCentre():F2} м от центра (яма {config.PitRadius:F2}), " +
                      $"дно клетки на верхней ступени {config.GetCageBottomHeight(config.MaxLevelSteps):F2} м.");
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
            }
        }

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
            }
        }

        /// <summary>Ферма под куполом: к ней крепятся цепи клеток. Коллайдеры сняты — под ней только летящие вниз.</summary>
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
                face.localScale = new Vector3(config.ScoreboardFaceWidth, config.ScoreboardFaceHeight, ScoreboardDepth);

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
            }
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

            for (int side = 0; side < 4; side++)
            {
                BuildCageWall(anchor, groundLayer, side, half, height);
            }

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
            var station = anchorGo.AddComponent<CageStation>();
            var serialized = new SerializedObject(station);
            serialized.FindProperty("doorLeft").objectReferenceValue = anchor.Find("Floor/DoorLeft");
            serialized.FindProperty("doorRight").objectReferenceValue = anchor.Find("Floor/DoorRight");
            serialized.FindProperty("propSlot").objectReferenceValue = slot.transform;
            serialized.FindProperty("respawnPoint").objectReferenceValue = respawn.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Цепь от фермы к крыше клетки. Живёт отдельно от клетки: если бы цепь
        /// была её дочерним объектом, при спуске она уезжала бы вниз вместе
        /// с клеткой вместо того, чтобы вытравливаться.
        /// </summary>
        private static void BuildChain(Transform parent, CircusArenaConfig config, int index)
        {
            Vector3 anchor = config.GetAnchorPosition(index);
            float bottom = anchor.y + config.CageInnerHeight + CageFloorThickness;
            float length = Mathf.Max(0.1f, config.RiggingHeight - bottom);

            Transform chain = ResetPrimitive(parent, $"Chain_{index + 1:00}", PrimitiveType.Cylinder);
            StripCollider(chain);
            chain.position = new Vector3(anchor.x, bottom + length * 0.5f, anchor.z);
            chain.localScale = new Vector3(ChainThickness, length * 0.5f, ChainThickness);
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

            // Внешняя стена остаётся без прутьев — только коллайдер. Игрок стоит
            // лицом к центру арены, камера смотрит ему в спину снаружи, и прутья
            // этой стены оказываются между камерой и игроком: один из них встаёт
            // ровно посреди экрана. Сквозь неё игроку смотреть всё равно некуда,
            // а клетка читается клеткой по трём остальным стенам, полу и крыше.
            if (side == OuterWallSide)
            {
                return;
            }

            for (int bar = 0; bar < BarsPerCageSide; bar++)
            {
                float t = (bar + 0.5f) / BarsPerCageSide;
                Transform pole = MakeBox(wall, $"Bar_{bar + 1}", groundLayer);
                StripCollider(pole);
                pole.localPosition = new Vector3(Mathf.Lerp(-half, half, t), 0f, 0f);
                pole.localScale = new Vector3(BarThickness, height, BarThickness);
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

            group.localPosition = Vector3.zero;
            group.localRotation = Quaternion.identity;
            return group;
        }
    }
}
