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

        private const string CameraBodyPath =
            "Assets/_Project/Art/HoleInWall/PolygonShops/Models/SM_Prop_Computer_Camera_DSLR_01.fbx";
        private const string CameraTripodPath =
            "Assets/_Project/Art/HoleInWall/PolygonShops/Models/SM_Prop_Computer_Camera_Tripod_01.fbx";

        // ========== ПУБЛИКА ==========

        /// <summary>Сколько зрителей на одной секции трибуны.</summary>
        private const int CrowdPerBleacher = 3;

        /// <summary>Рост зрителя, м. Ниже игрока: публика не должна читаться как участник.</summary>
        private const float CrowdHeightMin = 0.95f;
        private const float CrowdHeightMax = 1.35f;

        /// <summary>Толщина зрителя, м.</summary>
        private const float CrowdWidth = 0.42f;

        /// <summary>Доля зрителей, подсвеченных «телефоном». Живой зал снимает на телефоны.</summary>
        private const float CrowdPhoneShare = 0.28f;

        /// <summary>Размер огонька телефона, м.</summary>
        private const float PhoneSize = 0.12f;

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

            Material body = HoleInWallPaletteAssets.Get(Tone.Stage);
            Material phone = HoleInWallPaletteAssets.Get(Tone.NeonCyan);

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
                    float height = Mathf.Lerp(CrowdHeightMin, CrowdHeightMax, (float)rng.NextDouble());

                    var person = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    person.name = $"Fan_{i}_{p}";
                    person.transform.SetParent(group, true);
                    person.transform.position = new Vector3(x, standY + height * 0.5f, z);
                    person.transform.localScale = new Vector3(CrowdWidth, height * 0.5f, CrowdWidth);
                    person.GetComponent<Renderer>().sharedMaterial = body;

                    if (rng.NextDouble() >= CrowdPhoneShare)
                    {
                        continue;
                    }

                    // Огонёк телефона поднят к лицу и вынесен к арене: это
                    // единственное, что вообще видно в тёмной трибуне.
                    var light = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    light.name = $"Phone_{i}_{p}";
                    light.transform.SetParent(group, true);
                    float toArena = Mathf.Sign(-x) * (CrowdWidth * 0.5f + PhoneSize);
                    light.transform.position =
                        new Vector3(x + toArena, standY + height * 0.82f, z);
                    light.transform.localScale = Vector3.one * PhoneSize;
                    light.GetComponent<Renderer>().sharedMaterial = phone;
                }
            }
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
