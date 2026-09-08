using UnityEditor;
using UnityEngine;
using Igruha.Core.Spawning;
using Igruha.Core.Vision;
using Igruha.Minigames.CryingAngels;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Пересборка арены «Плачущих ангелов» по радиусу из CryingAngelsConfig.
    /// Расстановка руками означала, что смена размера зала — это переделка
    /// уровня; здесь это одно число в конфиге плюс пункт меню.
    ///
    /// Что масштабируется, а что нет, решает простое правило: размеры,
    /// заданные относительно персонажа (высоты укрытий, стен, постамента,
    /// толщина укрытий), остаются как есть — они настроены под присед, рост
    /// и высоту прыжка. Масштабируются только расстояния по площади зала.
    /// </summary>
    internal static class CryingAngelsArenaBuilder
    {
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/CryingAngelsConfig.asset";
        private const string KeeperRigPath = "Assets/_Project/Prefabs/Minigames/CryingAngels/KeeperRig.prefab";

        // Высоты — от персонажа, не от зала. Не масштабируются.
        private const float LowCoverHeight = 0.864f;   // 1.2 ШП — присед прячет, стоя видно голову
        private const float HighCoverHeight = 2.16f;   // 3 ШП — выше прыжка с земли
        private const float WallHeight = 4.32f;
        private const float WallThickness = 0.4f;
        private const float PedestalRadius = 2.16f;
        private const float PedestalHeight = 0.36f;
        private const float MinPassage = 1.44f;        // 2 ШП между укрытиями
        private const float CoverFootprintMin = 0.95f;
        private const float CoverFootprintMax = 1.25f;

        // Границы зон и плотность — доли радиуса, взяты из разметки спеки.
        private const float InnerRingT = 0.528f;
        private const float MiddleRingT = 0.736f;
        private const float OuterRingT = 0.861f;
        private const float SpawnRingT = 0.95f;
        private const int WallSegments = 16;
        /// <summary>Пол, постамент и стены обязаны быть на Ground: по нему считается «стою на земле» и по нему же камера обходит препятствия.</summary>
        private const string GroundLayerName = "Ground";
        private const int PlacementAttempts = 400;
        private const int Seed = 20250815;

        // Плотность укрытий эталонной арены радиуса 14.4: 6 / 10 / 16 на кольцо.
        private const float ReferenceRadius = 14.4f;
        private const int ReferenceInner = 6;
        private const int ReferenceMiddle = 10;
        private const int ReferenceOuter = 16;

        [MenuItem("Igruha/Minigames/Rebuild Crying Angels Arena")]
        private static void Rebuild()
        {
            var config = AssetDatabase.LoadAssetAtPath<CryingAngelsConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError("CryingAngelsArenaBuilder: не найден " + ConfigPath);
                return;
            }

            GameObject arenaRoot = GameObject.Find("_Arena");
            GameObject spawnsRoot = GameObject.Find("_Spawns");
            if (arenaRoot == null || spawnsRoot == null)
            {
                Debug.LogError("CryingAngelsArenaBuilder: открой сцену CryingAngels — не найдены _Arena/_Spawns.");
                return;
            }

            float radius = config.ArenaRadius;
            int coverLayer = LayerMask.NameToLayer("Cover");
            int groundLayer = LayerMask.NameToLayer(GroundLayerName);
            if (groundLayer < 0)
            {
                Debug.LogError($"CryingAngelsArenaBuilder: не найден слой {GroundLayerName} — прыжок и обход камерой сломаются.");
                return;
            }
            // Плотность сохраняем по площади: иначе большой зал становится голым,
            // и последний рывок к центру перестаёт быть единственным открытым местом.
            float areaScale = (radius * radius) / (ReferenceRadius * ReferenceRadius);

            BuildFloor(arenaRoot, radius, groundLayer);
            BuildPedestal(arenaRoot, groundLayer);
            BuildWall(arenaRoot, radius, groundLayer);

            Transform covers = ResetGroup(arenaRoot.transform, "Covers");
            var placed = new System.Collections.Generic.List<Vector3>(128);
            var random = new System.Random(Seed);
            int inner = Mathf.RoundToInt(ReferenceInner * areaScale);
            int middle = Mathf.RoundToInt(ReferenceMiddle * areaScale);
            int outer = Mathf.RoundToInt(ReferenceOuter * areaScale);

            int made = 0;
            made += PlaceRing(covers, coverLayer, random, placed, "Inner", inner, radius * InnerRingT, radius * 0.06f);
            made += PlaceRing(covers, coverLayer, random, placed, "Middle", middle, radius * MiddleRingT, radius * 0.05f);
            made += PlaceRing(covers, coverLayer, random, placed, "Outer", outer, radius * OuterRingT, radius * 0.045f);

            MoveSpawns(spawnsRoot.transform, radius * SpawnRingT);
            made += ScreenSpawns(covers, coverLayer, random, placed, spawnsRoot.transform, radius);
            ScaleKillZone(radius);
            ApplyBeamRange(radius);

            // Без синхронизации физика ещё не знает о только что созданных
            // коллайдерах, и проверка простреливаемости врёт «всё открыто».
            Physics.SyncTransforms();
            int exposed = CountExposedSpawns(spawnsRoot.transform);
            if (exposed > 0)
            {
                Debug.LogWarning($"CryingAngelsArenaBuilder: {exposed} спавнов простреливаются из центра насквозь — " +
                                 "игрок попадёт под луч, ещё не сделав шага. Добавь укрытий во внешнее кольцо.");
            }


            // Физика декора — последним шагом сборки. Дресс срезает коллайдеры
            // моделей, и всё, что поставлено в зал само по себе, без коробки
            // блокаута, до этого шага проходилось насквозь.
            PropColliders.Build(arenaRoot);

            // Оформление интерфейса — тем же прогоном: иначе пересборка арены
            // вернула бы серые прямоугольники шаблона.
            UiSkinPass.Apply();

            EditorSceneManagerSetDirty();
            Debug.Log($"Арена пересобрана: радиус {radius:F2} ({radius / 0.72f:F0} ШП), укрытий {made} " +
                      $"(внутр. {inner} / средн. {middle} / внешн. {outer}), спавны на {radius * SpawnRingT:F2}, дальность луча {radius:F2}.");
        }

        private static void BuildFloor(GameObject root, float radius, int groundLayer)
        {
            Transform floor = EnsurePrimitive(root.transform, "Floor", PrimitiveType.Cube);
            floor.gameObject.layer = groundLayer;
            floor.position = new Vector3(0f, -0.2f, 0f);
            floor.localScale = new Vector3(radius * 2f, 0.2f, radius * 2f);
        }

        private static void BuildPedestal(GameObject root, int groundLayer)
        {
            Transform pedestal = EnsurePrimitive(root.transform, "Pedestal", PrimitiveType.Cylinder);
            pedestal.gameObject.layer = groundLayer;
            pedestal.position = new Vector3(0f, PedestalHeight * 0.5f, 0f);
            pedestal.localScale = new Vector3(PedestalRadius * 2f, PedestalHeight * 0.5f, PedestalRadius * 2f);
        }

        private static void BuildWall(GameObject root, float radius, int groundLayer)
        {
            Transform wall = ResetGroup(root.transform, "Wall");
            float ringRadius = radius + WallThickness * 0.5f;
            // Сегменты с нахлёстом: встык они расходятся на округлении и оставляют щели.
            float segmentWidth = 2f * Mathf.PI * ringRadius / WallSegments * 1.03f;

            for (int i = 0; i < WallSegments; i++)
            {
                float angle = 360f / WallSegments * i;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Wall_{i + 1:00}";
                go.layer = groundLayer;
                go.transform.SetParent(wall, false);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                go.transform.position = dir * ringRadius + Vector3.up * (WallHeight * 0.5f);
                go.transform.rotation = Quaternion.LookRotation(dir);
                go.transform.localScale = new Vector3(segmentWidth, WallHeight, WallThickness);
            }
        }

        /// <summary>
        /// Кольцо укрытий. Углы берутся по секторам, а не случайно: на большой
        /// окружности равномерный рандом сбивается в кучки и оставляет широкие
        /// пустые сектора — то есть прямой коридор от края к центру, который
        /// спека запрещает отдельным пунктом. Сектор гарантирует, что укрытие
        /// есть в каждом направлении, а разброс внутри сектора убирает
        /// ощущение частокола.
        ///
        /// Минимальный проход проверяется отдельно: два укрытия ближе 2 ШП
        /// образуют щель, в которую персонаж не проходит, и получается тупик.
        /// </summary>
        private static int PlaceRing(Transform parent, int layer, System.Random random,
            System.Collections.Generic.List<Vector3> placed, string zone, int count, float ringRadius, float jitter)
        {
            int made = 0;
            float sector = Mathf.PI * 2f / Mathf.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                bool ok = false;
                for (int attempt = 0; attempt < PlacementAttempts && !ok; attempt++)
                {
                    // Разброс внутри своего сектора, сужающийся с попытками:
                    // если место занято, укрытие поджимается к центру сектора,
                    // а не улетает в чужой и не оставляет дыру.
                    float spread = Mathf.Lerp(0.5f, 0.15f, attempt / (float)PlacementAttempts);
                    float angle = sector * (i + 0.5f + (float)(random.NextDouble() * 2.0 - 1.0) * spread);
                    float r = ringRadius + (float)(random.NextDouble() * 2.0 - 1.0) * jitter;
                    Vector3 pos = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);

                    if (TooClose(placed, pos))
                    {
                        continue;
                    }

                    placed.Add(pos);
                    // Высоких ~40%: их меньше, потому что они прячут всегда,
                    // а низкие вознаграждают приседание.
                    bool high = random.NextDouble() < 0.4;
                    float footprint = Mathf.Lerp(CoverFootprintMin, CoverFootprintMax, (float)random.NextDouble());
                    float height = high ? HighCoverHeight : LowCoverHeight;

                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Cover_{zone}_{i + 1:00}_{(high ? "High" : "Low")}";
                    go.layer = layer;
                    go.transform.SetParent(parent, false);
                    go.transform.position = pos + Vector3.up * (height * 0.5f);
                    go.transform.rotation = Quaternion.Euler(0f, (float)(random.NextDouble() * 360.0), 0f);
                    go.transform.localScale = new Vector3(footprint, height, footprint);
                    ok = true;
                    made++;
                }
            }

            return made;
        }

        /// <summary>
        /// Заслон перед каждым спавном. Равномерная раскладка по кольцу этого
        /// не даёт: укрытия перекрывают лишь треть направлений, и попасть
        /// заслоном ровно в линию «постамент — спавн» она может только случайно.
        /// А спавн обязан быть прикрыт — иначе раунд начинается с того, что
        /// игрока держат лучом на его же стартовой точке, и он не играет вовсе.
        ///
        /// Заслон высокий: низкий прикрывает только присевшего, а на старте
        /// игрок стоит и ещё не понял, что происходит.
        /// </summary>
        private static int ScreenSpawns(Transform parent, int layer, System.Random random,
            System.Collections.Generic.List<Vector3> placed, Transform spawnsRoot, float radius)
        {
            var points = spawnsRoot.GetComponentsInChildren<SpawnPoint>(true);
            int made = 0;

            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].Role == SpawnRole.Special)
                {
                    continue;
                }

                Vector3 spawn = points[i].transform.position;
                Vector3 dir = new Vector3(spawn.x, 0f, spawn.z).normalized;
                // Ставим между спавном и центром, ближе к спавну: так заслон
                // работает сразу на старте, а не когда игрок уже пробежал полпути.
                Vector3 pos = dir * (radius * SpawnRingT - 2.0f);

                float footprint = CoverFootprintMax;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Cover_SpawnScreen_{i + 1:00}_High";
                go.layer = layer;
                go.transform.SetParent(parent, false);
                go.transform.position = pos + Vector3.up * (HighCoverHeight * 0.5f);
                go.transform.rotation = Quaternion.Euler(0f, (float)(random.NextDouble() * 360.0), 0f);
                go.transform.localScale = new Vector3(footprint * 1.6f, HighCoverHeight, footprint);
                placed.Add(pos);
                made++;
            }

            return made;
        }

        private static bool TooClose(System.Collections.Generic.List<Vector3> placed, Vector3 candidate)
        {
            float minDistance = CoverFootprintMax + MinPassage;
            for (int i = 0; i < placed.Count; i++)
            {
                if ((placed[i] - candidate).sqrMagnitude < minDistance * minDistance)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Спавны двигаем, а не пересоздаём: на них висят SpawnPoint с ролями.</summary>
        private static void MoveSpawns(Transform spawnsRoot, float ringRadius)
        {
            var points = spawnsRoot.GetComponentsInChildren<SpawnPoint>(true);
            int defaults = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].Role == SpawnRole.Special)
                {
                    continue;
                }

                defaults++;
            }

            int index = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].Role == SpawnRole.Special)
                {
                    continue;
                }

                float angle = 360f / Mathf.Max(1, defaults) * index;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 pos = dir * ringRadius;
                pos.y = points[i].transform.position.y;
                points[i].transform.position = pos;
                // Смотрят в центр: иначе на старте игрок развёрнут к стене.
                points[i].transform.rotation = Quaternion.LookRotation(-dir);
                index++;
            }
        }

        private static void ScaleKillZone(float radius)
        {
            GameObject bounds = GameObject.Find("_Bounds");
            if (bounds == null)
            {
                return;
            }

            var colliders = bounds.GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Transform t = colliders[i].transform;
                t.localScale = new Vector3(radius * 2.2f, t.localScale.y, radius * 2.2f);
            }
        }

        /// <summary>Луч обязан добивать до стены, иначе у края появляется зона, куда Водящий не дотягивается в принципе.</summary>
        private static void ApplyBeamRange(float radius)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KeeperRigPath);
            if (prefab == null)
            {
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(KeeperRigPath);
            try
            {
                var vision = contents.GetComponentInChildren<VisionCone>(true);
                if (vision != null)
                {
                    var so = new SerializedObject(vision);
                    so.FindProperty("range").floatValue = radius;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(contents, KeeperRigPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Сколько спавнов видно с постамента напрямую. Спавн обязан быть
        /// прикрыт: иначе раунд начинается с того, что игрока держат лучом
        /// на его же стартовой точке, и играть он не начинает вообще.
        /// </summary>
        private static int CountExposedSpawns(Transform spawnsRoot)
        {
            int mask = 1 << LayerMask.NameToLayer("Cover");
            Vector3 eye = Vector3.up * 1.5f;
            int exposed = 0;

            var points = spawnsRoot.GetComponentsInChildren<SpawnPoint>(true);
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].Role == SpawnRole.Special)
                {
                    continue;
                }

                Vector3 target = points[i].transform.position + Vector3.up * 0.9f;
                Vector3 dir = target - eye;
                if (!Physics.Raycast(eye, dir.normalized, dir.magnitude, mask))
                {
                    exposed++;
                }
            }

            return exposed;
        }

        private static Transform EnsurePrimitive(Transform parent, string name, PrimitiveType type)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                return existing;
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

            return group;
        }

        private static void EditorSceneManagerSetDirty()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }
    }
}
