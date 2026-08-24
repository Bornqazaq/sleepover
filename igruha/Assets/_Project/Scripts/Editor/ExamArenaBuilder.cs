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

            BuildHall(root.transform, config);
            BuildPit(root.transform, config, platformsZ);
            BuildPlatform(root.transform, config, ExamSide.A, platformsZ);
            BuildPlatform(root.transform, config, ExamSide.B, platformsZ);
            BuildGapFloor(root.transform, config, platformsZ);
            BuildPodium(root.transform, config, podiumZ);
            BuildBoard(root.transform, config, far);
            BuildReturnZone(root.transform, config, returnZ);

            Debug.Log($"📚 Арена «Экзамена» построена: зал {config.HallWidth:F1}×{config.HallDepth:F1} м, " +
                      $"платформы {config.PlatformWidth:F1}×{config.PlatformDepth:F1} м", root);

            Selection.activeGameObject = root;
        }

        private static void BuildHall(Transform parent, ExamConfig config)
        {
            var floor = CreateBox(parent, "Floor", new Vector3(config.HallWidth, WallThickness, config.HallDepth),
                new Vector3(0f, -WallThickness * 0.5f, 0f), new Color(0.72f, 0.68f, 0.62f));
            SetLayer(floor, "Ground");

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

        /// <summary>Яма под платформами: туда улетают те, кто выбрал неверно.</summary>
        private static void BuildPit(Transform parent, ExamConfig config, float platformsZ)
        {
            float width = config.PlatformWidth * 2f + config.PlatformGap;
            var pit = CreateBox(parent, "PitFloor", new Vector3(width, WallThickness, config.PlatformDepth),
                new Vector3(0f, -config.PitDepth, platformsZ), new Color(0.12f, 0.12f, 0.14f));
            SetLayer(pit, "Ground");
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

            var platform = go.AddComponent<ExamAnswerPlatform>();
            var platformSo = new SerializedObject(platform);
            platformSo.FindProperty("side").enumValueIndex = side == ExamSide.A ? 1 : 2;
            platformSo.FindProperty("size").vector2Value = new Vector2(config.PlatformWidth, config.PlatformDepth);
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

        private static void BuildBoard(Transform parent, ExamConfig config, float farZ)
        {
            var board = CreateBox(parent, "Board",
                new Vector3(config.HallWidth * 0.45f, config.CeilingHeight * 0.38f, 0.2f),
                new Vector3(0f, config.CeilingHeight * 0.62f, farZ - 0.3f),
                new Color(0.12f, 0.24f, 0.16f));
            SetLayer(board, "Ground");
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
