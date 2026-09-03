using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Igruha.Minigames.HoleInWall;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Поверхность воды «Дырки в стене»: подробная сетка плюс материал на
    /// шейдере <c>Igruha/Stylized Water</c>.
    ///
    /// До этого вода была примитивным <c>Cube</c>, растянутым в плиту 43×31 м.
    /// У куба четыре вершины на грань, поэтому никакие волны на нём невозможны
    /// в принципе, а материал стоял на URP/Lit с ровным цветом. В кадре это
    /// занимало нижние две трети экрана и читалось крашеным полом — самая
    /// большая поверхность игры не выглядела водой вовсе.
    ///
    /// Здесь строится сетка с шагом <see cref="CellSize"/>, и волны шейдера
    /// получают на чём играть.
    ///
    /// <b>Мешу нужен ассет, а не рантайм-объект.</b> Меш, созданный кодом
    /// и просто присвоенный объекту сцены, не переживает перезагрузку домена:
    /// сцена сохранит ссылку в никуда, и вода пропадёт. Поэтому сетка
    /// сохраняется в <see cref="MeshPath"/> и оттуда же переиспользуется.
    /// </summary>
    /// <remarks>
    /// <b>Коллайдера у воды нет и не появляется.</b> В неё падают, а не стоят
    /// на ней, и камера обязана проходить её насквозь (igruha/CLAUDE.md, 2a).
    /// Слой — <c>Default</c>, как и был.
    ///
    /// <b>Волны на игру не влияют.</b> Амплитуда 0.08 м живёт целиком в шейдере;
    /// ни <c>KillZone_Water</c>, ни проверка погружения в <c>HoleInWallAudio</c>
    /// по плоскому <c>WaterSurfaceY</c> о ней не знают и знать не должны.
    /// </remarks>
    public static class HoleInWallWater
    {
        private const string ConfigPath =
            "Assets/_Project/Settings/Gameplay/Minigames/HoleInWallConfig.asset";

        private const string MeshPath = "Assets/_Project/Art/HoleInWall/HIW_WaterSurface.asset";
        private const string MaterialPath = "Assets/_Project/Materials/HoleInWall/HIW_Water.mat";
        private const string ShaderName = "Igruha/Stylized Water";

        /// <summary>
        /// Шаг сетки, м. 0.4 — компромисс, снятый по числам: при длине самой
        /// короткой волны 2.6 м это шесть вершин на волну, чего хватает на
        /// гладкий гребень, а вся вода арены обходится примерно в 8.5 тысяч
        /// вершин — меньше, чем один персонаж.
        /// </summary>
        private const float CellSize = 0.4f;

        /// <summary>Та же подгонка под бортики, что была у плиты-куба.</summary>
        private const float RimInset = 0.4f;

        [MenuItem("Igruha/Дырка в стене/Вода")]
        public static void Build()
        {
            HoleInWallConfig config = AssetDatabase.LoadAssetAtPath<HoleInWallConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError($"Не найден конфиг {ConfigPath}");
                return;
            }

            GameObject water = FindWater();
            if (water == null)
            {
                Debug.LogError("В сцене нет объекта _Arena/Pool/Water — построй арену");
                return;
            }

            Mesh mesh = Dress(water, config);

            EditorUtility.SetDirty(water);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(water.scene);

            Debug.Log($"🌊 Вода «Дырки в стене»: сетка {mesh.vertexCount} вершин, " +
                      $"{mesh.triangles.Length / 3} треугольников, шаг {CellSize:0.00} м, " +
                      $"материал на «{ShaderName}»");
        }

        /// <summary>
        /// Одеть объект воды: сетка, материал, слой и снятые тени. Зовётся и
        /// из меню, и из <c>HoleInWallArenaBuilder</c>, чтобы пересборка арены
        /// не возвращала примитивную плиту.
        /// </summary>
        public static Mesh Dress(GameObject water, HoleInWallConfig config)
        {
            Mesh mesh = BuildSurfaceMesh(config);
            Material material = BuildMaterial();
            Apply(water, mesh, material, config);
            return mesh;
        }

        /// <summary>
        /// Сетка воды в локальных координатах объекта: плоскость XZ с началом
        /// в центре. Размер повторяет плиту, которую строил
        /// <c>HoleInWallArenaBuilder.BuildPool</c>, чтобы вода по-прежнему
        /// доходила до бортиков и не оставляла щели.
        /// </summary>
        public static Mesh BuildSurfaceMesh(HoleInWallConfig config)
        {
            float width = config.ArenaWidth - RimInset;
            float depth = config.ArenaDepth - RimInset;

            int columns = Mathf.Max(2, Mathf.RoundToInt(width / CellSize));
            int rows = Mathf.Max(2, Mathf.RoundToInt(depth / CellSize));

            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var uvs = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[columns * rows * 6];

            for (int z = 0; z <= rows; z++)
            {
                float tz = z / (float)rows;
                for (int x = 0; x <= columns; x++)
                {
                    float tx = x / (float)columns;
                    int index = z * (columns + 1) + x;

                    vertices[index] = new Vector3((tx - 0.5f) * width, 0f, (tz - 0.5f) * depth);
                    normals[index] = Vector3.up;

                    // UV в метрах, а не в долях: шейдер по ним ничего не тайлит,
                    // но метровая развёртка не врёт, если сетку когда-нибудь
                    // возьмут под текстуру.
                    uvs[index] = new Vector2(tx * width, tz * depth);
                }
            }

            int t = 0;
            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int corner = z * (columns + 1) + x;
                    int above = corner + columns + 1;

                    triangles[t++] = corner;
                    triangles[t++] = above;
                    triangles[t++] = corner + 1;

                    triangles[t++] = corner + 1;
                    triangles[t++] = above;
                    triangles[t++] = above + 1;
                }
            }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh == null)
            {
                mesh = new Mesh();
                System.IO.Directory.CreateDirectory(
                    System.IO.Path.GetDirectoryName(FullPath(MeshPath)));
                AssetDatabase.CreateAsset(mesh, MeshPath);
            }

            mesh.Clear();
            mesh.name = "HIW_WaterSurface";
            mesh.indexFormat = vertices.Length > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;

            // Границы задаются руками с запасом на высоту волны: иначе Unity
            // отсечёт воду по плоскому боксу, стоит камере посмотреть вдоль
            // поверхности, и дальний край моргнёт.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(width, 1f, depth));

            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        /// <summary>
        /// Переводит существующий материал воды на свой шейдер и выставляет
        /// числа. Материал тот же самый: на него уже ссылается сцена, а цвет
        /// брался из палитры игры — терять эту связь незачем.
        /// </summary>
        private static Material BuildMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"Шейдер «{ShaderName}» не найден — вода останется прежней");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            Color shallow = HoleInWallPalette.Water;
            Color deep = new Color(shallow.r * 0.18f, shallow.g * 0.30f, shallow.b * 0.46f, 1f);

            material.SetColor("_ShallowColor", new Color(shallow.r, shallow.g, shallow.b, 1f));
            material.SetColor("_DeepColor", deep);
            material.SetFloat("_DepthRange", 2.5f);
            material.SetFloat("_Opacity", 0.92f);

            material.SetColor("_FoamColor", new Color(0.85f, 0.97f, 1f, 1f));
            material.SetFloat("_FoamDepth", 0.55f);
            material.SetFloat("_FoamCutoff", 0.35f);
            material.SetFloat("_FoamSpeed", 1.6f);

            material.SetVector("_WaveA", new Vector4(1f, 0.35f, 0.18f, 7f));
            material.SetVector("_WaveB", new Vector4(-0.6f, 1f, 0.14f, 4.5f));
            material.SetVector("_WaveC", new Vector4(0.9f, -0.7f, 0.09f, 2.6f));
            material.SetFloat("_WaveSpeed", 0.55f);
            material.SetFloat("_WaveHeight", 0.08f);

            material.SetFloat("_RippleScale", 2.2f);
            material.SetFloat("_RippleSpeed", 1.1f);
            material.SetFloat("_RippleStrength", 0.35f);

            // Блик подобран под студийный свет 4.3: он холодный и резкий,
            // поэтому вода ловит его узкой полосой, а не общим сиянием.
            material.SetColor("_SpecColorTint", new Color(0.75f, 0.95f, 1f, 1f));
            material.SetFloat("_SpecPower", 220f);
            material.SetFloat("_SpecStrength", 1.4f);
            material.SetFloat("_FresnelPower", 4f);
            material.SetFloat("_FresnelStrength", 0.28f);

            material.SetFloat("_RefractionStrength", 0.035f);

            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void Apply(GameObject water, Mesh mesh, Material material, HoleInWallConfig config)
        {
            // Сетка уже в метрах, поэтому масштаб снимается. У куба он держал
            // размер плиты, и оставленный, он растянул бы волны вместе с водой.
            water.transform.localScale = Vector3.one;
            water.transform.localRotation = Quaternion.identity;

            water.transform.localPosition = new Vector3(
                0f, config.WaterSurfaceY, (config.ArenaFarZ + config.ArenaNearZ) * 0.5f);

            MeshFilter filter = water.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = water.AddComponent<MeshFilter>();
            }

            filter.sharedMesh = mesh;

            MeshRenderer renderer = water.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = water.AddComponent<MeshRenderer>();
            }

            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

            // Вода не отбрасывает и не принимает тени: она прозрачная и лежит
            // под всей ареной, а тень от неё легла бы на дно бассейна плитой.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Collider collider = water.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider, true);
            }
        }

        private static GameObject FindWater()
        {
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = root.transform.Find("Pool/Water");
                if (found != null)
                {
                    return found.gameObject;
                }
            }

            return null;
        }

        private static string FullPath(string assetPath) =>
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath), assetPath);
    }
}
