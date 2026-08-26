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
            BuildBoard(root.transform, config, far);
            BuildReturnZone(root.transform, config, returnZ);

            WireMinigameReferences(root);

            Debug.Log($"📚 Арена «Экзамена» построена: зал {config.HallWidth:F1}×{config.HallDepth:F1} м, " +
                      $"платформы {config.PlatformWidth:F1}×{config.PlatformDepth:F1} м", root);

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
            var left = BuildDoor(go.transform, "DoorLeft", config, -halfWidth * 0.5f, halfWidth);
            var right = BuildDoor(go.transform, "DoorRight", config, halfWidth * 0.5f, halfWidth);

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
        private static Transform BuildDoor(Transform parent, string name, ExamConfig config, float centerX, float hingeX)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = new Vector3(Mathf.Sign(centerX) * hingeX, 0f, 0f);

            var leaf = CreateBox(pivot.transform, "Leaf",
                new Vector3(config.PlatformWidth * 0.5f, 0.2f, config.PlatformDepth),
                new Vector3(-Mathf.Sign(centerX) * config.PlatformWidth * 0.25f, -0.1f, 0f),
                new Color(0.78f, 0.6f, 0.36f));
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

            var rig = new GameObject("PodiumCameraRig");
            rig.transform.SetParent(parent, false);
            rig.transform.position = new Vector3(0f, config.PodiumHeight + 1.8f, z - 3.2f);
            rig.transform.rotation = Quaternion.Euler(12f, 180f, 0f);
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
            text.color = new Color(0.16f, 0.14f, 0.10f, 0.85f);

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
            float width = config.HallWidth * 0.45f;
            float height = config.CeilingHeight * 0.5f;
            float centerY = config.CeilingHeight * 0.6f;
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
