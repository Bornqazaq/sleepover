using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Core.Player;
using Igruha.Core.UI;
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

            MemoryRunDress.Begin();
            MemoryRunPalette.Begin();

            ReplaceRoot(ArenaRoot, out Transform arena);
            ReplaceRoot(BoundsRoot, out Transform bounds);

            BuildHall(arena, config);
            BuildPit(arena, config);
            BuildStartZone(arena, config);
            BuildGate(arena, config);
            BuildPlates(arena, config);
            BuildExit(arena, config);
            BuildKillZone(bounds, config);
            MemoryRunEnvironment.Build(arena, config);
            GameObject manager = GameObject.Find("MinigameManager");
            MemoryRunVfx.Build(arena, manager);
            MemoryRunSfx.Build(arena, config, manager);
            EnsureHudStatusLine();

            MemoryRunPalette.Flush();

            Debug.Log(MemoryRunDress.Report(), arena);
            Debug.Log(MemoryRunPalette.Report(), arena);
            Debug.Log(MemoryRunEnvironment.Report(), arena);
            Debug.Log(MemoryRunVfx.Report(), arena);
            Debug.Log(MemoryRunSfx.Report(), arena);
            ReportMissingModels();

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
            Material wall = MemoryRunPalette.Get(MemoryRunPalette.Tone.Wall);
            SetLayer(CreateBox(parent, "Wall_Far", new Vector3(config.HallWidth, h, WallThickness),
                new Vector3(0f, h * 0.5f, halfD), wall), "Ground");
            SetLayer(CreateBox(parent, "Wall_Near", new Vector3(config.HallWidth, h, WallThickness),
                new Vector3(0f, h * 0.5f, -halfD), wall), "Ground");
            SetLayer(CreateBox(parent, "Wall_Left", new Vector3(WallThickness, h, config.HallDepth),
                new Vector3(-halfW, h * 0.5f, 0f), wall), "Ground");
            SetLayer(CreateBox(parent, "Wall_Right", new Vector3(WallThickness, h, config.HallDepth),
                new Vector3(halfW, h * 0.5f, 0f), wall), "Ground");
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
                MemoryRunPalette.Get(MemoryRunPalette.Tone.Pit));
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
                MemoryRunPalette.Get(MemoryRunPalette.Tone.Deck));
            SetLayer(floor, "Ground");
            MemoryRunDress.Apply(floor, MemoryRunDress.Kind.Deck);
            BuildPitRim(parent, config, config.GateZ - RimWidth * 0.5f, lift);

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
        /// <b>Барьер обязан быть прозрачным и обязан пропускать камеру.</b>
        /// Глухим и на <c>PlayerBarrier</c> он ломает мини-игру целиком, и это
        /// найдено ручным прогоном 31.08: при высоте 2.16 м верхняя грань стоит
        /// на 2.88 м, глаза стоящего в поднятой стартовой зоне — на 2.32 м.
        /// Ждущие не видели ни плит, ни идущего — только жёлтую стену, — а вся
        /// игра построена на «смотри и запоминай, пока идут другие». Стартовая
        /// зона поднята на 1 ШИ ровно ради обзора (спека, раздел 3), и глухой
        /// барьер этот подъём обесценивал.
        ///
        /// Слой <c>Ignore Raycast</c>, а не <c>PlayerBarrier</c>: барьер должен
        /// останавливать тело и пропускать камеру. На <c>PlayerBarrier</c> он
        /// входит в маску деокклюдера (`igruha/CLAUDE.md`, 2a), и камера
        /// каждого, кто подошёл посмотреть, ныряла ему в затылок — а подходят
        /// смотреть все и всегда. Наружу камера при этом не выходит: за
        /// стартовой зоной стоит <c>Wall_Near</c> на <c>Ground</c>. Тот же
        /// приём уже применён к барьеру стола в «Верю / не верю» (спека 4.5)
        /// и к внешней стене клетки в «Порядке банок» (STATE 3.7).
        ///
        /// Проходимость барьера при этом не меняется ничем: <see cref="TurnGate"/>
        /// снимает коллизию адресно через <c>Physics.IgnoreCollision</c>,
        /// а она от слоя не зависит.
        /// </summary>
        private static void BuildGate(Transform parent, MemoryRunConfig config)
        {
            var gate = CreateBox(parent, "TurnGate",
                new Vector3(config.HallWidth, config.GateHeight, WallThickness),
                new Vector3(0f, config.StartZoneLift + config.GateHeight * 0.5f, config.GateZ),
                MemoryRunPalette.Get(MemoryRunPalette.Tone.Gate));

            SetLayer(gate, "Ignore Raycast");
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
                        MemoryRunPalette.Get(MemoryRunPalette.Tone.Plate));
                    SetLayer(plate, "Ground");

                    // 🔴 Все тридцать одеваются одним мешем и одним материалом,
                    // без поворота и без вариаций. Это правило игры, а не вкус:
                    // любая примета на плите заменяет память приметой.
                    MemoryRunDress.ApplyPlate(plate);
                }
            }

            MemoryRunDress.BuildRowStructure(root.transform, config);
        }

        /// <summary>Сплошная безопасная площадка и дверь: понятная цель, видимая от первого ряда.</summary>
        private static void BuildExit(Transform parent, MemoryRunConfig config)
        {
            float centerZ = config.ExitPadZ + config.ExitPadDepth * 0.5f;

            var pad = CreateBox(parent, "ExitPad",
                new Vector3(config.HallWidth, WallThickness, config.ExitPadDepth),
                new Vector3(0f, -WallThickness * 0.5f, centerZ),
                MemoryRunPalette.Get(MemoryRunPalette.Tone.Deck));
            SetLayer(pad, "Ground");
            MemoryRunDress.Apply(pad, MemoryRunDress.Kind.Deck);
            BuildPitRim(parent, config, config.ExitPadZ + RimWidth * 0.5f, 0f);

            float doorHeight = 3f * config.UnitsPerWidth;
            float doorWidth = 2.4f * config.UnitsPerWidth;
            var door = CreateBox(parent, "ExitDoor",
                new Vector3(doorWidth, doorHeight, WallThickness),
                new Vector3(0f, doorHeight * 0.5f, config.HallDepth * 0.5f - WallThickness),
                MemoryRunPalette.Get(MemoryRunPalette.Tone.Door));
            SetLayer(door, "Ground");
            MemoryRunDress.Apply(door, MemoryRunDress.Kind.ExitDoor);

            var lamp = CreateBox(parent, "ExitLamp",
                new Vector3(doorWidth * 0.3f, 0.15f, 0.15f),
                new Vector3(0f, doorHeight + 0.3f, config.HallDepth * 0.5f - WallThickness),
                MemoryRunPalette.Get(MemoryRunPalette.Tone.ExitLamp));
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

        /// <summary>
        /// Назвать модели, которых не оказалось в проекте. Паки Synty каждый
        /// ставит себе сам и в репозиторий не кладутся, поэтому промах пути
        /// обязан быть громким: молча пропущенная модель выглядит в сцене как
        /// пустое место, а не как ошибка.
        /// </summary>
        private static void ReportMissingModels()
        {
            IReadOnlyList<string> missing = MemoryRunDress.Missing;
            if (missing.Count == 0)
            {
                return;
            }

            var text = new StringBuilder("Модели не найдены — пересборка прошла с замечаниями:");
            for (int i = 0; i < missing.Count; i++)
            {
                text.Append("\n— ").Append(missing[i]);
            }

            Debug.LogWarning(text.ToString());
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

        /// <summary>
        /// Коробка блокаута с материалом палитры.
        ///
        /// ⚠️ Материал <b>обязателен</b>: <c>CreatePrimitive</c> вешает
        /// встроенный Default-Material, шейдер которого не из URP и в сборку не
        /// попадает — в билде объект стал бы фиолетовым, хотя в редакторе
        /// выглядит нормально (STATE 3.9).
        ///
        /// Материал приходит <b>ассетом с диска</b>, а не создаётся здесь. До
        /// подфазы 4.2 он создавался на лету, уезжал в YAML сцены и множился
        /// при каждой пересборке; настроить тон в таком виде было негде.
        /// </summary>
        private static GameObject CreateBox(Transform parent, string name, Vector3 size, Vector3 position,
            Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }

            return go;
        }

        /// <summary>Ширина предупреждающей кромки вдоль края провала.</summary>
        private const float RimWidth = 0.6f;

        /// <summary>
        /// Кромка провала: красная полоса вдоль обоих его краёв.
        ///
        /// Плоский примитив, десяток треугольников. Единственное место в игре,
        /// где живёт предупреждающий красный: на плитах его быть не может по
        /// правилу неразличимости, а других опасных объектов здесь нет.
        ///
        /// Коллайдера у полосы нет и слой у неё <c>Default</c>: она лежит на
        /// самом краю площадки, и лишняя поверхность там ловила бы шаг перед
        /// прыжком.
        ///
        /// <paramref name="z"/> — центр полосы, и он обязан лежать <b>на
        /// площадке</b>, а не за её краем. Первая версия отмеряла полширины не
        /// в ту сторону на обоих краях, и обе полосы висели в воздухе над
        /// пропастью: числами это не ловится ничем — ни аудит дресса, ни
        /// проверка зоны выбывания сюда не смотрят, только глаз на кадре.
        /// </summary>
        private static void BuildPitRim(Transform parent, MemoryRunConfig config, float z, float surfaceY)
        {
            var rim = CreateBox(parent, "PitRim",
                new Vector3(config.HallWidth, 0.06f, RimWidth),
                new Vector3(0f, surfaceY + 0.01f, z),
                MemoryRunPalette.Get(MemoryRunPalette.Tone.PitRim));

            Object.DestroyImmediate(rim.GetComponent<Collider>());
            rim.GetComponent<Renderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// Дать HUD строку статуса, если её нет.
        ///
        /// Найдено ручным прогоном 31.08: <c>MemoryRunLocalHud</c> каждый кадр
        /// собирает строку «Ход: имя — 37 с · очередь · Попыток: 2 / 10» и
        /// отдаёт её в <c>RoundHud.ShowStatus</c>, а тот с пустой ссылкой
        /// молча выходит. То есть чей ход, сколько осталось на ход, очередь и
        /// свой счётчик попыток не выводились **ни разу за всё время**.
        ///
        /// Ровно тот же класс и ровно та же причина, что в «Верю / не верю»
        /// 25.08 (STATE 3.14): поле <c>statusText</c> в шаблоне сцены пустое,
        /// а пустое место на экране выглядит как «нечего показывать», а не как
        /// поломка. Глазами такое не ловится, только чтением поля.
        ///
        /// ⚠️ Холст берётся <b>от самого HUD</b>, а не поиском первого попавшегося
        /// <c>Canvas</c> в сцене. Поиск первого — это и есть та поломка, которая
        /// 28.08 сделала «Верю / не верю» нечитаемой целиком (STATE 3.26):
        /// в сцене нашёлся мировой холст пузыря реплики, и весь интерфейс игры
        /// уехал внутрь него зеркальными буквами в сантиметр.
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

            // Под таймером раунда, а не поверх него: TimerText стоит на -30 при
            // высоте 80, то есть занимает полосу до -110. Первая версия строки
            // встала на -52 и легла ровно на цифры таймера — видно на кадре
            // прогона, читались обе строки плохо.
            rect.anchoredPosition = new Vector2(0f, -118f);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = 24f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Top;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;

            // Обводка, а не подложка. Строка висит над ареной, а она светлая:
            // серый текст на ней читался с трудом. Подложку сюда поставить
            // нельзя — RoundHud.ShowStatus гасит именно объект текста, и
            // отдельная панель осталась бы висеть пустой плашкой.
            text.outlineColor = new Color32(0, 0, 0, 210);
            text.outlineWidth = 0.2f;

            property.objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("HUD получил строку статуса: чей ход, таймер хода, очередь и счётчик попыток", text);
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
