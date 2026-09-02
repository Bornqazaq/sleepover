using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Core.Player;
using Igruha.Core.Spawning;
using Igruha.Core.UI;
using Igruha.Minigames.HoleInWall;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Строит арену «Дырки в стене» из примитивов: общий бассейн, четыре
    /// платформы над ним, по стене на дорожку, точки спавна и зону воды.
    /// Геометрия серая — арт приезжает в фазе 4.
    ///
    /// Всё строится кодом по той же причине, что арены «Экзамена», цирка и
    /// «Рейса на память»: размеры живут в <see cref="HoleInWallConfig"/>, и
    /// пересобрать арену после правки числа должно быть одним нажатием.
    /// Здесь это критичнее обычного — на плейтесте будут двигать и разнос
    /// вырезов, и длину троса, и подъезды стен.
    /// </summary>
    public static class HoleInWallArenaBuilder
    {
        private const string ArenaRoot = "_Arena";
        private const string BoundsRoot = "_Bounds";
        private const string SpawnsRoot = "_Spawns";

        private const float SlabThickness = 0.4f;

        /// <summary>
        /// Зерно генератора дресса. Своё, а не общее с билдером: билдер
        /// раскладывает арену детерминированно, и подмешивать в его
        /// последовательность выбор моделей нельзя — смена модели сдвинула бы
        /// проверенную планировку (правило подфазы 4.1).
        /// </summary>
        private const int DressSeed = 20260902;

        private static System.Random dressRandom;

        /// <summary>Насколько бортик бассейна торчит над водой, м.</summary>
        private const float PoolRimHeight = 0.72f;

        private static readonly Color PlatformColor = new Color(0.62f, 0.64f, 0.66f);
        private static readonly Color SupportColor = new Color(0.40f, 0.42f, 0.45f);
        private static readonly Color PoolColor = new Color(0.16f, 0.18f, 0.22f);
        private static readonly Color RimColor = new Color(0.52f, 0.54f, 0.56f);
        private static readonly Color WaterColor = new Color(0.20f, 0.55f, 0.85f, 0.45f);
        private static readonly Color WallColor = new Color(0.78f, 0.76f, 0.70f);
        private static readonly Color LadderColor = new Color(0.45f, 0.36f, 0.26f);

        [MenuItem("Igruha/Дырка в стене/Построить арену")]
        public static void Build()
        {
            HoleInWallConfig config = FindConfig();
            if (config == null)
            {
                EditorUtility.DisplayDialog("Дырка в стене",
                    "Не найден HoleInWallConfig. Создай его через Create → Igruha → Hole In Wall Config.", "Ок");
                return;
            }

            dressRandom = new System.Random(DressSeed);
            HoleInWallDress.Begin();

            ReplaceRoot(ArenaRoot, out Transform arena);
            ReplaceRoot(BoundsRoot, out Transform bounds);

            BuildPool(arena, config);
            BuildLadders(arena, config);

            var tracks = new HoleInWallTrack[config.TrackCount];
            for (int i = 0; i < config.TrackCount; i++)
            {
                tracks[i] = BuildTrack(arena, config, i);
            }

            BuildWaterZone(bounds, config);
            BuildSpawns(config);
            WireController(tracks);
            EnsureHudStatusLine();
            VerifyLayout(config);
            ReportMissingModels();

            Debug.Log(
                $"🧱 Арена «Дырки в стене» построена: {config.TrackCount} дорожек, " +
                $"арена {config.ArenaWidth:F1}×{config.ArenaDepth:F1} м, путь стены {config.WallTravel:F1} м, " +
                $"платформа {config.PlatformWidth:F2}×{config.PlatformDepth:F2} м на {config.PlatformHeightOverWater:F2} м над водой",
                arena);

            Selection.activeGameObject = arena.gameObject;
        }

        // ========== БАССЕЙН ==========

        /// <summary>
        /// Бассейн один на все дорожки: сплошной объём под платформами.
        /// Падение безопасно, урона нет, возврат автоматический.
        /// </summary>
        private static void BuildPool(Transform parent, HoleInWallConfig config)
        {
            var pool = new GameObject("Pool").transform;
            pool.SetParent(parent, false);

            float width = config.ArenaWidth;
            float depth = config.ArenaDepth;
            float centerZ = (config.ArenaFarZ + config.ArenaNearZ) * 0.5f;

            SetLayer(CreateBox(pool, "PoolFloor",
                new Vector3(width, SlabThickness, depth),
                new Vector3(0f, config.PoolBottomY - SlabThickness * 0.5f, centerZ),
                PoolColor), "Ground");

            // Бортики: из бассейна не выплыть за пределы арены. Стоят от дна
            // до метра над водой — выше незачем, а вплотную за платформой их
            // и вовсе нет: там отходит камера (igruha/CLAUDE.md, 2a).
            float rimHeight = config.PoolDepth + PoolRimHeight;
            float rimCenterY = config.PoolBottomY + rimHeight * 0.5f;
            float halfWidth = width * 0.5f;

            CreateDressedBox(pool, "Rim_Far", new Vector3(width, rimHeight, SlabThickness),
                new Vector3(0f, rimCenterY, config.ArenaFarZ), RimColor, "Ground", HoleInWallDress.Kind.PoolRim);
            CreateDressedBox(pool, "Rim_Near", new Vector3(width, rimHeight, SlabThickness),
                new Vector3(0f, rimCenterY, config.ArenaNearZ), RimColor, "Ground", HoleInWallDress.Kind.PoolRim);
            CreateDressedBox(pool, "Rim_Left", new Vector3(SlabThickness, rimHeight, depth),
                new Vector3(-halfWidth, rimCenterY, centerZ), RimColor, "Ground", HoleInWallDress.Kind.PoolRim);
            CreateDressedBox(pool, "Rim_Right", new Vector3(SlabThickness, rimHeight, depth),
                new Vector3(halfWidth, rimCenterY, centerZ), RimColor, "Ground", HoleInWallDress.Kind.PoolRim);

            // Вода — только вид. Коллайдера нет: в неё падают, а не стоят на ней,
            // и камере она должна быть прозрачна.
            GameObject water = CreateBox(pool, "Water",
                new Vector3(width - SlabThickness, 0.05f, depth - SlabThickness),
                new Vector3(0f, config.WaterSurfaceY, centerZ), WaterColor);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.GetComponent<Renderer>().sharedMaterial = HoleInWallMaterials.Transparent(WaterColor);
            SetLayer(water, "Default");
        }

        /// <summary>
        /// Лесенки по краям бассейна — <b>декор</b>. Подниматься по ним не нужно
        /// и не предполагается: возврат на платформу автоматический через 4.5 с
        /// (решение геймдизайнера 31.08).
        /// </summary>
        private static void BuildLadders(Transform parent, HoleInWallConfig config)
        {
            var ladders = new GameObject("Ladders").transform;
            ladders.SetParent(parent, false);

            float height = config.PlatformHeightOverWater + config.PoolDepth;
            float centerY = config.PoolBottomY + height * 0.5f;
            float z = config.PlatformBackZ - config.PlatformDepth;

            for (int i = 0; i < config.TrackCount; i++)
            {
                float x = config.TrackCenterX(i) + config.PlatformWidth * 0.5f + config.TrackGap * 0.5f;
                CreateDressedBox(ladders, $"Ladder_{i}",
                    new Vector3(config.TrackGap * 0.5f, height, SlabThickness),
                    new Vector3(x, centerY, z), LadderColor, "Ground", HoleInWallDress.Kind.Ladder);
            }
        }

        // ========== ДОРОЖКА ==========

        private static HoleInWallTrack BuildTrack(Transform parent, HoleInWallConfig config, int index)
        {
            var root = new GameObject($"Track_{index}");
            root.transform.SetParent(parent, false);

            // Начало координат дорожки — линия проверки на полу платформы:
            // от неё считаются и вырезы, и путь стены.
            root.transform.localPosition =
                new Vector3(config.TrackCenterX(index), config.PlatformSurfaceY, config.CheckLineZ);

            CreateDressedBox(root.transform, "Platform",
                new Vector3(config.PlatformWidth, config.PlatformThickness, config.PlatformDepth),
                new Vector3(0f, -config.PlatformThickness * 0.5f, 0f),
                PlatformColor, "Ground", HoleInWallDress.Kind.PlatformDeck);

            float supportHeight = config.PlatformHeightOverWater - config.PlatformThickness;
            CreateDressedBox(root.transform, "Support",
                new Vector3(config.PlatformWidth * 0.6f, supportHeight, config.PlatformDepth * 0.6f),
                new Vector3(0f, -config.PlatformThickness - supportHeight * 0.5f, 0f),
                SupportColor, "Ground", HoleInWallDress.Kind.Support);

            var slots = new Transform[2];
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = new GameObject($"Slot_{i}");
                slot.transform.SetParent(root.transform, false);
                slots[i] = slot.transform;
            }

            GameObject banner = BuildSoloBanner(root.transform, config);
            SweepingWall wall = BuildWall(root.transform, config);

            var track = root.AddComponent<HoleInWallTrack>();
            var so = new SerializedObject(track);
            so.FindProperty("index").intValue = index;
            so.FindProperty("wall").objectReferenceValue = wall;
            so.FindProperty("soloBanner").objectReferenceValue = banner;

            SerializedProperty slotArray = so.FindProperty("slots");
            slotArray.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                slotArray.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return track;
        }

        /// <summary>
        /// Надпись над дорожкой одиночки. Подача — шутка, а не сглаживание:
        /// неравенство составов в проекте подаётся как повод посмеяться
        /// (спека 5.1).
        /// </summary>
        private static GameObject BuildSoloBanner(Transform parent, HoleInWallConfig config)
        {
            var banner = new GameObject("SoloBanner");
            banner.transform.SetParent(parent, false);
            banner.transform.localPosition = new Vector3(0f, config.WallHeight + 1.2f, 0f);

            // Лицом к камере: та стоит за спиной игрока, со стороны бассейна.
            banner.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var text = banner.AddComponent<TextMeshPro>();
            text.text = "ОСТАЛСЯ БЕЗ ДРУЗЕЙ";
            text.fontSize = 5f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.85f, 0.2f);
            text.rectTransform.sizeDelta = new Vector2(config.PlatformWidth, 1.5f);

            banner.SetActive(false);
            return banner;
        }

        /// <summary>
        /// Стена: пять плит вокруг вырезов и два контура. Плиты переставляет
        /// <see cref="SweepingWall"/> под каждый рисунок, здесь они только
        /// заводятся — и сразу складываются в сплошную стену, чтобы дорожку
        /// было видно в редакторе, не запуская игру.
        ///
        /// ⚠️ Коллайдеров у стены нет намеренно — см. <see cref="SweepingWall"/>.
        /// </summary>
        private static SweepingWall BuildWall(Transform parent, HoleInWallConfig config)
        {
            var root = new GameObject("Wall");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0f, config.WallStartZ + config.WallThickness * 0.5f);

            Transform left = CreatePanel(root.transform, "Panel_Left", config);
            Transform middle = CreatePanel(root.transform, "Panel_Middle", config);
            Transform right = CreatePanel(root.transform, "Panel_Right", config);
            Transform lintelA = CreatePanel(root.transform, "Lintel_A", config);
            Transform lintelB = CreatePanel(root.transform, "Lintel_B", config);

            // Вид по умолчанию — сплошная плита: так стена читается в редакторе.
            left.localScale = new Vector3(config.WallWidth, config.WallHeight, config.WallThickness);
            left.localPosition = new Vector3(0f, config.WallHeight * 0.5f, 0f);
            middle.gameObject.SetActive(false);
            right.gameObject.SetActive(false);
            lintelA.gameObject.SetActive(false);
            lintelB.gameObject.SetActive(false);

            WallCutout first = CreateCutout(root.transform, "Cutout_A");
            WallCutout second = CreateCutout(root.transform, "Cutout_B");

            var wall = root.AddComponent<SweepingWall>();
            var so = new SerializedObject(wall);
            so.FindProperty("panelLeft").objectReferenceValue = left;
            so.FindProperty("panelMiddle").objectReferenceValue = middle;
            so.FindProperty("panelRight").objectReferenceValue = right;
            so.FindProperty("lintelFirst").objectReferenceValue = lintelA;
            so.FindProperty("lintelSecond").objectReferenceValue = lintelB;
            so.FindProperty("firstCutout").objectReferenceValue = first;
            so.FindProperty("secondCutout").objectReferenceValue = second;
            so.ApplyModifiedPropertiesWithoutUndo();

            return wall;
        }

        private static Transform CreatePanel(Transform parent, string panelName, HoleInWallConfig config)
        {
            GameObject panel = CreateBox(parent, panelName,
                new Vector3(1f, config.WallHeight, config.WallThickness), Vector3.zero, WallColor);

            // ⚠️ Коллайдер снимаем: проверка в игре дискретная, и сплошная
            // панель вернула бы в неё габариты капсулы — толстый персонаж
            // перестал бы проходить там, где проходит тонкий.
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            return panel.transform;
        }

        private static WallCutout CreateCutout(Transform parent, string cutoutName)
        {
            var root = new GameObject(cutoutName);
            root.transform.SetParent(parent, false);

            Transform left = CreateOutline(root.transform, "Outline_Left");
            Transform right = CreateOutline(root.transform, "Outline_Right");
            Transform top = CreateOutline(root.transform, "Outline_Top");

            var cutout = root.AddComponent<WallCutout>();
            var so = new SerializedObject(cutout);
            so.FindProperty("leftPost").objectReferenceValue = left;
            so.FindProperty("rightPost").objectReferenceValue = right;
            so.FindProperty("lintel").objectReferenceValue = top;
            so.ApplyModifiedPropertiesWithoutUndo();

            left.gameObject.SetActive(false);
            right.gameObject.SetActive(false);
            top.gameObject.SetActive(false);

            return cutout;
        }

        private static Transform CreateOutline(Transform parent, string outlineName)
        {
            GameObject box = CreateBox(parent, outlineName,
                Vector3.one * WallCutout.FrameThickness, Vector3.zero, new Color(1f, 0.92f, 0.25f));
            Object.DestroyImmediate(box.GetComponent<Collider>());
            return box.transform;
        }

        // ========== ГРАНИЦЫ И СПАВНЫ ==========

        /// <summary>
        /// Объём воды как зона события. Режим <c>EventOnly</c>: что делать
        /// с упавшим, решают правила игры — здесь это отсчёт до автовозврата,
        /// а не молчаливый респавн.
        ///
        /// Триггер на <c>Default</c>: этот слой не входит в маску деокклюдера,
        /// иначе камера начала бы цепляться за пустоту (igruha/CLAUDE.md, 2a).
        /// </summary>
        private static void BuildWaterZone(Transform parent, HoleInWallConfig config)
        {
            var zone = new GameObject("KillZone_Water");
            zone.transform.SetParent(parent, false);

            float height = config.PoolDepth;
            zone.transform.position = new Vector3(0f,
                config.WaterSurfaceY - height * 0.5f,
                (config.ArenaFarZ + config.ArenaNearZ) * 0.5f);

            var box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(config.ArenaWidth, height, config.ArenaDepth);

            var kill = zone.AddComponent<KillZone>();
            var so = new SerializedObject(kill);
            SerializedProperty mode = so.FindProperty("mode");
            if (mode != null)
            {
                mode.enumValueIndex = (int)KillZone.ZoneMode.EventOnly;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            SetLayer(zone, "Default");
        }

        /// <summary>
        /// По две точки на дорожку. Куда игроки встанут на самом деле, решает
        /// контроллер по составу — эти точки нужны, чтобы до старта раунда
        /// никто не висел над пустотой.
        /// </summary>
        private static void BuildSpawns(HoleInWallConfig config)
        {
            GameObject spawnsRoot = GameObject.Find(SpawnsRoot);
            if (spawnsRoot == null)
            {
                Debug.LogWarning($"В сцене нет объекта {SpawnsRoot} — точки спавна ставить некуда");
                return;
            }

            var existing = spawnsRoot.GetComponentsInChildren<SpawnPoint>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                Object.DestroyImmediate(existing[i].gameObject);
            }

            float half = config.PairSpread * 0.5f;
            int number = 1;

            for (int track = 0; track < config.TrackCount; track++)
            {
                for (int slot = 0; slot < 2; slot++)
                {
                    var point = new GameObject($"Spawn_{number++}");
                    point.transform.SetParent(spawnsRoot.transform, false);
                    point.transform.position = new Vector3(
                        config.TrackCenterX(track) + (slot == 0 ? -half : half),
                        config.PlatformSurfaceY,
                        config.CheckLineZ);

                    // Лицом к стене: туда же смотрит и фиксированный фронт.
                    point.transform.rotation = Quaternion.LookRotation(-SweepingWall.TravelDirection, Vector3.up);
                    point.AddComponent<SpawnPoint>();
                }
            }
        }

        /// <summary>Отдать контроллеру собранные дорожки: иначе играть будет нечем.</summary>
        private static void WireController(HoleInWallTrack[] tracks)
        {
            var game = Object.FindFirstObjectByType<HoleInWallMinigame>(FindObjectsInactive.Include);
            if (game == null)
            {
                Debug.LogWarning("В сцене нет HoleInWallMinigame — дорожки некому отдать. " +
                                 "Повесь контроллер на MinigameManager и построй арену заново");
                return;
            }

            var so = new SerializedObject(game);
            SerializedProperty array = so.FindProperty("tracks");
            array.arraySize = tracks.Length;
            for (int i = 0; i < tracks.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = tracks[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ========== ПРОВЕРКИ ПЛАНИРОВКИ ==========

        /// <summary>
        /// Сверяет планировку с физикой персонажа и с правилами камеры.
        /// Существует затем, чтобы арена не могла уехать молча: оба числа
        /// в спеке названы расчётными, и пересчитывать их руками при каждой
        /// правке никто не станет.
        /// </summary>
        private static void VerifyLayout(HoleInWallConfig config)
        {
            CharacterConfig character = FindCharacterConfig();
            if (character != null)
            {
                float gravityUp = Mathf.Abs(Physics.gravity.y) * character.RiseGravityMultiplier;
                float jumpHeight = gravityUp > 0f
                    ? character.JumpSpeed * character.JumpSpeed / (2f * gravityUp)
                    : 0f;

                if (config.WallHeight < jumpHeight * 2f)
                {
                    Debug.LogWarning(
                        $"Стена {config.WallHeight:F2} м при прыжке {jumpHeight:F2} м — запас меньше двукратного. " +
                        "Спека считает высоту 5 ШП расчётной именно из этого запаса");
                }
                else
                {
                    Debug.Log($"Прыжок берёт {jumpHeight:F2} м, стена {config.WallHeight:F2} м — перепрыгнуть нельзя");
                }
            }

            // Правило раздела 2a: персонажу нужно около 4.5 м позади на отход
            // камеры. Здесь позади открытая вода, и запас задаётся конфигом.
            const float requiredClearance = 4.5f;
            if (config.CameraClearance < requiredClearance)
            {
                Debug.LogWarning(
                    $"За платформой всего {config.CameraClearance:F2} м — камере нужно {requiredClearance:F1} м " +
                    "на полный отход (igruha/CLAUDE.md, 2a). Поднять cameraClearance в конфиге");
            }

            // Трос обязан дотягиваться до самых разнесённых вырезов, иначе
            // часть стен непроходима физически — та самая ошибка LDD.
            float widestReach = config.SpreadMax - 2f * config.HitTolerance;
            if (widestReach > config.TetherLength)
            {
                Debug.LogError(
                    $"Разнос {config.SpreadMax:F2} м требует {widestReach:F2} м троса при длине {config.TetherLength:F2} м — " +
                    "такие стены непроходимы. Уменьшить spreadMax или удлинить трос");
            }
        }

        // ========== ОБЩЕЕ ==========

        /// <summary>
        /// Дать HUD строку статуса, если её нет.
        ///
        /// В шаблоне сцены поле <c>statusText</c> пустое, а <c>ShowStatus</c>
        /// с пустой ссылкой молча выходит — то есть номер стены и счёт
        /// не выводились бы ни разу. Ровно этот класс поломок ловили у
        /// «Верю / не верю» (STATE 3.14, 3.26) и у «Рейса на память» (3.31).
        ///
        /// ⚠️ Холст берётся <b>от самого HUD</b>, а не поиском первого
        /// попавшегося <c>Canvas</c>: поиск первого однажды уже утащил весь
        /// интерфейс внутрь мирового холста пузыря реплики.
        /// </summary>
        private static void EnsureHudStatusLine()
        {
            var hud = Object.FindFirstObjectByType<RoundHud>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning("В сцене нет RoundHud — строку статуса вешать не на что");
                return;
            }

            var so = new SerializedObject(hud);
            SerializedProperty property = so.FindProperty("statusText");
            if (property == null || property.objectReferenceValue != null)
            {
                return;
            }

            var canvas = hud.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("RoundHud не лежит под Canvas — строку статуса некуда положить", hud);
                return;
            }

            Transform existing = canvas.transform.Find("StatusLine");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = new GameObject("StatusLine", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(1100f, 68f);

            // Под таймером раунда, а не поверх него: TimerText занимает полосу
            // до -110 при высоте 80.
            rect.anchoredPosition = new Vector2(0f, -118f);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = 24f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Top;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            text.outlineColor = new Color32(0, 0, 0, 210);
            text.outlineWidth = 0.2f;

            property.objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("HUD получил строку статуса: номер стены, счёт дорожки и время до удара", text);
        }

        private static void ReplaceRoot(string rootName, out Transform root)
        {
            var existing = GameObject.Find(rootName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            root = new GameObject(rootName).transform;
        }

        /// <summary>
        /// Коробка блокаута, одетая в модель пака. Геометрия, коллайдер и слой
        /// те же, что на сером блокауте: гаснет только рендерер коробки, модель
        /// садится внутрь по её габаритам. Поэтому выверенная фазами 2–3
        /// планировка не может сдвинуться от арта.
        ///
        /// Слой ставится <b>до</b> дресса: <see cref="SetLayer"/> красит и детей,
        /// и модели пака уехали бы на <c>Ground</c> вместе с коробкой.
        /// </summary>
        private static GameObject CreateDressedBox(Transform parent, string boxName, Vector3 size, Vector3 position,
            Color color, string layerName, HoleInWallDress.Kind kind)
        {
            GameObject box = CreateBox(parent, boxName, size, position, color);
            SetLayer(box, layerName);
            HoleInWallDress.Apply(box, kind, dressRandom);
            return box;
        }

        /// <summary>
        /// Модели, которых не нашлось в проекте, — списком и всегда. Паки Synty
        /// в репозиторий не входят, и на машине без них арена соберётся серой:
        /// без этой строки разница читалась бы как «арт не сделан».
        /// </summary>
        private static void ReportMissingModels()
        {
            IReadOnlyList<string> missing = HoleInWallDress.Missing;
            if (missing.Count == 0)
            {
                return;
            }

            var paths = new string[missing.Count];
            for (int i = 0; i < missing.Count; i++)
            {
                paths[i] = missing[i];
            }

            Debug.LogWarning(
                $"«Дырка в стене»: не найдено моделей паков — {missing.Count}. Там, где их нет, арена осталась блокаутом. " +
                "Поставь паки Synty (POLYGON Nightclubs, POLYGON Generic) и пересобери.\n— " +
                string.Join("\n— ", paths));
        }

        private static GameObject CreateBox(Transform parent, string boxName, Vector3 size, Vector3 position, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = boxName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = HoleInWallMaterials.Opaque(color);
            return go;
        }

        private static void SetLayer(GameObject go, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                Debug.LogError($"Слоя '{layerName}' нет в проекте — камера будет проходить сквозь {go.name}");
                return;
            }

            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayer(child.gameObject, layerName);
            }
        }

        private static HoleInWallConfig FindConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:HoleInWallConfig");
            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<HoleInWallConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static CharacterConfig FindCharacterConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:CharacterConfig");
            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<CharacterConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
