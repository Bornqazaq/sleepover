using TMPro;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Minigames.Exam;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Строит арену «Экзамена» из примитивов: зал, две платформы со створками,
    /// кафедру, доску и зону возврата. Геометрия серая — арт приезжает в фазе 4.
    ///
    /// Всё строится кодом, а не руками, по той же причине, что и цирк:
    /// размеры живут в <see cref="ExamConfig"/>, и пересобрать арену после
    /// правки числа должно быть одним нажатием.
    /// </summary>
    public static class ExamArenaBuilder
    {
        private const string Root = "_Arena";
        private const float WallThickness = 0.4f;
        private const float BoardThickness = 0.2f;

        /// <summary>Поле от края доски до текста, в метрах.</summary>
        private const float BoardMargin = 0.18f;

        /// <summary>Мировой размер одного юнита холста доски: 1 юнит = 1 см.</summary>
        private const float BoardUnitScale = 0.01f;

        /// <summary>Насколько камера Ведущего поднята над кафедрой, в метрах.</summary>
        private const float PodiumCameraLift = 2.35f;

        /// <summary>Насколько камера Ведущего отставлена от кафедры в зал, в метрах.</summary>
        private const float PodiumCameraDistance = 7.2f;

        /// <summary>Сдвиг камеры Ведущего вбок — ракурс в три четверти, в метрах.</summary>
        private const float PodiumCameraSide = 2.6f;

        /// <summary>Наклон камеры Ведущего вниз, в градусах.</summary>
        private const float PodiumCameraPitch = 4f;

        /// <summary>Доворот камеры Ведущего на кафедру, в градусах.</summary>
        private const float PodiumCameraYaw = -19f;

        /// <summary>
        /// Насколько камера зала отставлена за ближний край платформ, в метрах.
        ///
        /// Число не на глаз: у фиксированного рига поле зрения 40°, то есть
        /// по горизонтали 65.8° при 16:9, полуугол 32.9° и тангенс 0.647.
        /// Крайняя точка площадок теперь ±6.48 м, значит камере нужно
        /// 6.48 / 0.647 = 10.0 м до их центра; 9.2 за ближним краем дают
        /// 11.36 — кадр с полями. До задней стены при этом остаётся 1.6 м.
        ///
        /// Было 11.5 при площадках ±9.36: после того как зал ужали, прежнее
        /// число отодвинуло бы камеру в стену и добавило бы в кадр ровно то,
        /// от чего избавлялись, — пустой пол перед площадками.
        /// </summary>
        private const float HallCameraDistance = 9.2f;

        /// <summary>Насколько камера зала опущена под потолок, в метрах.</summary>
        private const float HallCameraDrop = 0.6f;

        /// <summary>Наклон камеры зала вниз, в градусах.</summary>
        private const float HallCameraPitch = 12.5f;

        /// <summary>Яркость ламп класса.</summary>
        private const float LampIntensity = 4f;

        /// <summary>Запас в яме по бокам под размах створок, в метрах.</summary>
        private const float DoorSwingClearance = 1.6f;

        /// <summary>На сколько верх стенок ямы утоплен под пол зала, в метрах.</summary>
        private const float PitWallSink = 0.05f;

        /// <summary>На сколько буква варианта поднята над створками, в метрах.</summary>
        private const float LetterLift = 0.06f;

        /// <summary>Логический размер холста с буквой — в мир его переводит масштаб.</summary>
        private const float LetterCanvasUnits = 400f;

        /// <summary>Какую долю меньшей стороны платформы занимает буква.</summary>
        private const float LetterFitFactor = 0.55f;

        /// <summary>
        /// Насколько буква на полу разбелена относительно цвета варианта.
        ///
        /// Была затемнена на четверть — так она не спорила со светлым
        /// блокаутом площадки. Дресс 4.1 настелил площадку тёмным деревом,
        /// и тёмно-синяя буква на тёмно-коричневом настиле перестала читаться
        /// вовсе: рендер приёмки показал «Б» едва различимой. Теперь буква
        /// светлее фона, а не темнее, и цвет варианта в ней сохраняется.
        /// </summary>
        private const float LetterFloorLift = 0.4f;

        /// <summary>Доля цвета варианта в покрытии платформы — намёк, а не заливка.</summary>
        private const float PlatformTint = 0.3f;

        /// <summary>Толщина рамки доски, в метрах.</summary>
        private const float BoardFrameThickness = 0.12f;

        /// <summary>
        /// Доля ширины зала, которую занимает доска.
        ///
        /// Три числа доски (ширина, высота, центр) раньше стояли константами
        /// прямо в двух методах — в самой доске и в её рамке. Разъехаться им
        /// мешало только внимание: рамка считалась по своей копии тех же
        /// множителей. Теперь множитель один на оба места.
        /// </summary>
        private const float BoardWidthFactor = 0.5f;

        /// <summary>Доля высоты зала, которую занимает доска.</summary>
        private const float BoardHeightFactor = 0.44f;

        /// <summary>
        /// На какой доле высоты зала стоит центр доски.
        ///
        /// Поднято с 0.56 после рендера ужатого зала: нижняя строка доски —
        /// вариант Б — оказалась ровно за монитором Ведущего. Это арифметика
        /// параллакса, а не случайность. Камера зала подошла к площадкам
        /// на два метра ближе, угол на доску стал круче, и луч к нижней строке
        /// пошёл через ту высоту, на которой стоит монитор. Строку варианта
        /// нельзя закрывать ничем: по ней Ученик и выбирает, куда бежать.
        ///
        /// Лечится с двух сторон сразу — доска выше, тумба Ведущего ниже
        /// (см. <see cref="HostDeskHeight"/>): запас между лучом и верхом
        /// монитора выходит 27 см вместо минус пяти.
        /// </summary>
        private const float BoardCenterFactor = 0.6f;

        /// <summary>Высота тумбы Ведущего, м. Ниже монитор не опустить — он стоит на ней.</summary>
        private const float HostDeskHeight = 0.85f;

        /// <summary>Высота парты, в метрах.</summary>
        private const float DeskHeight = 0.75f;

        /// <summary>
        /// Сколько парт вдоль каждой боковой стены.
        ///
        /// Было три, и они кучкой стояли у зоны возврата: на рендере приёмки
        /// это читалось не классом, а шестью стульями, забытыми посреди
        /// спортзала. Пять коробок по два места растягивают колонну на всю
        /// длину площадки — боковой неф становится рядом парт, смотрящих
        /// на доску, то есть тем, чем в классе и должен быть.
        /// </summary>
        private const int DesksPerSide = 5;

        /// <summary>Отступ ЦЕНТРА колонны парт от боковой стены, в метрах.</summary>
        private const float DeskWallGap = 1.4f;

        /// <summary>Z ближайшей к доске парты, в метрах.</summary>
        private const float DeskFirstZ = 4.2f;

        /// <summary>Шаг между партами, в метрах.</summary>
        private const float DeskSpacing = 1.55f;

        /// <summary>Ширина коробки парты по X: два места рядом.</summary>
        private const float DeskWidth = 1.5f;

        /// <summary>Глубина коробки парты по Z — столешница вместе со скамьёй.</summary>
        private const float DeskDepth = 0.95f;

        /// <summary>
        /// Насколько низ подвеса указателя утоплен под потолок, в метрах.
        ///
        /// Было абсолютное 3.9 при потолке 5.76. Потолок опустился до 4.68,
        /// и абсолютное число оставило бы указателю 0.13 м подвеса — табличка
        /// висела бы приклеенной к перекрытию. Считаем от потолка: подвес
        /// всегда читается верёвкой, какой бы ни стала высота зала.
        /// </summary>
        private const float SignDropFromCeiling = 1f;

        /// <summary>Высота самой таблички указателя, в метрах.</summary>
        private const float SignPlateHeight = 0.95f;

        /// <summary>Ширина таблички указателя, в метрах.</summary>
        private const float SignPlateWidth = 1.8f;

        /// <summary>
        /// Насколько указатель отодвинут от внутреннего края своей площадки
        /// к внешнему, в долях её ширины.
        ///
        /// Висел строго над центром площадки — и в ужатом зале обе таблички
        /// встали ровно перед доской: с камеры зала «А» и «Б» закрывали текст
        /// вопроса, то есть указатель отнимал ровно то, ради чего сам и висит.
        /// 0.78 уводит их к боковым стенам, оставляя середину кадра доске,
        /// и при этом каждая табличка остаётся над своей площадкой.
        /// </summary>
        private const float SignOutwardFactor = 0.78f;

        /// <summary>Зерно генератора дресса. Фиксировано: пересборка обязана давать ту же арену.</summary>
        private const int DressSeed = 20260904;

        [MenuItem("Igruha/Экзамен/Построить арену")]
        public static void Build()
        {
            var config = FindConfig();
            if (config == null)
            {
                EditorUtility.DisplayDialog("Экзамен",
                    "Не найден ExamConfig. Создай его через Create → Igruha → Exam Config.", "Ок");
                return;
            }

            var existing = GameObject.Find(Root);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            var root = new GameObject(Root);

            // У дресса свой генератор случайных чисел. Общий с билдером сдвинул
            // бы последовательность, по которой раскладываются геймплейные
            // объекты, и проверенная планировка поехала бы от смены модели.
            var dressRandom = new System.Random(DressSeed);
            ExamDress.Begin();
            ExamPalette.Begin();

            // Раскладка по глубине от дальней стены: кафедра → проход →
            // платформы → проход → зона возврата → запас для камеры.
            float depth = config.HallDepth;
            float far = depth * 0.5f;

            float podiumZ = far - config.PodiumDepth * 0.5f;
            float platformsZ = far - config.PodiumDepth - 2.16f - config.PlatformDepth * 0.5f;
            float returnZ = platformsZ - config.PlatformDepth * 0.5f - config.ReturnToPlatformGap - config.ReturnZoneDepth * 0.5f;

            BuildHall(root.transform, config, platformsZ);
            BuildPit(root.transform, config, platformsZ);
            BuildPlatform(root.transform, config, ExamSide.A, platformsZ);
            BuildPlatform(root.transform, config, ExamSide.B, platformsZ);
            BuildGapFloor(root.transform, config, platformsZ);
            BuildPodium(root.transform, config, podiumZ);
            BuildHallCamera(root.transform, config, platformsZ);
            BuildBoard(root.transform, config, far);
            BuildReturnZone(root.transform, config, returnZ);
            BuildDecor(root.transform, config, platformsZ, podiumZ, far);

            // Арт строится той же пересборкой, что и блокаут: всё, что не
            // воспроизводится ею, теряется при первом слиянии веток — YAML
            // сцены слияние не переживает.
            ExamDress.Build(root, config, dressRandom);
            ExamSurfaces.Apply(root, config, dressRandom);
            ExamEnvironment.Build(root, config, dressRandom);
            ExamVfx.Build(root, config);
            ExamPalette.Flush();

            // Физика декора — последним шагом сборки. Дресс срезает коллайдеры
            // моделей, и всё, что поставлено в зал само по себе, без коробки
            // блокаута, до этого шага проходилось насквозь.
            PropColliders.Build(root);

            // Оформление интерфейса — тем же прогоном: иначе пересборка арены
            // вернула бы серые прямоугольники шаблона.
            UiSkinPass.Apply();

            WireMinigameReferences(root);

            Debug.Log($"📚 Арена «Экзамена» построена: зал {config.HallWidth:F1}×{config.HallDepth:F1} м, " +
                      $"платформы {config.PlatformWidth:F1}×{config.PlatformDepth:F1} м", root);
            Debug.Log(ExamDress.Report(), root);
            Debug.Log(ExamPalette.Report(), root);
            Debug.Log(ExamEnvironment.Report(), root);
            Debug.Log(ExamVfx.Report(), root);
            Debug.Log(ExamSfx.Build(root, config), root);

            Selection.activeGameObject = root;
        }

        /// <summary>
        /// Зал: пол, четыре стены и потолок.
        ///
        /// ⚠️ <b>Пол — четыре плиты вокруг проёма, а не одна на весь зал.</b>
        /// Сплошной плитой он стоил живого прогона дважды: створки платформ
        /// исправно распахивались, но под ними оставался тот же пол зала —
        /// провалиться было физически некуда, и неверный ответ ничего не стоил.
        /// Вдобавок верхняя грань пола и верхняя грань створок лежали на одной
        /// высоте, и вся площадка мерцала z-fighting'ом.
        ///
        /// Потолок обязателен: без него в классе видно небо, зал освещается
        /// скайбоксом и читается как коробка без крыши.
        /// </summary>
        /// <summary>
        /// Перецепить ссылки контроллера на только что построенную арену.
        ///
        /// Обязательно, потому что пункт меню сносит <c>_Arena</c> целиком:
        /// без этого после каждой пересборки контроллер держал бы ссылки
        /// на уничтоженные объекты, и матч падал бы на первом же вопросе —
        /// молча, потому что все поля в инспекторе выглядят заполненными.
        /// </summary>
        private static void WireMinigameReferences(GameObject root)
        {
            var minigame = Object.FindFirstObjectByType<ExamMinigame>(FindObjectsInactive.Include);
            if (minigame == null)
            {
                Debug.LogWarning("⚠️ ExamMinigame в сцене не найден — ссылки на арену перецепить некому");
                return;
            }

            var so = new SerializedObject(minigame);
            SetReference(so, "platformA", FindIn<ExamAnswerPlatform>(root, "Platform_A"));
            SetReference(so, "platformB", FindIn<ExamAnswerPlatform>(root, "Platform_B"));
            SetReference(so, "board", FindIn<ExamBoard>(root, "BoardCanvas"));
            SetReference(so, "podiumStand", FindTransform(root, "HostStand"));
            SetReference(so, "returnZone", FindTransform(root, "ReturnPoint"));
            SetReference(so, "podiumCameraRig", FindTransform(root, "PodiumCameraRig"));
            SetReference(so, "hallCameraRig", FindTransform(root, "HallCameraRig"));
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(minigame);
        }

        private static void SetReference(SerializedObject so, string property, Object value)
        {
            SerializedProperty found = so.FindProperty(property);
            if (found == null)
            {
                Debug.LogWarning($"⚠️ У ExamMinigame нет поля '{property}' — ссылка не перецеплена");
                return;
            }

            if (value == null)
            {
                Debug.LogWarning($"⚠️ На арене не найден объект для поля '{property}'");
                return;
            }

            found.objectReferenceValue = value;
        }

        private static T FindIn<T>(GameObject root, string childName) where T : Component
        {
            foreach (var candidate in root.GetComponentsInChildren<T>(true))
            {
                if (candidate.name == childName)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Transform FindTransform(GameObject root, string childName)
        {
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == childName)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void BuildHall(Transform parent, ExamConfig config, float platformsZ)
        {
            float h = config.CeilingHeight;
            float halfW = config.HallWidth * 0.5f;
            float halfD = config.HallDepth * 0.5f;

            // Проём в полу — обе платформы вместе с зазором между ними.
            float openingHalfWidth = (config.PlatformWidth * 2f + config.PlatformGap) * 0.5f;
            float openingDepth = config.PlatformDepth;
            float nearEdge = platformsZ - openingDepth * 0.5f;
            float farEdge = platformsZ + openingDepth * 0.5f;

            var floorColor = new Color(0.72f, 0.68f, 0.62f);

            float farStrip = halfD - farEdge;
            SetLayer(CreateBox(parent, "Floor_Far", new Vector3(config.HallWidth, WallThickness, farStrip),
                new Vector3(0f, -WallThickness * 0.5f, farEdge + farStrip * 0.5f), floorColor), "Ground");

            float nearStrip = nearEdge + halfD;
            SetLayer(CreateBox(parent, "Floor_Near", new Vector3(config.HallWidth, WallThickness, nearStrip),
                new Vector3(0f, -WallThickness * 0.5f, nearEdge - nearStrip * 0.5f), floorColor), "Ground");

            float sideWidth = halfW - openingHalfWidth;
            SetLayer(CreateBox(parent, "Floor_Left", new Vector3(sideWidth, WallThickness, openingDepth),
                new Vector3(-openingHalfWidth - sideWidth * 0.5f, -WallThickness * 0.5f, platformsZ), floorColor), "Ground");
            SetLayer(CreateBox(parent, "Floor_Right", new Vector3(sideWidth, WallThickness, openingDepth),
                new Vector3(openingHalfWidth + sideWidth * 0.5f, -WallThickness * 0.5f, platformsZ), floorColor), "Ground");

            SetLayer(CreateBox(parent, "Ceiling", new Vector3(config.HallWidth, WallThickness, config.HallDepth),
                new Vector3(0f, h + WallThickness * 0.5f, 0f), new Color(0.86f, 0.86f, 0.88f)), "Ground");

            BuildCeilingLights(parent, config);

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
        /// Лампы класса. Появились вместе с потолком: он отсекает направленный
        /// свет, и без ламп зал держится на одном ambient — плоско и темно.
        /// </summary>
        private static void BuildCeilingLights(Transform parent, ExamConfig config)
        {
            var lights = new GameObject("CeilingLights");
            lights.transform.SetParent(parent, false);

            float x = config.HallWidth * 0.25f;
            float z = config.HallDepth * 0.25f;
            float y = config.CeilingHeight - 0.4f;

            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Lamp_" + (i + 1));
                go.transform.SetParent(lights.transform, false);
                go.transform.localPosition = new Vector3(i < 2 ? -x : x, y, i % 2 == 0 ? -z : z);

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = config.HallDepth * 0.6f;
                light.intensity = LampIntensity;
                light.color = new Color(1f, 0.96f, 0.88f);
                light.shadows = LightShadows.None;
            }
        }

        /// <summary>Яма под платформами: туда улетают те, кто выбрал неверно.</summary>
        private static void BuildPit(Transform parent, ExamConfig config, float platformsZ)
        {
            // Яма шире проёма: раскрываясь на 110°, створка заваливается
            // за вертикаль и уходит наружу от своей петли. Впритык к проёму
            // она втыкалась бы в стенку ямы у всех на глазах.
            float width = config.PlatformWidth * 2f + config.PlatformGap + DoorSwingClearance * 2f;
            var pitColor = new Color(0.12f, 0.12f, 0.14f);
            var pit = CreateBox(parent, "PitFloor", new Vector3(width, WallThickness, config.PlatformDepth),
                new Vector3(0f, -config.PitDepth, platformsZ), pitColor);
            SetLayer(pit, "Ground");

            // Стенки ямы. Без них сквозь открытый проём видно пустоту за
            // пределами зала, а упавший укатывается под пол.
            // Верх стенок уходит чуть ниже пола: вровень с ним они снова дают
            // ту же полосу z-fighting'а, из-за которой мерцали площадки.
            // Щели не будет — пол зала толще этого запаса.
            float depth = config.PlatformDepth;
            float wallHeight = config.PitDepth - PitWallSink;
            float wallY = -PitWallSink - wallHeight * 0.5f;

            SetLayer(CreateBox(parent, "PitWall_Far", new Vector3(width, wallHeight, WallThickness),
                new Vector3(0f, wallY, platformsZ + depth * 0.5f), pitColor), "Ground");
            SetLayer(CreateBox(parent, "PitWall_Near", new Vector3(width, wallHeight, WallThickness),
                new Vector3(0f, wallY, platformsZ - depth * 0.5f), pitColor), "Ground");
            SetLayer(CreateBox(parent, "PitWall_Left", new Vector3(WallThickness, wallHeight, depth),
                new Vector3(-width * 0.5f, wallY, platformsZ), pitColor), "Ground");
            SetLayer(CreateBox(parent, "PitWall_Right", new Vector3(WallThickness, wallHeight, depth),
                new Vector3(width * 0.5f, wallY, platformsZ), pitColor), "Ground");
        }

        private static void BuildPlatform(Transform parent, ExamConfig config, ExamSide side, float z)
        {
            float offset = (config.PlatformWidth + config.PlatformGap) * 0.5f;
            float x = side == ExamSide.A ? -offset : offset;

            var go = new GameObject(side == ExamSide.A ? "Platform_A" : "Platform_B");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, 0f, z);

            // Две половины-створки: петли по внешним краям, распахиваются вниз.
            float halfWidth = config.PlatformWidth * 0.5f;
            var left = BuildDoor(go.transform, "DoorLeft", config, -halfWidth * 0.5f, halfWidth, side);
            var right = BuildDoor(go.transform, "DoorRight", config, halfWidth * 0.5f, halfWidth, side);

            var hatch = go.AddComponent<HingedFloorHatch>();
            var hatchSo = new SerializedObject(hatch);
            hatchSo.FindProperty("doorLeft").objectReferenceValue = left;
            hatchSo.FindProperty("doorRight").objectReferenceValue = right;
            hatchSo.ApplyModifiedPropertiesWithoutUndo();

            GameObject letter = BuildPlatformLetter(go.transform, config, side);

            var platform = go.AddComponent<ExamAnswerPlatform>();
            var platformSo = new SerializedObject(platform);
            platformSo.FindProperty("side").enumValueIndex = side == ExamSide.A ? 1 : 2;
            platformSo.FindProperty("size").vector2Value = new Vector2(config.PlatformWidth, config.PlatformDepth);
            platformSo.FindProperty("letter").objectReferenceValue = letter;
            platformSo.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Половина пола на петле. Петля — по внешнему краю платформы, поэтому
        /// сама створка смещена от оси на четверть ширины.
        /// </summary>
        private static Transform BuildDoor(Transform parent, string name, ExamConfig config, float centerX, float hingeX, ExamSide side)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = new Vector3(Mathf.Sign(centerX) * hingeX, 0f, 0f);

            var leaf = CreateBox(pivot.transform, "Leaf",
                new Vector3(config.PlatformWidth * 0.5f, 0.2f, config.PlatformDepth),
                new Vector3(-Mathf.Sign(centerX) * config.PlatformWidth * 0.25f, -0.1f, 0f),
                Color.Lerp(new Color(0.74f, 0.7f, 0.62f), SideColor(side), PlatformTint));
            SetLayer(leaf, "Ground");

            return pivot.transform;
        }

        /// <summary>
        /// Зазор между платформами закрыт невидимым полом: решение
        /// геймдизайнера от 24.08. Если в него можно столкнуть, толчок
        /// начинает решать больше, чем угадывание.
        /// </summary>
        private static void BuildGapFloor(Transform parent, ExamConfig config, float z)
        {
            var gap = CreateBox(parent, "GapFloor_Invisible",
                new Vector3(config.PlatformGap, 0.2f, config.PlatformDepth),
                new Vector3(0f, -0.1f, z), Color.black);

            var renderer = gap.GetComponent<Renderer>();
            if (renderer != null)
            {
                Object.DestroyImmediate(renderer);
            }

            SetLayer(gap, "Ground");

            // Порог поверх невидимого пола.
            //
            // Невидимость здесь была следствием, а не целью: рендерер сняли,
            // чтобы полоса не мерцала z-fighting'ом со створками. Но сквозь
            // проём между площадками честно видно дно ямы, и полутораметровая
            // щель читается пропастью — при том что стоять на ней можно.
            // Игрок обходит место, которое на самом деле безопасно, а толчок
            // в «пропасть», ради устранения которого пол и появился, снова
            // выглядит смертельным. Видимый порог снимает обман: тёмная
            // полоса отделяет А от Б и при этом очевидно является полом.
            var threshold = CreateBox(parent, "GapThreshold",
                new Vector3(config.PlatformGap, 0.06f, config.PlatformDepth),
                new Vector3(0f, 0.02f, z), new Color(0.24f, 0.19f, 0.16f));

            var thresholdCollider = threshold.GetComponent<Collider>();
            if (thresholdCollider != null)
            {
                // Опору держит невидимый пол под ним: второй коллайдер на той
                // же высоте — это ступенька в шесть сантиметров ровно там,
                // где игроки толкаются.
                Object.DestroyImmediate(thresholdCollider);
            }
        }

        private static void BuildPodium(Transform parent, ExamConfig config, float z)
        {
            var podium = CreateBox(parent, "Podium",
                new Vector3(config.PodiumWidth, config.PodiumHeight, config.PodiumDepth),
                new Vector3(0f, config.PodiumHeight * 0.5f, z), new Color(0.55f, 0.4f, 0.28f));
            SetLayer(podium, "Ground");

            // Ученикам на кафедру нельзя: иначе толпа поднимется к Ведущему
            // и фаза выбора превратится в свалку у доски.
            var barrier = CreateBox(parent, "PodiumBarrier_Invisible",
                new Vector3(config.PodiumWidth, config.CeilingHeight, 0.3f),
                new Vector3(0f, config.CeilingHeight * 0.5f, z - config.PodiumDepth * 0.5f), Color.red);
            var barrierRenderer = barrier.GetComponent<Renderer>();
            if (barrierRenderer != null)
            {
                Object.DestroyImmediate(barrierRenderer);
            }

            SetLayer(barrier, "Ground");

            var stand = new GameObject("HostStand");
            stand.transform.SetParent(parent, false);
            stand.transform.position = new Vector3(0f, config.PodiumHeight, z);
            stand.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            // Точка, в которую садится камера Ведущего на время печати.
            // Смотрит НА кафедру, а не с неё: с разворотом на 180° она стояла
            // перед Ведущим спиной к нему, и в кадре не было ни его, ни доски —
            // только пустой класс. Наклон подобран так, чтобы в кадр попадал
            // человек за кафедрой на фоне доски.
            var rig = new GameObject("PodiumCameraRig");
            rig.transform.SetParent(parent, false);
            rig.transform.position = new Vector3(PodiumCameraSide, config.PodiumHeight + PodiumCameraLift,
                z - PodiumCameraDistance);
            rig.transform.rotation = Quaternion.Euler(PodiumCameraPitch, PodiumCameraYaw, 0f);
        }

        /// <summary>
        /// Точка, с которой Ведущий смотрит на зал, пока Ученики выбирают
        /// платформу. Стоит под потолком за спинами Учеников и смотрит вперёд:
        /// в кадре обе платформы целиком, а за ними кафедра с Ведущим и доска
        /// с его вопросом. Ракурс фиксированный намеренно — за кафедрой на
        /// 3rd person места нет, там до задней стены 1.8 м при требуемых
        /// правилом камеры ~4.5 (igruha/CLAUDE.md, 2a).
        /// </summary>
        private static void BuildHallCamera(Transform parent, ExamConfig config, float platformsZ)
        {
            var rig = new GameObject("HallCameraRig");
            rig.transform.SetParent(parent, false);
            rig.transform.position = new Vector3(
                0f,
                config.CeilingHeight - HallCameraDrop,
                platformsZ - config.PlatformDepth * 0.5f - HallCameraDistance);
            rig.transform.rotation = Quaternion.Euler(HallCameraPitch, 0f, 0f);
        }

        /// <summary>
        /// Буква варианта, лежащая на полу платформы.
        ///
        /// Приподнята над створками: лёжа с ними в одной плоскости она мерцает
        /// z-fighting'ом. Размер считается от платформы, а не задаётся числом —
        /// иначе буква вылезает за края площадки, как было до 26.08.
        /// </summary>
        private static GameObject BuildPlatformLetter(Transform parent, ExamConfig config, ExamSide side)
        {
            var canvasGo = new GameObject("LetterCanvas", typeof(Canvas));
            canvasGo.transform.SetParent(parent, false);
            canvasGo.transform.localPosition = new Vector3(0f, LetterLift, 0f);
            canvasGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            // Сторона площадки, в которую буква обязана поместиться целиком.
            float fit = Mathf.Min(config.PlatformWidth, config.PlatformDepth) * LetterFitFactor;

            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(LetterCanvasUnits, LetterCanvasUnits);
            canvasRect.localScale = Vector3.one * (fit / LetterCanvasUnits);

            var letter = new GameObject("Letter", typeof(TextMeshProUGUI));
            letter.transform.SetParent(canvasGo.transform, false);

            var letterRect = (RectTransform)letter.transform;
            letterRect.anchorMin = Vector2.zero;
            letterRect.anchorMax = Vector2.one;
            letterRect.offsetMin = Vector2.zero;
            letterRect.offsetMax = Vector2.zero;

            var text = letter.GetComponent<TextMeshProUGUI>();
            text.text = side == ExamSide.A ? "А" : "Б";
            text.fontSize = LetterCanvasUnits * 0.8f;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = Color.Lerp(SideColor(side), Color.white, LetterFloorLift);

            return canvasGo;
        }

        /// <summary>
        /// Доска над кафедрой и текст на ней.
        ///
        /// Холст строится здесь, а не руками в сцене: пункт меню сносит
        /// <c>_Arena</c> целиком, и доделанное вручную пропадало при первой же
        /// пересборке вместе со ссылкой на него у контроллера.
        ///
        /// ⚠️ Холст — <b>не ребёнок доски</b>. У доски неравномерный масштаб
        /// куба (12.96 × 2.19 × 0.2), и текст внутри неё растягивался вслед
        /// за ним: шапка уезжала выше доски, вариант Б — ниже, надписи висели
        /// в полуметре перед доской в воздухе.
        /// </summary>
        private static void BuildBoard(Transform parent, ExamConfig config, float farZ)
        {
            float width = config.HallWidth * BoardWidthFactor;
            float height = config.CeilingHeight * BoardHeightFactor;
            float centerY = config.CeilingHeight * BoardCenterFactor;
            float z = farZ - 0.3f;

            var board = CreateBox(parent, "Board", new Vector3(width, height, BoardThickness),
                new Vector3(0f, centerY, z), new Color(0.12f, 0.24f, 0.16f));
            SetLayer(board, "Ground");

            var canvasGo = new GameObject("BoardCanvas", typeof(Canvas));
            canvasGo.transform.SetParent(parent, false);
            canvasGo.transform.localPosition = new Vector3(0f, centerY, z - BoardThickness * 0.5f - 0.02f);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            // Холст меряется в сантиметрах доски: 1 юнит = 1 см, поэтому
            // размеры полей ниже читаются как сантиметры и не разъезжаются
            // при смене габаритов зала.
            var canvasRect = (RectTransform)canvasGo.transform;
            float canvasWidth = (width - BoardMargin * 2f) / BoardUnitScale;
            float canvasHeight = (height - BoardMargin * 2f) / BoardUnitScale;
            canvasRect.sizeDelta = new Vector2(canvasWidth, canvasHeight);
            canvasRect.localScale = Vector3.one * BoardUnitScale;

            // Раскладка сверху вниз: шапка, вопрос в две строки, два варианта.
            // Доли от высоты холста, чтобы ни одна строка не вышла за доску.
            TMP_Text header = CreateBoardLine(canvasRect, "Text_Header", canvasWidth,
                canvasHeight * 0.15f, canvasHeight * 0.41f, canvasHeight * 0.11f, "Header");
            TMP_Text question = CreateBoardLine(canvasRect, "Text_Question", canvasWidth,
                canvasHeight * 0.40f, canvasHeight * 0.12f, canvasHeight * 0.18f, "Question");
            question.textWrappingMode = TextWrappingModes.Normal;
            TMP_Text optionA = CreateBoardLine(canvasRect, "Text_OptionA", canvasWidth,
                canvasHeight * 0.19f, canvasHeight * -0.19f, canvasHeight * 0.14f, "OptionA");
            TMP_Text optionB = CreateBoardLine(canvasRect, "Text_OptionB", canvasWidth,
                canvasHeight * 0.19f, canvasHeight * -0.39f, canvasHeight * 0.14f, "OptionB");

            var boardComponent = canvasGo.AddComponent<ExamBoard>();
            var so = new SerializedObject(boardComponent);
            so.FindProperty("headerText").objectReferenceValue = header;
            so.FindProperty("questionText").objectReferenceValue = question;
            so.FindProperty("optionAText").objectReferenceValue = optionA;
            so.FindProperty("optionBText").objectReferenceValue = optionB;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Строка на доске. Автоподбор кегля включён у всех строк: вопрос
        /// длиной в 80 символов иначе не влезает в ширину доски и обрезается
        /// ровно там, где начинается смысл.
        /// </summary>
        private static TMP_Text CreateBoardLine(RectTransform parent, string name, float canvasWidth,
            float height, float centerY, float fontSize, string placeholder)
        {
            var go = new GameObject(name, typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(canvasWidth * 0.94f, height);
            rect.anchoredPosition = new Vector2(0f, centerY);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = placeholder;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true;
            text.fontSizeMax = fontSize;
            text.fontSizeMin = fontSize * 0.45f;
            text.fontSize = fontSize;
            text.overflowMode = TextOverflowModes.Truncate;
            return text;
        }

        private static void BuildReturnZone(Transform parent, ExamConfig config, float z)
        {
            var zone = CreateBox(parent, "ReturnZone",
                new Vector3(config.ReturnZoneWidth, 0.05f, config.ReturnZoneDepth),
                new Vector3(0f, 0.03f, z), new Color(0.6f, 0.62f, 0.68f));
            SetLayer(zone, "Ground");

            var marker = new GameObject("ReturnPoint");
            marker.transform.SetParent(parent, false);
            marker.transform.position = new Vector3(0f, 0.1f, z);
        }

        /// <summary>
        /// Обстановка класса: парты у стен, шкаф, вешалка, компьютер на
        /// кафедре, рамка доски и подвесные указатели вариантов.
        ///
        /// Всё ещё блокаут из примитивов — настоящий арт приезжает в фазе 4.
        /// Смысл здесь не в красоте: пустая коробка не читается как класс,
        /// а вариант, написанный только на полу, с уровня глаз почти не виден.
        ///
        /// ⚠️ Мебель НЕ на слое Ground. Ground для камеры непрозрачен, и
        /// деоклюдер начал бы дёргать кадр на каждой парте (igruha/CLAUDE.md,
        /// 2a). Игрокам мебель при этом остаётся препятствием: коллайдер
        /// работает независимо от слоя.
        /// </summary>
        private static void BuildDecor(Transform parent, ExamConfig config, float platformsZ, float podiumZ, float farZ)
        {
            var decor = new GameObject("Decor");
            decor.transform.SetParent(parent, false);

            BuildDeskRows(decor.transform, config);
            BuildCabinet(decor.transform, config, farZ);
            BuildCoatRack(decor.transform, config);
            BuildHostComputer(decor.transform, config, podiumZ);
            BuildBoardFrame(decor.transform, config, farZ);
            BuildOverheadSigns(decor.transform, config, platformsZ);
        }

        /// <summary>
        /// Парты в боковых нефах — колонна вдоль каждой стены, лицом к доске.
        ///
        /// Колонна начинается у дальнего края площадки и тянется до зоны
        /// возврата: там, где раньше было три метра голого пола между партой
        /// и платформой, теперь ряд. Ширина нефа 3.24 м, коробка 1.5 —
        /// метр с лишним прохода вдоль стены остаётся, и промахнувшийся мимо
        /// площадки в нём не застревает.
        /// </summary>
        private static void BuildDeskRows(Transform parent, ExamConfig config)
        {
            var wood = new Color(0.55f, 0.4f, 0.28f);
            float x = config.HallWidth * 0.5f - DeskWallGap;
            float halfWidth = DeskWidth * 0.5f;

            for (int side = 0; side < 2; side++)
            {
                float sideX = side == 0 ? -x : x;

                for (int i = 0; i < DesksPerSide; i++)
                {
                    float z = DeskFirstZ - i * DeskSpacing;
                    var desk = new GameObject("Desk_" + (side == 0 ? "L" : "R") + (i + 1));
                    desk.transform.SetParent(parent, false);
                    desk.transform.localPosition = new Vector3(sideX, 0f, z);

                    CreateBox(desk.transform, "Top", new Vector3(DeskWidth, 0.08f, DeskDepth * 0.72f),
                        new Vector3(0f, DeskHeight, DeskDepth * 0.14f), wood);
                    CreateBox(desk.transform, "LegL", new Vector3(0.1f, DeskHeight, DeskDepth * 0.6f),
                        new Vector3(-halfWidth + 0.05f, DeskHeight * 0.5f, DeskDepth * 0.14f), wood * 0.8f);
                    CreateBox(desk.transform, "LegR", new Vector3(0.1f, DeskHeight, DeskDepth * 0.6f),
                        new Vector3(halfWidth - 0.05f, DeskHeight * 0.5f, DeskDepth * 0.14f), wood * 0.8f);
                    CreateBox(desk.transform, "Bench", new Vector3(DeskWidth, 0.08f, 0.34f),
                        new Vector3(0f, DeskHeight * 0.6f, -DeskDepth * 0.42f), wood * 0.9f);
                }
            }
        }

        /// <summary>Шкаф у дальней стены, со стороны кафедры.</summary>
        private static void BuildCabinet(Transform parent, ExamConfig config, float farZ)
        {
            float x = config.HallWidth * 0.5f - 1.2f;
            CreateBox(parent, "Cabinet", new Vector3(1.8f, 2.3f, 0.6f),
                new Vector3(-x, 1.15f, farZ - 0.6f), new Color(0.42f, 0.31f, 0.22f));
        }

        /// <summary>Вешалка у боковой стены: стойка и перекладина.</summary>
        private static void BuildCoatRack(Transform parent, ExamConfig config)
        {
            float x = config.HallWidth * 0.5f - 0.9f;
            var metal = new Color(0.3f, 0.31f, 0.34f);

            var rack = new GameObject("CoatRack");
            rack.transform.SetParent(parent, false);
            rack.transform.localPosition = new Vector3(x, 0f, -2.5f);

            CreateBox(rack.transform, "Post", new Vector3(0.1f, 1.9f, 0.1f), new Vector3(0f, 0.95f, 0f), metal);
            CreateBox(rack.transform, "Bar", new Vector3(0.08f, 0.08f, 2.4f), new Vector3(0f, 1.85f, 0f), metal);
        }

        /// <summary>Тумба с ретро-монитором на кафедре (спека 4.6).</summary>
        private static void BuildHostComputer(Transform parent, ExamConfig config, float podiumZ)
        {
            var desk = new GameObject("HostDesk");
            desk.transform.SetParent(parent, false);
            desk.transform.localPosition = new Vector3(0f, config.PodiumHeight, podiumZ - 1.3f);

            CreateBox(desk.transform, "Stand", new Vector3(1.4f, HostDeskHeight, 0.65f),
                new Vector3(0f, HostDeskHeight * 0.5f, 0f), new Color(0.5f, 0.36f, 0.25f));
            // Монитор стоит по центру стола, а не сбоку. Сдвинут туда на 4.3:
            // замер зеркальности зала показал его единственным предметом
            // у оси, у которого нет пары, — а правило симметрии в этой игре
            // проверяется числом и исключений не терпит. Коллайдеры декора
            // при этом не появились и не исчезли, только переехали на 0.35 м
            // на высоте двух метров над недоступным Ученику возвышением.
            CreateBox(desk.transform, "Monitor", new Vector3(0.55f, 0.45f, 0.5f),
                new Vector3(0f, HostDeskHeight + 0.225f, 0f), new Color(0.78f, 0.76f, 0.7f));
            CreateBox(desk.transform, "Screen", new Vector3(0.42f, 0.32f, 0.02f),
                new Vector3(0f, HostDeskHeight + 0.245f, -0.26f), new Color(0.15f, 0.35f, 0.2f));
        }

        /// <summary>Рамка доски — четыре бруска по периметру.</summary>
        private static void BuildBoardFrame(Transform parent, ExamConfig config, float farZ)
        {
            float width = config.HallWidth * BoardWidthFactor;
            float height = config.CeilingHeight * BoardHeightFactor;
            float centerY = config.CeilingHeight * BoardCenterFactor;
            float z = farZ - 0.3f - BoardThickness * 0.5f - BoardFrameThickness * 0.5f;
            var frame = new Color(0.35f, 0.25f, 0.16f);

            CreateBox(parent, "BoardFrame_Top", new Vector3(width + BoardFrameThickness * 2f, BoardFrameThickness, BoardFrameThickness),
                new Vector3(0f, centerY + height * 0.5f, z), frame);
            CreateBox(parent, "BoardFrame_Bottom", new Vector3(width + BoardFrameThickness * 2f, BoardFrameThickness, BoardFrameThickness),
                new Vector3(0f, centerY - height * 0.5f, z), frame);
            CreateBox(parent, "BoardFrame_Left", new Vector3(BoardFrameThickness, height, BoardFrameThickness),
                new Vector3(-width * 0.5f - BoardFrameThickness * 0.5f, centerY, z), frame);
            CreateBox(parent, "BoardFrame_Right", new Vector3(BoardFrameThickness, height, BoardFrameThickness),
                new Vector3(width * 0.5f + BoardFrameThickness * 0.5f, centerY, z), frame);
        }

        /// <summary>
        /// Подвесные указатели А и Б над платформами.
        ///
        /// Буква на полу читается только сверху, а глаза персонажа в метре
        /// с небольшим над ней: с уровня игрока площадки различались плохо.
        /// Указатель виден с любой точки зала и с обеих сторон.
        /// </summary>
        private static void BuildOverheadSigns(Transform parent, ExamConfig config, float platformsZ)
        {
            float offset = (config.PlatformWidth + config.PlatformGap) * 0.5f
                           + config.PlatformWidth * 0.5f * SignOutwardFactor;

            float signHeight = config.CeilingHeight - SignDropFromCeiling - SignPlateHeight * 0.5f;

            for (int i = 0; i < 2; i++)
            {
                ExamSide side = i == 0 ? ExamSide.A : ExamSide.B;
                float x = i == 0 ? -offset : offset;

                var sign = new GameObject("Sign_" + (side == ExamSide.A ? "A" : "B"));
                sign.transform.SetParent(parent, false);
                sign.transform.localPosition = new Vector3(x, signHeight, platformsZ);

                // Подвес ровно от верха таблички до потолка: посчитанный
                // «на глаз» он протыкал перекрытие и торчал над классом.
                float plateHalf = SignPlateHeight * 0.5f;
                float ropeLength = config.CeilingHeight - signHeight - plateHalf;
                CreateBox(sign.transform, "Rope", new Vector3(0.06f, ropeLength, 0.06f),
                    new Vector3(0f, plateHalf + ropeLength * 0.5f, 0f), new Color(0.3f, 0.3f, 0.32f));
                CreateBox(sign.transform, "Plate", new Vector3(SignPlateWidth, SignPlateHeight, 0.1f),
                    Vector3.zero, new Color(0.93f, 0.92f, 0.88f));

                // Обе стороны: с одной надписью табличка читалась бы с изнанки
                // зеркально — половине зала.
                CreateSignFace(sign.transform, "Face_Front", side, new Vector3(0f, 0f, -0.07f), 0f);
                CreateSignFace(sign.transform, "Face_Back", side, new Vector3(0f, 0f, 0.07f), 180f);
            }
        }

        private static void CreateSignFace(Transform parent, string name, ExamSide side, Vector3 localPosition, float yaw)
        {
            var canvasGo = new GameObject(name, typeof(Canvas));
            canvasGo.transform.SetParent(parent, false);
            canvasGo.transform.localPosition = localPosition;
            canvasGo.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = new Vector2(SignPlateWidth * 100f, SignPlateHeight * 100f);
            rect.localScale = Vector3.one * 0.01f;

            var text = new GameObject("Letter", typeof(TextMeshProUGUI));
            text.transform.SetParent(canvasGo.transform, false);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var tmp = text.GetComponent<TextMeshProUGUI>();
            tmp.text = side == ExamSide.A ? "А" : "Б";
            tmp.fontSize = SignPlateHeight * 85f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.color = SideColor(side);
        }

        /// <summary>
        /// Цвет варианта: один и тот же на полу, на указателе и на рамке люка.
        ///
        /// Синий с янтарным взяты не на вкус: эта пара различима при всех
        /// распространённых формах дальтонизма, а красный с зелёным — нет.
        /// Дресс 4.1 и палитра 4.2 берут цвет отсюда, а не заводят свой:
        /// разойдись они, «синее = вариант А» перестало бы работать ровно там,
        /// где нужнее всего.
        /// </summary>
        internal static Color SideColor(ExamSide side) => side == ExamSide.A
            ? new Color(0.16f, 0.42f, 0.72f)
            : new Color(0.78f, 0.5f, 0.09f);

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

            var instance = new Material(blockoutMaterial) { color = color };
            return instance;
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

        private static ExamConfig FindConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:ExamConfig");
            if (guids.Length == 0)
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<ExamConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
