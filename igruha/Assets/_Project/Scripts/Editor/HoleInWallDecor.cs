using UnityEditor;
using UnityEngine;
using Igruha.Minigames.HoleInWall;
using Tone = Igruha.EditorTools.HoleInWallPaletteAssets.Tone;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Наполнение павильона «Дырки в стене»: публика на трибунах, шахты света
    /// из прожекторов и трансляционные камеры на боковых настилах.
    ///
    /// Появилось по разбору кадра: павильон 4.3 дал стены, фермы, софиты и
    /// трибуны, но трибуны остались <b>пустыми</b>, а прожекторы светили
    /// невидимым светом. В кадре это читалось как «сыро»: середина экрана —
    /// плоская тёмная полоса между водой и фермами, и ни одного признака, что
    /// идёт съёмка при живом зале.
    ///
    /// <b>Отдельный инструмент, а не часть павильона — сознательно.</b>
    /// Павильон строится только вместе со всей ареной, а пересборка арены
    /// возвращает ссылки на <c>Assets/Synty/**</c> и обнуляет запекание 4.6
    /// (STATE 3.45: было 71 ссылка, стало 0). Поэтому декор умеет доклеиться
    /// к готовой сцене на месте, ничего в ней не пересобирая. Из
    /// <see cref="HoleInWallEnvironment"/> он тоже зовётся — чтобы будущая
    /// честная пересборка его не потеряла.
    /// </summary>
    /// <remarks>
    /// <b>Правила павильона соблюдаются буквально.</b> Ни одного коллайдера,
    /// слой <c>Default</c>, тени не отбрасываются — иначе публика ловила бы
    /// толчки сметённых игроков и лучи деокклюдера камеры, а тени погасили бы
    /// студийный свет (см. шапку <see cref="HoleInWallEnvironment"/>).
    ///
    /// <b>Обе запретные зоны учтены числами, а не на глаз.</b>
    /// <c>HoleInWallArtAudit</c> проверяет пятно арены до высоты
    /// «верх стены плюс запас» = <c>PlatformSurfaceY + WallHeight + 0.6</c>
    /// и полосу перед камерой. Трибуны и настилы стоят на X ±24.8 при
    /// полуширине зоны 21.0 — снаружи. Шахты света упираются низом в
    /// <see cref="ShaftFloorMargin"/> над верхом зоны, поэтому висят в воздухе
    /// обрезанными: это луч, уходящий в дымку, а не конус до пола. Конус до
    /// пола перекрывал бы вырез, то есть саму игру.
    ///
    /// <b>Только запечённые модели.</b> Камеры берутся из
    /// <c>Assets/_Project/Art/HoleInWall/</c>, куда 4.6 их уже скопировал.
    /// Обращаться в <c>Assets/Synty/**</c> отсюда нельзя — вернём ту самую
    /// ссылку, ради снятия которой ветка не вливалась шесть подфаз.
    /// </remarks>
    internal static class HoleInWallDecor
    {
        private const string ConfigPath =
            "Assets/_Project/Settings/Gameplay/Minigames/HoleInWallConfig.asset";

        private const string ArenaRoot = "_Arena";
        private const string StudioRoot = "_Studio";
        private const string DecorGroup = "Decor";

        private const string PackModels = "Assets/Synty/PolygonNightclubs/Models/";

        /// <summary>Восемь персонажей одним FBX — из них набирается зал.</summary>
        private const string CrowdPath = PackModels + "Characters.fbx";
        private const string SpeakerPath = PackModels + "SM_Prop_Speaker_Large_01.fbx";
        private const string SpeakerTallPath = PackModels + "SM_Prop_Speaker_Large_02.fbx";
        private const string ScreenPath = PackModels + "SM_Prop_Screen_01.fbx";
        private static readonly string[] SignPaths =
        {
            PackModels + "SM_Prop_Sign_Club_01.fbx",
            PackModels + "SM_Prop_Sign_Club_02.fbx",
            PackModels + "SM_Prop_Sign_Red_01.fbx",
            PackModels + "SM_Prop_Sign_Bar_01.fbx"
        };

        private const string CameraBodyPath =
            "Assets/_Project/Art/HoleInWall/PolygonShops/Models/SM_Prop_Computer_Camera_DSLR_01.fbx";
        private const string CameraTripodPath =
            "Assets/_Project/Art/HoleInWall/PolygonShops/Models/SM_Prop_Computer_Camera_Tripod_01.fbx";

        // ========== ПУБЛИКА ==========

        /// <summary>Сколько зрителей на одной секции трибуны.</summary>
        private const int CrowdPerBleacher = 5;

        /// <summary>Рост зрителя, м. Ниже игрока: публика не должна читаться как участник.</summary>
        private const float CrowdHeightMin = 0.95f;
        private const float CrowdHeightMax = 1.35f;

        /// <summary>Толщина зрителя, м.</summary>
        private const float CrowdWidth = 0.42f;

        /// <summary>Доля зрителей, подсвеченных «телефоном». Живой зал снимает на телефоны.</summary>
        private const float CrowdPhoneShare = 0.40f;

        /// <summary>Размер огонька телефона, м.</summary>
        private const float PhoneSize = 0.12f;

        /// <summary>На сколько огонёк вынесен к арене и поднят над сиденьем, м.</summary>
        private const float PhoneReach = 0.35f;
        private const float PhoneHeight = 1.25f;

        /// <summary>Разброс роста зрителя множителем к модели пака.</summary>
        private const float FanScaleMin = 0.92f;
        private const float FanScaleMax = 1.06f;

        // ========== ШАХТЫ СВЕТА ==========

        /// <summary>На сколько низ шахты держится выше запретной зоны, м.</summary>
        private const float ShaftFloorMargin = 0.4f;

        /// <summary>Радиус шахты у прожектора и у нижнего обреза, м.</summary>
        private const float ShaftTopRadius = 0.35f;
        private const float ShaftBottomRadius = 1.5f;

        /// <summary>Сколько граней у шахты. Восьми хватает: она полупрозрачная и без контура.</summary>
        private const int ShaftSides = 12;

        /// <summary>Яркость шахты у прожектора. К низу гаснет вершинным цветом.</summary>
        private const float ShaftOpacity = 0.55f;

        private const string ShaftMaterialPath =
            "Assets/_Project/Materials/HoleInWall/HIW_LightShaft.mat";
        private const string ShaftMeshPath =
            "Assets/_Project/Art/HoleInWall/HIW_LightShaft.asset";

        private const string ShaftShaderName = "Igruha/Light Shaft";

        // ========== ТРАНСЛЯЦИЯ ==========

        /// <summary>Где по Z стоят камеры на настиле. В метрах от линии проверки.</summary>
        private static readonly float[] BroadcastZ = { 1.5f, 13.5f };

        /// <summary>Насколько камера отставлена от края настила внутрь, м.</summary>
        private const float BroadcastInset = 1.3f;

        [MenuItem("Igruha/Дырка в стене/Декор")]
        public static void BuildFromMenu()
        {
            HoleInWallConfig config = AssetDatabase.LoadAssetAtPath<HoleInWallConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError($"Не найден конфиг {ConfigPath}");
                return;
            }

            GameObject arena = GameObject.Find(ArenaRoot);
            Transform studio = arena == null ? null : arena.transform.Find(StudioRoot);
            if (studio == null)
            {
                Debug.LogError($"В сцене нет {ArenaRoot}/{StudioRoot} — построй павильон");
                return;
            }

            int pieces = Build(studio, config);

            EditorUtility.SetDirty(studio.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(studio.gameObject.scene);

            Debug.Log($"🎪 Декор «Дырки в стене»: {pieces} предметов в {StudioRoot}/{DecorGroup} — " +
                      "публика, шахты света, трансляционные камеры");
        }

        /// <summary>
        /// Доклеить декор к павильону. Идемпотентно: своя группа сносится
        /// целиком и строится заново, остального павильона не касается.
        /// </summary>
        /// <returns>Сколько предметов получилось — для отчёта и приёмки.</returns>
        internal static int Build(Transform studio, HoleInWallConfig config)
        {
            Transform existing = studio.Find(DecorGroup);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var decor = new GameObject(DecorGroup).transform;
            decor.SetParent(studio, false);

            // Один и тот же посев: декор обязан быть одинаковым у обоих
            // разработчиков и в раздатке, иначе «у меня по-другому стоит».
            var rng = new System.Random(486);

            BuildCrowd(decor, studio, rng);
            BuildLightShafts(decor, config);
            BuildBroadcast(decor, config);
            BuildBackstage(decor, config, rng);

            Strip(decor);
            return decor.GetComponentsInChildren<Renderer>(true).Length;
        }

        // ========== ПУБЛИКА ==========

        /// <summary>
        /// Зрители на секциях трибуны. Позиции берутся у самих секций, а не
        /// считаются заново: трибуны строит <see cref="HoleInWallEnvironment"/>,
        /// и второй расчёт разъехался бы с ними при первой же правке.
        /// </summary>
        private static void BuildCrowd(Transform parent, Transform studio, System.Random rng)
        {
            Transform stands = studio.Find("Stands");
            if (stands == null)
            {
                Debug.LogWarning("Трибун в павильоне нет — публику ставить некуда");
                return;
            }

            var group = new GameObject("Crowd").transform;
            group.SetParent(parent, false);

            // ⚠️ Не Tone.Stage. Первым прогоном зал покрасили именно им —
            // это самый тёмный тон палитры, тот же, что у трибуны и у стен
            // студии. Зрители слились с фоном, и в кадре их не было вовсе:
            // сорок человек, которых не видно. Сталь читается силуэтом
            // и на тёмной трибуне, и против светодиодного борта.
            Material body = HoleInWallPaletteAssets.Get(Tone.Metal);
            Material phone = HoleInWallPaletteAssets.Get(Tone.NeonCyan);

            // Восемь персонажей лежат в одном FBX; берутся их меши — почему
            // именно меши, а не объекты, разобрано в LoadCrowdMeshes.
            Mesh[] kinds = LoadCrowdMeshes();

            for (int i = 0; i < stands.childCount; i++)
            {
                Transform bench = stands.GetChild(i);
                if (!bench.name.StartsWith("Bleacher_"))
                {
                    continue;
                }

                Renderer benchRenderer = bench.GetComponent<Renderer>();
                if (benchRenderer == null)
                {
                    continue;
                }

                Bounds b = benchRenderer.bounds;
                float standY = b.max.y;

                for (int p = 0; p < CrowdPerBleacher; p++)
                {
                    float alongZ = (p + 0.5f) / CrowdPerBleacher;
                    float z = Mathf.Lerp(b.min.z, b.max.z, alongZ) + Jitter(rng, 0.25f);
                    float x = b.center.x + Jitter(rng, b.size.x * 0.22f);
                    var at = new Vector3(x, standY, z);

                    // Зритель смотрит на арену: она в середине по X, трибуны
                    // по краям, поэтому разворот — от знака X, а не наугад.
                    float yaw = (x < 0f ? 90f : -90f) + Jitter(rng, 18f);
                    SpawnFan(group, kinds, body, at, yaw, i, p, rng);

                    if (rng.NextDouble() >= CrowdPhoneShare)
                    {
                        continue;
                    }

                    // Огонёк телефона поднят к лицу и вынесен к арене: это
                    // единственное, что вообще видно в тёмной трибуне.
                    var light = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    light.name = $"Phone_{i}_{p}";
                    light.transform.SetParent(group, true);
                    light.transform.position =
                        new Vector3(x + Mathf.Sign(-x) * PhoneReach, standY + PhoneHeight, z);
                    light.transform.localScale = Vector3.one * PhoneSize;
                    light.GetComponent<Renderer>().sharedMaterial = phone;
                }
            }

        }

        /// <summary>
        /// Достать из пака меши персонажей — <b>именно меши, а не объекты</b>.
        ///
        /// ⚠️ Первым прогоном зрители копировались как дети FBX целиком,
        /// и это <b>уронило редактор на запекании</b>. Персонажи пака скинненые:
        /// их <c>SkinnedMeshRenderer</c> ссылается на кости, лежащие в ветке
        /// <c>Root</c> того же FBX. Скопировав одного персонажа без скелета,
        /// получаешь рендерер с висячим массивом костей: в кадре он
        /// отрисовывается случайно, в позе привязки, а обходчик зависимостей
        /// на нём падает.
        ///
        /// Зал статичен и анимировать его незачем, поэтому берётся
        /// <c>sharedMesh</c> и ставится обычным <c>MeshRenderer</c>: поза
        /// привязки та же, зависимости — только сам меш, и запекание проходит.
        /// </summary>
        private static Mesh[] LoadCrowdMeshes()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(CrowdPath);
            if (source == null)
            {
                Debug.LogWarning($"Персонажей пака нет ({CrowdPath}) — зал будет капсулами");
                return System.Array.Empty<Mesh>();
            }

            var meshes = new System.Collections.Generic.List<Mesh>(8);
            foreach (SkinnedMeshRenderer skin in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh != null)
                {
                    meshes.Add(skin.sharedMesh);
                }
            }

            return meshes.ToArray();
        }

        /// <summary>
        /// Поставить одного зрителя. Персонаж пака, а при его отсутствии —
        /// капсула того же роста.
        /// </summary>
        private static void SpawnFan(Transform parent, Mesh[] kinds, Material body,
            Vector3 at, float yaw, int bench, int seat, System.Random rng)
        {
            if (kinds.Length == 0)
            {
                float height = Mathf.Lerp(CrowdHeightMin, CrowdHeightMax, (float)rng.NextDouble());
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = $"Fan_{bench}_{seat}";
                capsule.transform.SetParent(parent, true);
                capsule.transform.position = at + Vector3.up * height * 0.5f;
                capsule.transform.localScale = new Vector3(CrowdWidth, height * 0.5f, CrowdWidth);
                capsule.GetComponent<Renderer>().sharedMaterial = body;
                return;
            }

            var fan = new GameObject($"Fan_{bench}_{seat}");
            fan.transform.SetParent(parent, false);
            fan.transform.position = at;
            fan.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // Рост чуть разный, иначе зал читается штампом. Ниже игрока
            // намеренно: публика не должна путаться с участником.
            float scale = Mathf.Lerp(FanScaleMin, FanScaleMax, (float)rng.NextDouble());
            fan.transform.localScale = Vector3.one * scale;

            fan.AddComponent<MeshFilter>().sharedMesh = kinds[rng.Next(kinds.Length)];
            fan.AddComponent<MeshRenderer>().sharedMaterial = body;
        }

        private static float Jitter(System.Random rng, float amount) =>
            (float)(rng.NextDouble() * 2.0 - 1.0) * amount;

        // ========== ШАХТЫ СВЕТА ==========

        /// <summary>
        /// Видимый луч под каждым прожектором. Прожекторы есть с 4.3, но URP
        /// не рисует объём, поэтому свет в кадре не читался вовсе: софиты
        /// висели, а воздух под ними оставался пустым.
        ///
        /// Луч — усечённый конус с вершинным альфа-градиентом, растущим вверх
        /// к прожектору. Низ обрезан над запретной зоной арены, и это не
        /// компромисс: конус до пола лёг бы поверх выреза.
        /// </summary>
        private static void BuildLightShafts(Transform parent, HoleInWallConfig config)
        {
            Light[] spots = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var group = new GameObject("Shafts").transform;
            group.SetParent(parent, false);

            float zoneTop = config.PlatformSurfaceY + config.WallHeight + 0.6f;
            float bottomY = zoneTop + ShaftFloorMargin;

            Mesh mesh = LoadOrBuildShaftMesh();
            int built = 0;

            for (int i = 0; i < spots.Length; i++)
            {
                Light spot = spots[i];
                if (spot.type != LightType.Spot)
                {
                    continue;
                }

                float length = spot.transform.position.y - bottomY;
                if (length <= 0.1f)
                {
                    continue;
                }

                var shaft = new GameObject($"Shaft_{spot.gameObject.name}");
                shaft.transform.SetParent(group, false);
                shaft.transform.position = spot.transform.position;
                shaft.transform.rotation = spot.transform.rotation;
                shaft.transform.localScale = new Vector3(1f, 1f, length);

                shaft.AddComponent<MeshFilter>().sharedMesh = mesh;

                // Материал на прожектор свой: цвет луча обязан совпасть
                // с цветом дорожки, иначе луч выдаёт себя подделкой.
                Material beam = ShaftMaterial(spot.color);
                if (beam == null)
                {
                    Object.DestroyImmediate(shaft);
                    return;
                }

                shaft.AddComponent<MeshRenderer>().sharedMaterial = beam;
                built++;
            }

            if (built == 0)
            {
                Debug.LogWarning("Прожекторов в сцене нет — шахты света ставить не от чего");
            }
        }

        /// <summary>
        /// Меш луча: усечённый конус вдоль +Z длиной 1, чтобы длина задавалась
        /// масштабом объекта. Альфа лежит в вершинном цвете — у вершины 1,
        /// у нижнего обреза 0, поэтому луч растворяется, а не кончается ребром.
        /// </summary>
        private static Mesh LoadOrBuildShaftMesh()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(ShaftMeshPath);
            if (existing != null)
            {
                return existing;
            }

            var vertices = new Vector3[ShaftSides * 2];
            var colors = new Color[vertices.Length];
            var triangles = new int[ShaftSides * 6];

            for (int i = 0; i < ShaftSides; i++)
            {
                float a = i / (float)ShaftSides * Mathf.PI * 2f;
                float cos = Mathf.Cos(a);
                float sin = Mathf.Sin(a);

                vertices[i] = new Vector3(cos * ShaftTopRadius, sin * ShaftTopRadius, 0f);
                vertices[i + ShaftSides] = new Vector3(cos * ShaftBottomRadius, sin * ShaftBottomRadius, 1f);

                // Градиент лежит в RGB, а не в альфе. При аддитивном
                // смешивании чёрный не добавляет ничего, то есть гаснет
                // гарантированно; альфа же в аддитиве у разных вариантов
                // URP учитывается по-разному — на этом шахты и вышли
                // непрозрачными абажурами с первого прогона.
                colors[i] = new Color(1f, 1f, 1f, 1f);
                colors[i + ShaftSides] = new Color(0f, 0f, 0f, 1f);

                int next = (i + 1) % ShaftSides;
                int t = i * 6;
                triangles[t + 0] = i;
                triangles[t + 1] = next;
                triangles[t + 2] = i + ShaftSides;
                triangles[t + 3] = next;
                triangles[t + 4] = next + ShaftSides;
                triangles[t + 5] = i + ShaftSides;
            }

            var mesh = new Mesh { name = "HIW_LightShaft" };
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            AssetDatabase.CreateAsset(mesh, ShaftMeshPath);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        /// <summary>
        /// Полупрозрачный аддитивный материал луча под цвет дорожки. Один
        /// ассет на цвет: четыре дорожки — четыре материала, а не четыре копии
        /// одного.
        /// </summary>
        private static Material ShaftMaterial(Color tint)
        {
            string path = ShaftMaterialPath.Replace(
                ".mat", $"_{Mathf.RoundToInt(tint.r * 255):X2}{Mathf.RoundToInt(tint.g * 255):X2}{Mathf.RoundToInt(tint.b * 255):X2}.mat");

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            // Свой шейдер, а не URP/Particles/Unlit: у последнего вариант
            // выбирается кейвордами редактора материала, и из кода он остаётся
            // непрозрачным — шахты так и вышли абажурами с первого прогона.
            // Разбор в шапке Art/Shaders/LightShaft.shader.
            var shader = Shader.Find(ShaftShaderName);
            if (shader == null)
            {
                Debug.LogError($"Шейдера «{ShaftShaderName}» нет в проекте — луч рисовать нечем");
                return null;
            }

            var material = new Material(shader);
            material.name = System.IO.Path.GetFileNameWithoutExtension(path);
            material.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, 1f));
            material.SetFloat("_Intensity", ShaftOpacity);

            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            return material;
        }

        // ========== ТРАНСЛЯЦИЯ ==========

        /// <summary>
        /// Камеры на боковых настилах. Берутся из запечённого арта, а не из
        /// пака: обе модели 4.6 уже скопировал в проект, и в сцене они пока
        /// не использованы ни раз.
        /// </summary>
        private static void BuildBroadcast(Transform parent, HoleInWallConfig config)
        {
            var body = AssetDatabase.LoadAssetAtPath<GameObject>(CameraBodyPath);
            var tripod = AssetDatabase.LoadAssetAtPath<GameObject>(CameraTripodPath);
            if (body == null || tripod == null)
            {
                Debug.LogWarning("Моделей трансляционной камеры нет в запечённом арте — пропускаю");
                return;
            }

            var group = new GameObject("Broadcast").transform;
            group.SetParent(parent, false);

            Material metal = HoleInWallPaletteAssets.Get(Tone.Metal);

            // Настил лежит снаружи бортика; ставим на его верхнюю грань.
            float deckX = config.ArenaWidth * 0.5f + BroadcastInset + config.SideMargin;
            float deckY = config.WaterSurfaceY + config.PoolDepth * 0.25f;

            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -deckX : deckX;
                for (int i = 0; i < BroadcastZ.Length; i++)
                {
                    float z = BroadcastZ[i];
                    var rig = new GameObject($"Camera_{(side == 0 ? "L" : "R")}_{i}").transform;
                    rig.SetParent(group, false);
                    rig.position = new Vector3(x, deckY, z);
                    rig.rotation = Quaternion.LookRotation(
                        new Vector3(-x, 0f, config.CheckLineZ - z).normalized, Vector3.up);

                    Place(tripod, rig, Vector3.zero, metal);
                    Place(body, rig, new Vector3(0f, 1.25f, 0f), metal);
                }
            }
        }

        private static void Place(GameObject source, Transform parent, Vector3 offset, Material material)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.transform.localPosition = offset;
            instance.transform.localRotation = Quaternion.identity;

            Paint(instance, material);
        }

        /// <summary>
        /// Перекрасить модель пака в палитру игры.
        ///
        /// Обязательно, а не для красоты: со своими материалами модель тянет
        /// за собой текстуры пака, и запекание 4.6 перестаёт давать ноль
        /// ссылок на <c>Assets/Synty/**</c>. Палитра у игры одна.
        /// </summary>
        private static void Paint(GameObject instance, Material material)
        {
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }

                renderer.sharedMaterials = materials;
            }
        }

        // ========== ЗАКУЛИСЬЕ ==========

        /// <summary>
        /// Наполнение пустого пола студии — за камерой и за стеной.
        ///
        /// Между бассейном и стенами павильона лежало по десять с лишним метров
        /// голого пола: у ближнего края за отходом камеры и у дальнего за
        /// стартом стены. В кадре при обороте камеры это читалось пустотой,
        /// в которой студия просто кончается.
        ///
        /// <b>Обе запретные зоны обойдены числами.</b> Полоса перед камерой
        /// занимает Z от <c>ArenaNearZ − CameraClearance</c> до
        /// <c>ArenaNearZ</c>, то есть −13.3…−7.6 — закулисье ставится ЗА ней,
        /// от <see cref="BackstageNearZ"/>. Пятно арены кончается на
        /// <c>ArenaFarZ</c> = 23.0, поэтому дальняя группа начинается с
        /// <see cref="BackstageFarZ"/>.
        /// </summary>
        private static void BuildBackstage(Transform parent, HoleInWallConfig config, System.Random rng)
        {
            var group = new GameObject("Backstage").transform;
            group.SetParent(parent, false);

            Material metal = HoleInWallPaletteAssets.Get(Tone.Metal);
            Material stage = HoleInWallPaletteAssets.Get(Tone.Stage);

            float floorY = config.WaterSurfaceY - config.PoolDepth + FloorLift;
            float nearZ = config.ArenaNearZ - config.CameraClearance - BackstageNearGap;
            float farZ = config.ArenaFarZ + BackstageFarGap;

            var speaker = AssetDatabase.LoadAssetAtPath<GameObject>(SpeakerPath);
            var speakerTall = AssetDatabase.LoadAssetAtPath<GameObject>(SpeakerTallPath);
            var screen = AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath);

            // Колонки стоят ВДОЛЬ арены, а не за ней. Первым прогоном их
            // расставили по дальнему и ближнему краю пола — и обе группы
            // оказались за стенами павильона, то есть невидимы из игры.
            // Место, которое видно всегда, — торцы боковых настилов.
            float deckX = config.ArenaWidth * 0.5f + config.SideMargin + DeckSpeakerOut;
            float[] deckZ = { config.PlatformBackZ - 2f, config.ArenaFarZ - 6f };

            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -deckX : deckX;
                for (int i = 0; i < deckZ.Length; i++)
                {
                    StackSpeakers(group, speaker, speakerTall, metal,
                        new Vector3(x, config.WaterSurfaceY + DeckTopLift, deckZ[i]),
                        side == 0 ? 90f : -90f, rng);
                }
            }

            // И ещё пара у дальнего торца — чтобы задник не обрывался пустотой.
            StackSpeakers(group, speaker, speakerTall, metal, new Vector3(-9f, floorY, farZ), 180f, rng);
            StackSpeakers(group, speaker, speakerTall, metal, new Vector3(9f, floorY, farZ), 180f, rng);

            // Экраны у дальней стены: задник светодиодный и ровный, а экран
            // на стойке ломает его плоскость.
            if (screen != null)
            {
                float[] screenX = { -13f, 13f };
                for (int i = 0; i < screenX.Length; i++)
                {
                    var rig = new GameObject("Screen_" + i).transform;
                    rig.SetParent(group, false);
                    rig.position = new Vector3(screenX[i], floorY + ScreenLift, farZ + 3f);
                    rig.rotation = Quaternion.Euler(0f, 180f, 0f);
                    rig.localScale = Vector3.one * ScreenScale;
                    Place(screen, rig, Vector3.zero, stage);
                }
            }

            BuildSigns(group, config, floorY);
        }

        /// <summary>Стопка из двух колонок: широкая снизу, узкая сверху.</summary>
        private static void StackSpeakers(Transform parent, GameObject wide, GameObject tall,
            Material material, Vector3 at, float yaw, System.Random rng)
        {
            if (wide == null)
            {
                return;
            }

            var rig = new GameObject("Speakers_" + at.x.ToString("0") + "_" + at.z.ToString("0")).transform;
            rig.SetParent(parent, false);
            rig.position = at;
            rig.rotation = Quaternion.Euler(0f, yaw + Jitter(rng, 6f), 0f);

            Place(wide, rig, Vector3.zero, material);
            Place(tall != null ? tall : wide, rig, new Vector3(0f, SpeakerStackLift, 0f), material);
        }

        /// <summary>
        /// Неоновые вывески пака на стенах павильона. Красятся неоном палитры,
        /// а не своими материалами: собственные притащили бы за собой текстуры
        /// пака, а палитра у игры одна.
        /// </summary>
        private static void BuildSigns(Transform parent, HoleInWallConfig config, float floorY)
        {
            Material pink = HoleInWallPaletteAssets.Get(Tone.NeonPink);
            Material cyan = HoleInWallPaletteAssets.Get(Tone.NeonCyan);

            float nearWallZ = config.ArenaNearZ - config.CameraClearance - SignWallGap;
            float halfWidth = config.ArenaWidth * 0.5f + SignSideOut;

            for (int i = 0; i < SignPaths.Length; i++)
            {
                var sign = AssetDatabase.LoadAssetAtPath<GameObject>(SignPaths[i]);
                if (sign == null)
                {
                    continue;
                }

                bool left = i % 2 == 0;
                var rig = new GameObject("Sign_" + i).transform;
                rig.SetParent(parent, false);
                rig.position = new Vector3(
                    left ? -halfWidth : halfWidth,
                    floorY + SignLift + i * SignStep,
                    nearWallZ + (i < 2 ? 0f : SignRowStep));
                rig.rotation = Quaternion.Euler(0f, left ? 90f : -90f, 0f);
                rig.localScale = Vector3.one * SignScale;

                Place(sign, rig, Vector3.zero, i % 2 == 0 ? pink : cyan);
            }
        }

        /// <summary>Насколько закулисье поднято над дном бассейна: пол студии лежит выше дна.</summary>
        private const float FloorLift = 2.16f;

        /// <summary>Отступ закулисья за полосой перед камерой и за пятном арены, м.</summary>
        private const float BackstageNearGap = 2.5f;
        private const float BackstageFarGap = 2.5f;

        private const float SpeakerStackLift = 2.1f;

        /// <summary>Насколько колонки вынесены наружу за край арены и подняты на настил, м.</summary>
        private const float DeckSpeakerOut = 1.2f;
        private const float DeckTopLift = 1.6f;
        private const float ScreenLift = 3.5f;
        private const float ScreenScale = 3f;

        private const float SignWallGap = 5f;
        private const float SignSideOut = 3f;
        private const float SignLift = 4f;
        private const float SignStep = 1.6f;
        private const float SignRowStep = 6f;
        private const float SignScale = 2f;

        // ========== ПРАВИЛА ПАВИЛЬОНА ==========

        /// <summary>
        /// Снять с декора всё, что павильону запрещено: коллайдеры, отброс
        /// теней и чужой слой. Делается одним проходом в конце, а не на каждой
        /// коробке: так правило одно и его нельзя забыть на новом куске —
        /// тот же приём, что в <see cref="HoleInWallEnvironment"/>.
        /// </summary>
        private static void Strip(Transform root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i], true);
            }

            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Object.DestroyImmediate(lights[i], true);
            }

            int layer = LayerMask.NameToLayer("Default");
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.gameObject.layer = layer;
            }
        }
    }
}
