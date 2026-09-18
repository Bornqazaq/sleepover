using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Core.Minigame;
using Igruha.Core.Spawning;
using Igruha.Core.Traps;
using Igruha.Core.UI;
using Igruha.Minigames.Infection;
using TMPro;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Сборка арены «Заражения» из серых блоков по координатам спеки
    /// (`docs/minigames/infection.md`, раздел 3.2).
    ///
    /// Расстановка кодом, а не руками: площадка обязана пересобираться после
    /// каждой правки размеров, а правило «вокруг каждого объекта два обхода»
    /// проверяется числами, а не на глаз. Пункт меню идемпотентен — каждый
    /// прогон строит арену заново поверх старой.
    ///
    /// Высоты заданы от персонажа (рост 1.65, прыжок ~1.6, шаг 0.42) и не
    /// масштабируются: просвет трубы 2.2 — это «пробежать не пригибаясь»,
    /// крыша лазалки 2.6 — «не запрыгнуть», ступень 0.35 — «взбежать не прыгая».
    /// Масштабируются только расстояния по площадке.
    /// </summary>
    internal static class InfectionArenaBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Minigames/Infection.unity";
        private const string SettingsFolder = "Assets/_Project/Settings/Gameplay/Minigames";
        private const string ConfigPath = SettingsFolder + "/InfectionConfig.asset";
        private const string DefinitionPath = SettingsFolder + "/Infection.asset";
        private const string CatalogPath = SettingsFolder + "/MinigameCatalog.asset";
        private const string MaterialFolder = "Assets/_Project/Art/Minigames/Infection/Blockout";
        private const string SceneName = "Infection";

        // ===== Арена =====
        private const float ArenaWidth = 52f;
        private const float ArenaDepth = 40f;
        private const float FenceHeight = 3f;
        private const float FenceThickness = 0.4f;

        // ===== Объекты (центры в мире, спека 3.2) =====
        private static readonly Vector2 CarouselCenter = new Vector2(0f, 0f);
        private const float CarouselRadius = 4f;
        private const float CarouselDeckHeight = 0.4f;

        private static readonly Vector2 SlideCenter = new Vector2(-16f, 8f);
        private const float SlideTopHeight = 3f;
        private const float SlideWidth = 2.4f;
        private const float SlideAngle = 50f;
        private const float StepRise = 0.35f;
        private const float StepRun = 0.45f;

        private static readonly Vector2 SwingCenter = new Vector2(13f, 10f);
        private const float SwingFrameWidth = 8f;
        private const float SwingBarHeight = 3.2f;
        private const float SwingLength = 2.6f;

        private static readonly Vector2 TubeWest = new Vector2(-20f, -4f);
        private static readonly Vector2 TubeEast = new Vector2(-16f, -4f);
        private const float TubeLength = 6f;
        private const float TubeClear = 2.2f;
        private const float TubeWall = 0.2f;

        private static readonly Vector2 ClimberCenter = new Vector2(17.5f, -4f);
        private const float ClimberSize = 5f;
        private const float ClimberHeight = 2.6f;
        private const float ClimberPassage = 1.6f;

        private static readonly Vector2 SandCenter = new Vector2(0f, -12f);
        private const float SandWidth = 9f;
        private const float SandDepth = 6f;
        private const float SandBorder = 0.4f;

        private static readonly Vector2[] SpawnPoints =
        {
            new Vector2(-11f, -15f), new Vector2(11f, -15f),
            new Vector2(21f, -8f), new Vector2(21f, 8f),
            new Vector2(11f, 15f), new Vector2(-11f, 15f),
            new Vector2(-21f, 8f), new Vector2(-21f, -8f)
        };

        private static readonly Color FloorColor = new Color(0.62f, 0.60f, 0.58f);
        private static readonly Color PropColor = new Color(0.70f, 0.68f, 0.64f);
        private static readonly Color AccentColor = new Color(0.78f, 0.60f, 0.42f);
        private static readonly Color SandColor = new Color(0.82f, 0.76f, 0.58f);
        private static readonly Color FenceColor = new Color(0.45f, 0.45f, 0.47f);

        private static int groundLayer;
        private static int barrierLayer;

        [MenuItem("Igruha/Minigames/Собрать арену «Заражение»")]
        private static void Build()
        {
            groundLayer = LayerMask.NameToLayer("Ground");
            barrierLayer = LayerMask.NameToLayer("PlayerBarrier");
            if (groundLayer < 0 || barrierLayer < 0)
            {
                Debug.LogError("InfectionArenaBuilder: нет слоёв Ground/PlayerBarrier — прыжок и обход камерой сломаются");
                return;
            }

            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return;
                }

                EditorSceneManager.OpenScene(ScenePath);
            }

            GameObject arena = Root("_Arena");
            GameObject spawns = Root("_Spawns");
            GameObject bounds = Root("_Bounds");
            GameObject ui = Root("_UI");
            GameObject manager = Root("MinigameManager");
            if (arena == null || spawns == null || bounds == null || ui == null || manager == null)
            {
                Debug.LogError("InfectionArenaBuilder: сцена не похожа на шаблон — не найдены корневые группы");
                return;
            }

            InfectionConfig config = EnsureConfig();

            CleanTemplateGeometry(arena);
            BuildGround(arena);
            RotatingPlatform carousel = BuildCarousel(arena, config);
            BuildSlide(arena);
            PendulumSwing[] swings = BuildSwings(arena);
            BuildTubes(arena);
            BuildClimber(arena);
            SpeedZone sand = BuildSandbox(arena, config);
            MoveSpawns(spawns);
            SetupKillZone(bounds);

            AnnouncerBanner banner = BuildAnnouncer(ui);
            MinigameDefinition definition = EnsureDefinition(config);
            WireManager(manager, definition, config, carousel, swings, sand, banner, ui);

            RegisterScene(definition);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log("InfectionArenaBuilder: арена «Заражения» собрана — 52 × 40, шесть объектов, 8 спавнов");
        }

        // ================= ГЕОМЕТРИЯ =================

        /// <summary>
        /// Снести то, что осталось от шаблона: его пол, платформа и препятствие
        /// стоят в тех же координатах, что наша арена, и молча перекрывают
        /// карусель с песочницей.
        /// </summary>
        private static void CleanTemplateGeometry(GameObject arena)
        {
            for (int i = arena.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = arena.transform.GetChild(i);
                switch (child.name)
                {
                    case "Ground":
                    case "Carousel":
                    case "Slide":
                    case "Swings":
                    case "Tubes":
                    case "Climber":
                    case "Sandbox":
                    case "ArenaCenter":
                        continue;
                    default:
                        Object.DestroyImmediate(child.gameObject);
                        break;
                }
            }
        }

        private static void BuildGround(GameObject arena)
        {
            Transform group = ResetGroup(arena.transform, "Ground");

            Box(group, "Floor", new Vector3(0f, -0.2f, 0f),
                new Vector3(ArenaWidth, 0.4f, ArenaDepth), Material("Floor", FloorColor), groundLayer);

            // Забор непроходим и по нему же камера обходит арену: слой
            // PlayerBarrier входит в её маску препятствий. Высота 3 — выше
            // прыжка (около 1.6 от земли), перелезть нельзя.
            float halfX = ArenaWidth * 0.5f;
            float halfZ = ArenaDepth * 0.5f;
            Material fence = Material("Fence", FenceColor);

            Box(group, "Fence_North", new Vector3(0f, FenceHeight * 0.5f, halfZ),
                new Vector3(ArenaWidth + FenceThickness, FenceHeight, FenceThickness), fence, barrierLayer);
            Box(group, "Fence_South", new Vector3(0f, FenceHeight * 0.5f, -halfZ),
                new Vector3(ArenaWidth + FenceThickness, FenceHeight, FenceThickness), fence, barrierLayer);
            Box(group, "Fence_East", new Vector3(halfX, FenceHeight * 0.5f, 0f),
                new Vector3(FenceThickness, FenceHeight, ArenaDepth + FenceThickness), fence, barrierLayer);
            Box(group, "Fence_West", new Vector3(-halfX, FenceHeight * 0.5f, 0f),
                new Vector3(FenceThickness, FenceHeight, ArenaDepth + FenceThickness), fence, barrierLayer);
        }

        /// <summary>
        /// Карусель: вращающийся настил, несущий игроков. Диск — меш-коллайдер,
        /// а не капсула примитива: капсула цилиндра скруглена по краю, и с неё
        /// соскальзывают, стоя у самого борта.
        /// </summary>
        private static RotatingPlatform BuildCarousel(GameObject arena, InfectionConfig config)
        {
            Transform group = ResetGroup(arena.transform, "Carousel");
            group.position = new Vector3(CarouselCenter.x, 0f, CarouselCenter.y);

            GameObject deck = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            deck.name = "Deck";
            deck.transform.SetParent(group, false);
            deck.transform.localPosition = new Vector3(0f, CarouselDeckHeight * 0.5f, 0f);
            deck.transform.localScale = new Vector3(CarouselRadius * 2f, CarouselDeckHeight * 0.5f, CarouselRadius * 2f);
            deck.layer = groundLayer;
            SetMaterial(deck, Material("Carousel", AccentColor));

            Object.DestroyImmediate(deck.GetComponent<CapsuleCollider>());
            MeshCollider mesh = deck.AddComponent<MeshCollider>();
            mesh.convex = true;

            // Поручни — единственное, по чему видно, что карусель крутится.
            // Коллайдеров у них нет: пассажира держит настил, а перила на этой
            // фазе только читаются глазом.
            Material rail = Material("CarouselRail", PropColor);
            for (int i = 0; i < 3; i++)
            {
                GameObject spoke = Box(group, $"Rail_{i + 1}", new Vector3(0f, CarouselDeckHeight + 0.5f, 0f),
                    new Vector3(CarouselRadius * 1.9f, 0.14f, 0.2f), rail, groundLayer, collider: false);
                spoke.transform.localRotation = Quaternion.Euler(0f, i * 60f, 0f);
            }

            Rigidbody body = group.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;

            // Коллайдер на корне нужен самой платформе: по нему она меряет
            // высоту настила, на которой ищет пассажиров.
            BoxCollider probe = group.gameObject.AddComponent<BoxCollider>();
            probe.size = new Vector3(CarouselRadius * 2f, CarouselDeckHeight, CarouselRadius * 2f);
            probe.center = new Vector3(0f, CarouselDeckHeight * 0.5f, 0f);
            probe.isTrigger = true;

            RotatingPlatform platform = group.gameObject.AddComponent<RotatingPlatform>();
            SerializedObject serialized = new SerializedObject(platform);
            serialized.FindProperty("degreesPerSecond").floatValue = config.CarouselDegreesPerSecond;
            serialized.FindProperty("radius").floatValue = CarouselRadius;
            serialized.FindProperty("passengerLayers").intValue = ~0;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return platform;
        }

        /// <summary>
        /// Горка: ступени с севера, скат на юг. Подниматься по скату нельзя
        /// физически — <see cref="SlideSurface"/> держит съезжающего вниз,
        /// сколько бы он ни жал вперёд.
        /// </summary>
        private static void BuildSlide(GameObject arena)
        {
            Transform group = ResetGroup(arena.transform, "Slide");
            group.position = new Vector3(SlideCenter.x, 0f, SlideCenter.y);

            Material body = Material("Slide", AccentColor);
            Material steps = Material("SlideSteps", PropColor);

            // Площадка наверху: 3 × 3, её южный край — начало ската.
            Box(group, "Platform", new Vector3(0f, SlideTopHeight - 0.15f, 1.5f),
                new Vector3(SlideWidth + 0.4f, 0.3f, 3f), body, groundLayer);

            Box(group, "Post_W", new Vector3(-SlideWidth * 0.5f, SlideTopHeight * 0.5f, 1.5f),
                new Vector3(0.2f, SlideTopHeight, 0.2f), body, groundLayer);
            Box(group, "Post_E", new Vector3(SlideWidth * 0.5f, SlideTopHeight * 0.5f, 1.5f),
                new Vector3(0.2f, SlideTopHeight, 0.2f), body, groundLayer);

            // Ступени на север от площадки: шаг 0.35 меньше stepHeight 0.42,
            // поэтому они взбегаются штатной анимацией бега, без прыжка.
            int stepCount = Mathf.CeilToInt(SlideTopHeight / StepRise);
            for (int i = 0; i < stepCount; i++)
            {
                float top = SlideTopHeight - StepRise * (i + 1);
                if (top <= 0.05f)
                {
                    break;
                }

                float z = 3f + StepRun * i + StepRun * 0.5f;
                Box(group, $"Step_{i + 1}", new Vector3(0f, top * 0.5f, z),
                    new Vector3(SlideWidth, top, StepRun), steps, groundLayer);
            }

            // Скат: от южного края площадки вниз под 50°.
            Vector3 top3 = new Vector3(0f, SlideTopHeight, 0f);
            float drop = SlideTopHeight;
            float run = drop / Mathf.Tan(SlideAngle * Mathf.Deg2Rad);
            Vector3 bottom = new Vector3(0f, 0.05f, -run);
            Vector3 direction = (bottom - top3).normalized;
            float length = Vector3.Distance(top3, bottom);

            GameObject chute = Box(group, "Chute", (top3 + bottom) * 0.5f,
                new Vector3(SlideWidth, 0.25f, length), body, groundLayer);
            chute.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);

            Box(group, "Chute_Rail_W", (top3 + bottom) * 0.5f + new Vector3(-SlideWidth * 0.5f, 0.35f, 0f),
                new Vector3(0.15f, 0.5f, length), body, groundLayer)
                .transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);
            Box(group, "Chute_Rail_E", (top3 + bottom) * 0.5f + new Vector3(SlideWidth * 0.5f, 0.35f, 0f),
                new Vector3(0.15f, 0.5f, length), body, groundLayer)
                .transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);

            // Зона съезда — прямая коробка поверх ската: её собственный «вперёд»
            // задаёт направление съезда, поэтому она развёрнута на юг, а не
            // наклонена вместе с жёлобом.
            GameObject zone = new GameObject("SlideZone");
            zone.transform.SetParent(group, false);
            zone.transform.localPosition = new Vector3(0f, SlideTopHeight * 0.5f, -run * 0.5f);
            zone.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            BoxCollider trigger = zone.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(SlideWidth + 0.3f, SlideTopHeight + 1.2f, run + 0.6f);
            zone.AddComponent<SlideSurface>();
        }

        private static PendulumSwing[] BuildSwings(GameObject arena)
        {
            Transform group = ResetGroup(arena.transform, "Swings");
            group.position = new Vector3(SwingCenter.x, 0f, SwingCenter.y);

            Material frame = Material("SwingFrame", PropColor);
            Material seat = Material("SwingSeat", AccentColor);

            float half = SwingFrameWidth * 0.5f;
            Box(group, "Bar", new Vector3(0f, SwingBarHeight, 0f),
                new Vector3(SwingFrameWidth, 0.2f, 0.2f), frame, groundLayer);

            for (int side = -1; side <= 1; side += 2)
            {
                float x = half * side;
                Box(group, side < 0 ? "Leg_W_N" : "Leg_E_N", new Vector3(x, SwingBarHeight * 0.5f, 1f),
                    new Vector3(0.2f, SwingBarHeight, 0.2f), frame, groundLayer);
                Box(group, side < 0 ? "Leg_W_S" : "Leg_E_S", new Vector3(x, SwingBarHeight * 0.5f, -1f),
                    new Vector3(0.2f, SwingBarHeight, 0.2f), frame, groundLayer);
            }

            var swings = new PendulumSwing[2];
            for (int i = 0; i < swings.Length; i++)
            {
                float x = i == 0 ? -1.5f : 1.5f;
                Vector3 pivot = group.position + new Vector3(x, SwingBarHeight, 0f);

                GameObject seatObject = Box(group, $"Seat_{i + 1}",
                    new Vector3(x, SwingBarHeight - SwingLength, 0f),
                    new Vector3(1.2f, 0.25f, 0.8f), seat, 0, collider: false);

                BoxCollider hit = seatObject.AddComponent<BoxCollider>();
                hit.isTrigger = true;
                hit.size = new Vector3(1.4f, 1.2f, 1.2f);

                PendulumSwing swing = seatObject.AddComponent<PendulumSwing>();
                swing.Configure(pivot, Vector3.right);

                SerializedObject serialized = new SerializedObject(swing);
                serialized.FindProperty("length").floatValue = SwingLength;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                swings[i] = swing;
            }

            return swings;
        }

        /// <summary>
        /// Трубы: сквозной проход без приседания (просвет 2.2 при росте 1.65)
        /// и прозрачные стенки, пока внутри кто-то есть. Две рядом — те самые
        /// «напёрстки»: вбежал в одну, вышел из соседней.
        /// </summary>
        private static void BuildTubes(GameObject arena)
        {
            Transform group = ResetGroup(arena.transform, "Tubes");

            BuildTube(group, "Tube_West", TubeWest);
            BuildTube(group, "Tube_East", TubeEast);
        }

        private static void BuildTube(Transform parent, string name, Vector2 center)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(center.x, 0f, center.y);

            Material wall = Material("Tube", AccentColor);
            Material glass = TransparentMaterial("TubeGlass", AccentColor);

            float halfClear = TubeClear * 0.5f;
            float wallCenter = halfClear + TubeWall * 0.5f;

            Box(root.transform, "Wall_W", new Vector3(-wallCenter, TubeClear * 0.5f, 0f),
                new Vector3(TubeWall, TubeClear + TubeWall, TubeLength), wall, groundLayer);
            Box(root.transform, "Wall_E", new Vector3(wallCenter, TubeClear * 0.5f, 0f),
                new Vector3(TubeWall, TubeClear + TubeWall, TubeLength), wall, groundLayer);
            Box(root.transform, "Roof", new Vector3(0f, TubeClear + TubeWall * 0.5f, 0f),
                new Vector3(TubeClear + TubeWall * 2f, TubeWall, TubeLength), wall, groundLayer);

            BoxCollider inside = root.AddComponent<BoxCollider>();
            inside.isTrigger = true;
            inside.center = new Vector3(0f, TubeClear * 0.5f, 0f);
            inside.size = new Vector3(TubeClear, TubeClear, TubeLength);

            SeeThroughShell shell = root.AddComponent<SeeThroughShell>();
            SerializedObject serialized = new SerializedObject(shell);
            SerializedProperty renderers = serialized.FindProperty("shell");
            Renderer[] found = root.GetComponentsInChildren<Renderer>(true);
            renderers.arraySize = found.Length;
            for (int i = 0; i < found.Length; i++)
            {
                renderers.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
            }

            serialized.FindProperty("transparentMaterial").objectReferenceValue = glass;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Лазалка: куб с двумя сквозными проходами крест-накрест. Верх
        /// непроходим — крыша на 2.6, выше прыжка с земли.
        /// </summary>
        private static void BuildClimber(GameObject arena)
        {
            Transform group = ResetGroup(arena.transform, "Climber");
            group.position = new Vector3(ClimberCenter.x, 0f, ClimberCenter.y);

            Material bars = Material("Climber", PropColor);
            float half = ClimberSize * 0.5f;
            float segment = (ClimberSize - ClimberPassage) * 0.5f;
            float offset = ClimberPassage * 0.5f + segment * 0.5f;

            for (int side = -1; side <= 1; side += 2)
            {
                // Северная и южная стенки: две панели с проходом посередине.
                Box(group, side < 0 ? "Wall_S_W" : "Wall_N_W",
                    new Vector3(-offset, ClimberHeight * 0.5f, half * side),
                    new Vector3(segment, ClimberHeight, 0.2f), bars, groundLayer);
                Box(group, side < 0 ? "Wall_S_E" : "Wall_N_E",
                    new Vector3(offset, ClimberHeight * 0.5f, half * side),
                    new Vector3(segment, ClimberHeight, 0.2f), bars, groundLayer);

                // Западная и восточная — так же, проход вдоль другой оси.
                Box(group, side < 0 ? "Wall_W_S" : "Wall_E_S",
                    new Vector3(half * side, ClimberHeight * 0.5f, -offset),
                    new Vector3(0.2f, ClimberHeight, segment), bars, groundLayer);
                Box(group, side < 0 ? "Wall_W_N" : "Wall_E_N",
                    new Vector3(half * side, ClimberHeight * 0.5f, offset),
                    new Vector3(0.2f, ClimberHeight, segment), bars, groundLayer);
            }

            Box(group, "Roof", new Vector3(0f, ClimberHeight + 0.1f, 0f),
                new Vector3(ClimberSize, 0.2f, ClimberSize), bars, groundLayer);
        }

        private static SpeedZone BuildSandbox(GameObject arena, InfectionConfig config)
        {
            Transform group = ResetGroup(arena.transform, "Sandbox");
            group.position = new Vector3(SandCenter.x, 0f, SandCenter.y);

            Material sand = Material("Sand", SandColor);
            Material border = Material("SandBorder", PropColor);

            Box(group, "Sand", new Vector3(0f, 0.05f, 0f),
                new Vector3(SandWidth, 0.1f, SandDepth), sand, groundLayer);

            float halfX = SandWidth * 0.5f;
            float halfZ = SandDepth * 0.5f;
            // Борт 0.4 ниже автошага 0.42 — перешагивается на бегу, не сбивает.
            Box(group, "Border_N", new Vector3(0f, SandBorder * 0.5f, halfZ),
                new Vector3(SandWidth + 0.4f, SandBorder, 0.2f), border, groundLayer);
            Box(group, "Border_S", new Vector3(0f, SandBorder * 0.5f, -halfZ),
                new Vector3(SandWidth + 0.4f, SandBorder, 0.2f), border, groundLayer);
            Box(group, "Border_E", new Vector3(halfX, SandBorder * 0.5f, 0f),
                new Vector3(0.2f, SandBorder, SandDepth), border, groundLayer);
            Box(group, "Border_W", new Vector3(-halfX, SandBorder * 0.5f, 0f),
                new Vector3(0.2f, SandBorder, SandDepth), border, groundLayer);

            GameObject zone = new GameObject("SandZone");
            zone.transform.SetParent(group, false);
            zone.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            BoxCollider trigger = zone.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(SandWidth, 2.5f, SandDepth);

            SpeedZone speed = zone.AddComponent<SpeedZone>();
            speed.SpeedMultiplier = config.SandSpeedMultiplier;
            return speed;
        }

        private static void MoveSpawns(GameObject spawns)
        {
            SpawnPoint[] points = spawns.GetComponentsInChildren<SpawnPoint>(true);
            var defaults = new List<SpawnPoint>(8);
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].Role == SpawnRole.Default)
                {
                    defaults.Add(points[i]);
                }
                else
                {
                    // Спец-роль в этой игре не нужна: все равны, Нулевого
                    // назначают после старта, а не спавном.
                    Object.DestroyImmediate(points[i].gameObject);
                }
            }

            for (int i = 0; i < SpawnPoints.Length; i++)
            {
                SpawnPoint point;
                if (i < defaults.Count)
                {
                    point = defaults[i];
                }
                else
                {
                    GameObject go = new GameObject($"Spawn_{i + 1}");
                    go.transform.SetParent(spawns.transform, false);
                    point = go.AddComponent<SpawnPoint>();
                }

                Vector3 position = new Vector3(SpawnPoints[i].x, 0.1f, SpawnPoints[i].y);
                point.transform.position = position;
                point.transform.rotation = Quaternion.LookRotation(-position.normalized, Vector3.up);
                point.gameObject.name = $"Spawn_{i + 1}";
            }

            for (int i = SpawnPoints.Length; i < defaults.Count; i++)
            {
                Object.DestroyImmediate(defaults[i].gameObject);
            }
        }

        private static void SetupKillZone(GameObject bounds)
        {
            KillZone zone = bounds.GetComponentInChildren<KillZone>(true);
            if (zone == null)
            {
                return;
            }

            zone.transform.position = new Vector3(0f, -5f, 0f);
            if (zone.TryGetComponent(out BoxCollider box))
            {
                box.isTrigger = true;
                box.size = new Vector3(ArenaWidth + 8f, 1f, ArenaDepth + 8f);
                box.center = Vector3.zero;
            }
        }

        // ================= UI =================

        private static AnnouncerBanner BuildAnnouncer(GameObject ui)
        {
            Transform canvas = ui.transform.Find("Canvas");
            if (canvas == null)
            {
                Debug.LogError("InfectionArenaBuilder: в _UI нет Canvas — плашку диктора вешать некуда");
                return null;
            }

            Transform existing = canvas.Find("AnnouncerPlate");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            GameObject plate = new GameObject("AnnouncerPlate", typeof(RectTransform), typeof(CanvasGroup));
            plate.transform.SetParent(canvas, false);

            RectTransform rect = plate.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 120f);
            rect.sizeDelta = new Vector2(900f, 64f);

            GameObject textObject = new GameObject("Line", typeof(RectTransform));
            textObject.transform.SetParent(plate.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 34f;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.text = string.Empty;

            AnnouncerBanner banner = plate.AddComponent<AnnouncerBanner>();
            SerializedObject serialized = new SerializedObject(banner);
            serialized.FindProperty("group").objectReferenceValue = plate.GetComponent<CanvasGroup>();
            serialized.FindProperty("label").objectReferenceValue = label;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return banner;
        }

        // ================= НАСТРОЙКИ =================

        private static InfectionConfig EnsureConfig()
        {
            InfectionConfig config = AssetDatabase.LoadAssetAtPath<InfectionConfig>(ConfigPath);
            if (config != null)
            {
                return config;
            }

            config = ScriptableObject.CreateInstance<InfectionConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        private static MinigameDefinition EnsureDefinition(InfectionConfig config)
        {
            MinigameDefinition definition = AssetDatabase.LoadAssetAtPath<MinigameDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<MinigameDefinition>();
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            }

            SerializedObject serialized = new SerializedObject(definition);
            serialized.FindProperty("displayName").stringValue = "Заражение";
            serialized.FindProperty("sceneName").stringValue = SceneName;
            serialized.FindProperty("roundDuration").floatValue = 60f;
            serialized.FindProperty("minPlayers").intValue = 4;
            serialized.FindProperty("maxPlayers").intValue = 8;
            serialized.FindProperty("objective").stringValue =
                "Не дай себя запятнать. Зелёный заражает касанием, заражённый начинает заражать сам.";

            SerializedProperty hints = serialized.FindProperty("controlHints");
            hints.arraySize = 3;
            hints.GetArrayElementAtIndex(0).stringValue = "WASD — бежать, Space — прыжок";
            hints.GetArrayElementAtIndex(1).stringValue = "ЛКМ — толкнуть: под качели или в толпу";
            hints.GetArrayElementAtIndex(2).stringValue = "Трубы и лазалка рвут погоню, песок замедляет всех";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(definition);
            EditorUtility.SetDirty(config);
            return definition;
        }

        private static void WireManager(
            GameObject manager,
            MinigameDefinition definition,
            InfectionConfig config,
            RotatingPlatform carousel,
            PendulumSwing[] swings,
            SpeedZone sand,
            AnnouncerBanner announcer,
            GameObject ui)
        {
            TemplateMinigame template = manager.GetComponent<TemplateMinigame>();
            if (template != null)
            {
                Object.DestroyImmediate(template);
            }

            InfectionMinigame game = manager.GetComponent<InfectionMinigame>();
            if (game == null)
            {
                game = manager.AddComponent<InfectionMinigame>();
            }

            // Сетевая половина: NetworkBehaviour на том же NetworkObject, что и
            // NetworkMinigameBridge. Реплицирует фазы заражения и реплики диктора.
            if (manager.GetComponent<InfectionNetwork>() == null)
            {
                manager.AddComponent<InfectionNetwork>();
            }

            Transform canvas = ui.transform.Find("Canvas");
            SerializedObject serialized = new SerializedObject(game);
            serialized.FindProperty("definition").objectReferenceValue = definition;
            serialized.FindProperty("roundTimer").objectReferenceValue = manager.GetComponent<RoundTimer>();
            serialized.FindProperty("tutorialScreen").objectReferenceValue = canvas != null ? canvas.GetComponent<TutorialScreen>() : null;
            serialized.FindProperty("hud").objectReferenceValue = canvas != null ? canvas.GetComponent<RoundHud>() : null;
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.FindProperty("carousel").objectReferenceValue = carousel;
            serialized.FindProperty("sandbox").objectReferenceValue = sand;
            serialized.FindProperty("announcer").objectReferenceValue = announcer;
            serialized.FindProperty("arenaRadius").floatValue = ArenaDepth * 0.5f - 2f;

            SerializedProperty swingList = serialized.FindProperty("swings");
            swingList.arraySize = swings.Length;
            for (int i = 0; i < swings.Length; i++)
            {
                swingList.GetArrayElementAtIndex(i).objectReferenceValue = swings[i];
            }

            SerializedProperty center = serialized.FindProperty("arenaCenter");
            center.objectReferenceValue = EnsureArenaCenter();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            MinigameBootstrap bootstrap = manager.GetComponent<MinigameBootstrap>();
            if (bootstrap != null)
            {
                SerializedObject boot = new SerializedObject(bootstrap);
                boot.FindProperty("minigame").objectReferenceValue = game;
                boot.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static Transform EnsureArenaCenter()
        {
            GameObject arena = Root("_Arena");
            Transform center = arena.transform.Find("ArenaCenter");
            if (center == null)
            {
                GameObject go = new GameObject("ArenaCenter");
                go.transform.SetParent(arena.transform, false);
                center = go.transform;
            }

            center.position = Vector3.zero;
            return center;
        }

        /// <summary>
        /// Сцена — в Build Settings, игра — в каталог приставки. Без первого
        /// её не загрузит NGO, без второго до неё не добраться из хаба.
        /// </summary>
        private static void RegisterScene(MinigameDefinition definition)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool present = false;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == ScenePath)
                {
                    scenes[i] = new EditorBuildSettingsScene(ScenePath, true);
                    present = true;
                    break;
                }
            }

            if (!present)
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();

            MinigameCatalog catalog = AssetDatabase.LoadAssetAtPath<MinigameCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogWarning("InfectionArenaBuilder: каталог мини-игр не найден — игра не появится на приставке");
                return;
            }

            SerializedObject serialized = new SerializedObject(catalog);
            SerializedProperty games = serialized.FindProperty("games");
            for (int i = 0; i < games.arraySize; i++)
            {
                if (games.GetArrayElementAtIndex(i).objectReferenceValue == definition)
                {
                    return;
                }
            }

            games.arraySize++;
            games.GetArrayElementAtIndex(games.arraySize - 1).objectReferenceValue = definition;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        // ================= ХЕЛПЕРЫ =================

        private static GameObject Root(string name)
        {
            GameObject found = GameObject.Find(name);
            return found;
        }

        private static Transform ResetGroup(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            GameObject group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static GameObject Box(
            Transform parent,
            string name,
            Vector3 localCenter,
            Vector3 size,
            Material material,
            int layer,
            bool collider = true)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localCenter;
            box.transform.localScale = size;
            box.layer = layer;

            if (!collider)
            {
                Object.DestroyImmediate(box.GetComponent<BoxCollider>());
            }

            SetMaterial(box, material);
            return box;
        }

        private static void SetMaterial(GameObject target, Material material)
        {
            if (material != null && target.TryGetComponent(out Renderer renderer))
            {
                renderer.sharedMaterial = material;
            }
        }

        /// <summary>
        /// Серый блокаутный материал. Заводится ассетом, а не материалом
        /// примитива: встроенный Default-Material не из URP, и в сборке на его
        /// месте оказывается «шейдер потерян» — фиолетовая арена.
        /// </summary>
        private static Material Material(string name, Color color)
        {
            EnsureFolder();
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material TransparentMaterial(string name, Color color)
        {
            Material material = Material(name, new Color(color.r, color.g, color.b, 0.35f));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 0.35f));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(MaterialFolder))
            {
                return;
            }

            string[] parts = MaterialFolder.Split('/');
            string path = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{path}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(path, parts[i]);
                }

                path = next;
            }
        }
    }
}
