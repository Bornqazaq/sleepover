using TMPro;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Minigames.Exam;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Строит игровую геометрию «Экзамена», затем собственный зал из Blender.
    /// Створки, рабочие зоны и ссылки игры воспроизводятся одной командой.
    ///
    /// Всё строится кодом, а не руками, по той же причине, что и цирк:
    /// размеры живут в <see cref="ExamConfig"/>, и пересобрать арену после
    /// правки числа должно быть одним нажатием.
    /// </summary>
    public static class ExamArenaBuilder
    {
        private const string Root = "_Arena";
        private const float WallThickness = .4f;
        private const float BoardThickness = .2f;
        private const float BoardMargin = .18f;
        private const float BoardUnitScale = .01f;
        private const float PodiumCameraDistance = 9f;
        private const float PodiumCameraSide = 2.6f;
        private const float PodiumCameraHeight = 3.5f;
        private const float PodiumCameraPitch = -2.5f;
        private const float PodiumCameraYaw = -12f;
        // Preserve the gameplay overview independently of the taller architecture.
        private const float HallCameraDistance = 9.2f;
        private const float HallCameraHeight = 4.08f;
        private const float HallCameraPitch = 12.5f;
        private const float DoorSwingClearance = 1.6f;
        // Camera retreat space below the unchanged floor opening.
        private const float PitEndClearance = 2.52f;
        private const float PitWallSink = .05f;
        private const float LetterLift = .06f;
        private const float LetterCanvasUnits = 400f;
        private const float LetterFitFactor = .55f;
        private const float LetterFloorLift = .4f;
        private const float PlatformTint = .3f;

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

            if (ExamHallAssets.Material("Plaster") == null) ExamHallAssets.Import();
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
            BuildHallCamera(root.transform, config, platformsZ);
            BuildBoard(root.transform, config, far);
            BuildReturnZone(root.transform, config, returnZ);
            ExamHallBuilder.Apply(root, config);
            ExamVfx.Build(root, config);

            // Оформление интерфейса — тем же прогоном: иначе пересборка арены
            // вернула бы серые прямоугольники шаблона.
            UiSkinPass.Apply();

            WireMinigameReferences(root);
            for(int i=1;i<=8;i++)
            {
                var spawn=GameObject.Find("Spawn_Student_"+i);
                if(spawn!=null)spawn.transform.position=new Vector3(-3.15f+(i-1)*.9f,.1f,-6.12f);
            }
            var specialSpawn = GameObject.Find("Spawn_Host_Special");
            if (specialSpawn != null)
                specialSpawn.transform.SetPositionAndRotation(root.transform.Find("HostStand").position,
                    root.transform.Find("HostStand").rotation);

            Debug.Log($"📚 Арена «Экзамена» построена: зал {config.HallWidth:F1}×{config.HallDepth:F1} м, " +
                      $"платформы {config.PlatformWidth:F1}×{config.PlatformDepth:F1} м", root);

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

        /// <summary>Яма под платформами: туда улетают те, кто выбрал неверно.</summary>
        private static void BuildPit(Transform parent, ExamConfig config, float platformsZ)
        {
            // Яма шире проёма: раскрываясь на 110°, створка заваливается
            // за вертикаль и уходит наружу от своей петли. Впритык к проёму
            // она втыкалась бы в стенку ямы у всех на глазах.
            float width = config.PlatformWidth * 2f + config.PlatformGap + DoorSwingClearance * 2f;
            var pitColor = new Color(0.12f, 0.12f, 0.14f);
            var pit = CreateBox(parent, "PitFloor", new Vector3(width, WallThickness, config.PlatformDepth + PitEndClearance * 2f),
                new Vector3(0f, -config.PitDepth, platformsZ), pitColor);
            SetLayer(pit, "Ground");

            // Стенки ямы. Без них сквозь открытый проём видно пустоту за
            // пределами зала, а упавший укатывается под пол.
            // Верх стенок уходит чуть ниже пола: вровень с ним они снова дают
            // ту же полосу z-fighting'а, из-за которой мерцали площадки.
            // Щели не будет — пол зала толще этого запаса.
            float depth = config.PlatformDepth + PitEndClearance * 2f;
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
            rig.transform.position = new Vector3(PodiumCameraSide, PodiumCameraHeight,
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
                HallCameraHeight,
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
            float width = ExamHallBuilder.BoardWidth;
            float height = ExamHallBuilder.BoardHeight;
            float centerY = ExamHallBuilder.BoardCenter;
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
                canvasHeight * 0.11f, canvasHeight * 0.44f, canvasHeight * 0.10f, "Header");
            TMP_Text question = CreateBoardLine(canvasRect, "Text_Question", canvasWidth,
                canvasHeight * 0.34f, canvasHeight * 0.18f, canvasHeight * 0.18f, "Question");
            question.textWrappingMode = TextWrappingModes.Normal;
            TMP_Text optionA = CreateBoardLine(canvasRect, "Text_OptionA", canvasWidth,
                canvasHeight * 0.16f, canvasHeight * -0.09f, canvasHeight * 0.14f, "OptionA");
            TMP_Text optionB = CreateBoardLine(canvasRect, "Text_OptionB", canvasWidth,
                canvasHeight * 0.16f, canvasHeight * -0.29f, canvasHeight * 0.14f, "OptionB");

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

        /// <summary>Semantic side colors also used by legacy editor palette tools.</summary>
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

        private static Material GetBlockoutMaterial(Color color) => ExamHallAssets.Material("Plaster");

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
