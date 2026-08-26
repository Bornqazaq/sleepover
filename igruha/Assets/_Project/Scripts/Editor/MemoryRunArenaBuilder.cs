using UnityEditor;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Core.Player;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Строит арену «Рейса на память» из примитивов: цех, пропасть, тридцать
    /// висящих плит, приподнятую стартовую зону, барьер очереди и выходную
    /// площадку с дверью. Геометрия серая — арт приезжает в фазе 4.
    ///
    /// Всё строится кодом по той же причине, что арены «Экзамена» и цирка:
    /// размеры живут в <see cref="MemoryRunConfig"/>, и пересобрать арену после
    /// правки числа должно быть одним нажатием. Здесь это критичнее обычного —
    /// число шагов названо главным рычагом длительности, и его будут двигать
    /// на плейтесте.
    /// </summary>
    public static class MemoryRunArenaBuilder
    {
        private const string ArenaRoot = "_Arena";
        private const string BoundsRoot = "_Bounds";
        private const float WallThickness = 0.4f;

        /// <summary>Ниже нижней грани плит, чтобы упавший гарантированно вошёл в зону.</summary>
        private const float KillZoneDrop = 2f;

        [MenuItem("Igruha/Рейс на память/Построить арену")]
        public static void Build()
        {
            MemoryRunConfig config = FindConfig();
            if (config == null)
            {
                EditorUtility.DisplayDialog("Рейс на память",
                    "Не найден MemoryRunConfig. Создай его через Create → Igruha → Memory Run Config.", "Ок");
                return;
            }

            if (!VerifyJumpReach(config))
            {
                return;
            }

            ReplaceRoot(ArenaRoot, out Transform arena);
            ReplaceRoot(BoundsRoot, out Transform bounds);

            BuildHall(arena, config);
            BuildPit(arena, config);
            BuildStartZone(arena, config);
            BuildGate(arena, config);
            BuildPlates(arena, config);
            BuildExit(arena, config);
            BuildKillZone(bounds, config);

            Debug.Log(
                $"🧨 Арена «Рейса на память» построена: цех {config.HallWidth:F1}×{config.HallDepth:F1} м, " +
                $"{config.Steps} рядов по {MemoryRunConfig.LaneCount} плиты, " +
                $"самый длинный требуемый прыжок {config.LongestRequiredJump:F2} м",
                arena);

            Selection.activeGameObject = arena.gameObject;
        }

        /// <summary>
        /// Сверяет самый длинный прыжок, который может потребовать маршрут,
        /// с реальной дальностью прыжка персонажа.
        ///
        /// Существует ровно затем, чтобы планировка не могла уехать молча.
        /// В LDD переход лево ↔ право требовал 4.55 м при дальности 4.69 —
        /// запас 3%, и увидеть это в тексте было нельзя, только пересчётом.
        /// Здесь пересчёт делает редактор, каждый раз.
        /// </summary>
        private static bool VerifyJumpReach(MemoryRunConfig config)
        {
            CharacterConfig character = FindCharacterConfig();
            if (character == null)
            {
                Debug.LogWarning("Не найден CharacterConfig — проверку дальности прыжка пропускаю");
                return true;
            }

            float g = Mathf.Abs(Physics.gravity.y);
            float gUp = g * character.RiseGravityMultiplier;
            float gDown = g * character.FallGravityMultiplier;
            if (gUp <= 0f || gDown <= 0f)
            {
                return true;
            }

            float riseTime = character.JumpSpeed / gUp;
            float apex = character.JumpSpeed * character.JumpSpeed / (2f * gUp);
            float fallTime = Mathf.Sqrt(2f * apex / gDown);
            float reach = character.MaxSpeed * (riseTime + fallTime);

            float required = config.LongestRequiredJump;
            float margin = reach / Mathf.Max(0.0001f, required);

            if (margin < 1.5f)
            {
                bool proceed = EditorUtility.DisplayDialog("Рейс на память",
                    $"Планировка требует прыжка на {required:F2} м при дальности {reach:F2} м — запас всего {margin:F2}×.\n\n" +
                    "Механика про память, а не про точность прыжка: на таком запасе часть смертей будет от физики, " +
                    "и игроки сочтут их несправедливыми.\n\nУменьшить зазор между шагами или между полосами.",
                    "Всё равно построить", "Отмена");

                if (!proceed)
                {
                    return false;
                }
            }

            Debug.Log($"Прыжок: дальность {reach:F2} м, самый длинный требуемый {required:F2} м, запас {margin:F1}×");
            return true;
        }

        private static void BuildHall(Transform parent, MemoryRunConfig config)
        {
            float h = config.CeilingHeight;
            float halfW = config.HallWidth * 0.5f;
            float halfD = config.HallDepth * 0.5f;

            // ⚠️ Стены обязаны лежать на Ground: геометрия на Default для камеры
            // прозрачна, и деоклюдер выпустит её наружу (igruha/CLAUDE.md, 2a).
            SetLayer(CreateBox(parent, "Wall_Far", new Vector3(config.HallWidth, h, WallThickness),
                new Vector3(0f, h * 0.5f, halfD), Color.white), "Ground");
            SetLayer(CreateBox(parent, "Wall_Near", new Vector3(config.HallWidth, h, WallThickness),
                new Vector3(0f, h * 0.5f, -halfD), Color.white), "Ground");
            SetLayer(CreateBox(parent, "Wall_Left", new Vector3(WallThickness, h, config.HallDepth),
                new Vector3(-halfW, h * 0.5f, 0f), Color.white), "Ground");
            SetLayer(CreateBox(parent, "Wall_Right", new Vector3(WallThickness, h, config.HallDepth),
                new Vector3(halfW, h * 0.5f, 0f), Color.white), "Ground");
        }

        /// <summary>
        /// Дно пропасти. Идёт <b>во всю ширину цеха</b>, а не только под цепочкой:
        /// оставь по бокам пол, и маршрут обходится пешком по краю, минуя все
        /// тридцать плит разом.
        /// </summary>
        private static void BuildPit(Transform parent, MemoryRunConfig config)
        {
            float from = config.GateZ;
            float to = config.ExitPadZ;
            float depth = to - from;

            var pit = CreateBox(parent, "PitFloor",
                new Vector3(config.HallWidth, WallThickness, depth),
                new Vector3(0f, -config.PitDepth, from + depth * 0.5f),
                new Color(0.10f, 0.10f, 0.12f));
            SetLayer(pit, "Ground");
        }

        /// <summary>
        /// Площадка ожидания. Приподнята: при восьмерых стоящие сзади должны
        /// видеть плиты поверх голов передних.
        ///
        /// Идёт от стены до стены и глубже номинальных 14 ШИ. Четырнадцать — это
        /// игровая зона; платформа шире по двум причинам сразу. По бокам иначе
        /// остаются щели, и первый же вытолкнутый из драки улетает в пропасть,
        /// получив смерть ни за что. А позади нужен запас на отход камеры —
        /// без него стоящий у задней стенки получает камеру в затылок.
        /// </summary>
        private static void BuildStartZone(Transform parent, MemoryRunConfig config)
        {
            float depth = config.CameraClearance + config.StartZoneSize;
            float centerZ = config.NearEdgeZ + depth * 0.5f;
            float lift = config.StartZoneLift;

            var floor = CreateBox(parent, "StartZone",
                new Vector3(config.HallWidth, WallThickness, depth),
                new Vector3(0f, lift - WallThickness * 0.5f, centerZ),
                new Color(0.62f, 0.66f, 0.70f));
            SetLayer(floor, "Ground");

            // Метка для расстановки точек спавна и для возврата погибших.
            var marker = new GameObject("StartPoint");
            marker.transform.SetParent(parent, false);
            marker.transform.position = new Vector3(
                0f, lift, config.GateZ - config.StartZoneSize * 0.5f);
        }

        /// <summary>
        /// Барьер очереди: пропускает только того, чей сейчас ход, остальные
        /// упираются. Без него кто-нибудь столкнул бы идущего в пропасть, и
        /// информация о шаге пропала бы — а вся игра на том, что информация
        /// копится честно.
        ///
        /// Лежит на <c>PlayerBarrier</c> — это слой сплошной геометрии из маски
        /// камеры, то есть камера через барьер не пройдёт.
        /// </summary>
        private static void BuildGate(Transform parent, MemoryRunConfig config)
        {
            var gate = CreateBox(parent, "TurnGate",
                new Vector3(config.HallWidth, config.GateHeight, WallThickness),
                new Vector3(0f, config.StartZoneLift + config.GateHeight * 0.5f, config.GateZ),
                new Color(0.75f, 0.62f, 0.20f));
            SetLayer(gate, "PlayerBarrier");
        }

        /// <summary>
        /// Тридцать плит. Верхняя грань каждой — на нуле, чтобы прыжок между
        /// рядами шёл строго по горизонтали и все переходы были одинаковыми.
        /// </summary>
        private static void BuildPlates(Transform parent, MemoryRunConfig config)
        {
            var root = new GameObject("Plates");
            root.transform.SetParent(parent, false);

            for (int step = 0; step < config.Steps; step++)
            {
                for (int lane = 0; lane < MemoryRunConfig.LaneCount; lane++)
                {
                    var plate = CreateBox(root.transform, $"Plate_{step:00}_{lane}",
                        new Vector3(config.PlateSize, config.PlateThickness, config.PlateSize),
                        new Vector3(config.LaneX(lane), -config.PlateThickness * 0.5f, config.StepZ(step)),
                        new Color(0.55f, 0.56f, 0.58f));
                    SetLayer(plate, "Ground");
                }
            }
        }

        /// <summary>Сплошная безопасная площадка и дверь: понятная цель, видимая от первого ряда.</summary>
        private static void BuildExit(Transform parent, MemoryRunConfig config)
        {
            float centerZ = config.ExitPadZ + config.ExitPadDepth * 0.5f;

            var pad = CreateBox(parent, "ExitPad",
                new Vector3(config.HallWidth, WallThickness, config.ExitPadDepth),
                new Vector3(0f, -WallThickness * 0.5f, centerZ),
                new Color(0.58f, 0.62f, 0.58f));
            SetLayer(pad, "Ground");

            float doorHeight = 3f * config.UnitsPerWidth;
            float doorWidth = 2.4f * config.UnitsPerWidth;
            var door = CreateBox(parent, "ExitDoor",
                new Vector3(doorWidth, doorHeight, WallThickness),
                new Vector3(0f, doorHeight * 0.5f, config.HallDepth * 0.5f - WallThickness),
                new Color(0.30f, 0.33f, 0.36f));
            SetLayer(door, "Ground");

            var lamp = CreateBox(parent, "ExitLamp",
                new Vector3(doorWidth * 0.3f, 0.15f, 0.15f),
                new Vector3(0f, doorHeight + 0.3f, config.HallDepth * 0.5f - WallThickness),
                new Color(0.25f, 0.95f, 0.35f));
            SetLayer(lamp, "Ground");

            // Триггер прохода — на Default: маска камеры этот слой не включает,
            // иначе камера начала бы цепляться за пустоту (igruha/CLAUDE.md, 2a).
            var finish = new GameObject("ExitTrigger");
            finish.transform.SetParent(parent, false);
            finish.transform.position = new Vector3(0f, doorHeight * 0.5f, centerZ);
            var box = finish.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(config.HallWidth, doorHeight, config.ExitPadDepth);
        }

        /// <summary>
        /// Зона падения. Режим <c>EventOnly</c>: что делать с упавшим, решают
        /// правила игры — здесь падение это конец хода и засчитанная смерть,
        /// а не молчаливый респаун.
        /// </summary>
        private static void BuildKillZone(Transform parent, MemoryRunConfig config)
        {
            var zone = new GameObject("KillZone_Bottom");
            zone.transform.SetParent(parent, false);
            zone.transform.position = new Vector3(0f, -config.PitDepth + KillZoneDrop, 0f);

            var box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(config.HallWidth, 1f, config.HallDepth);

            var kill = zone.AddComponent<KillZone>();
            var so = new SerializedObject(kill);
            SerializedProperty mode = so.FindProperty("mode");
            if (mode != null)
            {
                mode.enumValueIndex = (int)KillZone.ZoneMode.EventOnly;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ReplaceRoot(string name, out Transform root)
        {
            var existing = GameObject.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            root = new GameObject(name).transform;
        }

        private static GameObject CreateBox(Transform parent, string name, Vector3 size, Vector3 position, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;

            // ⚠️ CreatePrimitive вешает встроенный Default-Material, шейдер
            // которого не из URP и в сборку не попадает: в билде объект стал бы
            // фиолетовым, хотя в редакторе выглядит нормально (STATE 3.9).
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetBlockoutMaterial(color);
            }

            return go;
        }

        private static Material blockoutMaterial;

        private static Material GetBlockoutMaterial(Color color)
        {
            if (blockoutMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogError("Шейдер 'Universal Render Pipeline/Lit' не найден — блокаут будет фиолетовым в сборке");
                    return null;
                }

                blockoutMaterial = new Material(shader);
            }

            return new Material(blockoutMaterial) { color = color };
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

        private static MemoryRunConfig FindConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:MemoryRunConfig");
            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<MemoryRunConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
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
