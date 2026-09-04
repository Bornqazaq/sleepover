using System.Reflection;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Minigames.HoleInWall;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Стенд вырезов: собирает настоящую стену в отдельной сцене, режет её
    /// вырезом под конкретного персонажа и кладёт рядом его силуэт.
    ///
    /// Меню: <c>Igruha/Дырка в стене/Стенд: проверить вырезы</c>. На выходе
    /// один лист PNG — 8 персонажей × 4 позы — и таблица в консоли.
    /// </summary>
    /// <remarks>
    /// <b>Проверяем геометрию, а не картинку в редакторе.</b> Стена собирается
    /// через рефлексию (<c>Awake</c> в редакторе не зовётся), ей скармливается
    /// настоящий <see cref="WallPattern"/>, а дальше растеризуется <b>её меш</b>
    /// и поверх — силуэт, снятый тем же скиннингом, что и при выпечке контура.
    /// Красный пиксель на листе значит ровно одно: кожа персонажа оказалась
    /// внутри полотна, то есть в эту дырку он не лезет.
    ///
    /// Пары стенд не гоняет намеренно: их контур — объединение двух силуэтов,
    /// и он по построению шире каждого из них. Проверять надо самый узкий
    /// случай, а это одиночка.
    /// </remarks>
    internal static class HoleInWallCutoutProof
    {
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/HoleInWallConfig.asset";

        /// <summary>Сторона пикселя листа, м.</summary>
        private const float PixelSize = 0.012f;

        /// <summary>Полуширина кадра одной клетки, м.</summary>
        private const float CellHalfWidth = 1.5f;

        /// <summary>Высота кадра одной клетки, м. Выше самой высокой позы плюс поля.</summary>
        private const float CellHeight = 2.7f;

        /// <summary>Толщина рамки клетки, пикселей. Ею же показывается вердикт.</summary>
        private const int BorderThickness = 2;

        private static readonly Color HoleColor = new Color(0.08f, 0.08f, 0.10f);
        private static readonly Color WallColor = new Color(0.38f, 0.38f, 0.40f);
        private static readonly Color BodyInHoleColor = new Color(0.25f, 0.85f, 0.35f);
        private static readonly Color BodyInWallColor = new Color(0.95f, 0.15f, 0.15f);
        private static readonly Color ColliderColor = new Color(0.25f, 0.55f, 1f);
        private static readonly Color PassColor = new Color(0.20f, 0.70f, 0.30f);
        private static readonly Color FailColor = new Color(0.90f, 0.10f, 0.10f);

        [MenuItem("Igruha/Дырка в стене/Стенд: проверить вырезы")]
        internal static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("HoleInWallCutoutProof: стенд не гоняется в плей-моде — он собирает свою сцену.");
                return;
            }

            var config = AssetDatabase.LoadAssetAtPath<HoleInWallConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError($"HoleInWallCutoutProof: нет конфига по пути {ConfigPath}.");
                return;
            }

            string[] prefabs = HoleInWallPoseClipBuilder.PrefabNames;
            string[] names = HoleInWallPoseClipBuilder.CharacterNames;
            int poses = HoleInWallPoseClipBuilder.PoseCount;

            int cellWidth = Mathf.CeilToInt(2f * CellHalfWidth / PixelSize);
            int cellHeight = Mathf.CeilToInt(CellHeight / PixelSize);
            var sheet = new Texture2D(cellWidth * poses, cellHeight * names.Length, TextureFormat.RGB24, false);

            var report = new System.Text.StringBuilder();
            report.AppendLine("HoleInWallCutoutProof: вырез против силуэта, м");
            report.AppendLine("  персонаж поза       вырез Ш×В, м   допуск  кожа в полотне, px");

            int failures = 0;
            GameObject wall = null;

            try
            {
                wall = BuildWall(config);
                var sweeping = wall.GetComponent<SweepingWall>();

                for (int c = 0; c < names.Length; c++)
                {
                    var cells = new Cell[poses];
                    if (!Capture(prefabs[c], names[c], config, sweeping, cells, poses, report, ref failures))
                    {
                        continue;
                    }

                    for (int p = 0; p < poses; p++)
                    {
                        Paint(sheet, cells[p], p * cellWidth, (names.Length - 1 - c) * cellHeight,
                            cellWidth, cellHeight);
                    }
                }

            }
            finally
            {
                if (wall != null)
                {
                    Object.DestroyImmediate(wall);
                }
            }

            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HoleInWallCutoutProof.png");
            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            report.AppendLine(failures == 0
                ? "  ✅ ни один силуэт не задевает полотно"
                : $"  ❌ силуэт задевает полотно в {failures} случаях из {names.Length * poses}");
            report.AppendLine($"  лист: {path}");
            report.AppendLine($"  * — допуск ужат до полуширины выреза: настроенный ±{config.HitTolerance:F2} м " +
                              "шире всей дырки, и игрок проходил бы, стоя на сплошной плите");

            string pairs = PairSheet(config, report);
            report.AppendLine($"  стена пары: {pairs}");

            // Таблицу пишем ещё и файлом: в консоли редактора длинная запись
            // обрезается, а сверять её приходится построчно.
            string log = System.IO.Path.ChangeExtension(path, ".txt");
            System.IO.File.WriteAllText(log, report.ToString());

            Debug.Log($"HoleInWallCutoutProof: {(failures == 0 ? "✅ чисто" : $"❌ {failures} провалов")}. " +
                      $"Лист {path}, таблица {log}");
        }

        /// <summary>
        /// Второй лист: стена пары целиком — обычная, после зеркального
        /// переворота и после смены формы.
        ///
        /// Одиночная стена режется одним вырезом, и триангуляция полотна с
        /// двумя выемками в кромке остаётся непроверенной — а это самый
        /// опасный случай: два контура в одном обходе. Здесь он и проверяется,
        /// вместе с обоими подвохами.
        /// </summary>
        private static string PairSheet(HoleInWallConfig config, System.Text.StringBuilder report)
        {
            const float PairPixel = 0.012f;
            int stripWidth = Mathf.CeilToInt(config.WallWidth / PairPixel);
            int stripHeight = Mathf.CeilToInt(config.WallHeight / PairPixel);
            var sheet = new Texture2D(stripWidth, stripHeight * 3, TextureFormat.RGB24, false);

            GameObject wall = null;

            try
            {
                wall = BuildWall(config);
                var sweeping = wall.GetComponent<SweepingWall>();

                // Fat и Шланга: самый широкий против самого высокого. Их
                // объединение — худший случай для контура пары.
                var shapes = new CutoutShapes(config, CutoutShapes.Compose("FatAnimator", "ShlangaAnimator"));
                sweeping.Configure(config, shapes);

                float spread = (shapes.Size(HoleInWallPose.HandsWide).x + shapes.Size(HoleInWallPose.Crouch).x)
                               * 0.5f + config.CutoutBridge;

                var pattern = new WallPattern(
                    new WallCutoutSpec(HoleInWallPose.HandsWide, -spread * 0.5f),
                    new WallCutoutSpec(HoleInWallPose.Crouch, spread * 0.5f),
                    true, WallTrick.Mirror, HoleInWallPose.HandsUp, HoleInWallPose.SideLunge);

                sweeping.Launch(pattern, NetworkClock.Now, 1f, double.MaxValue);
                PaintStrip(sheet, sweeping, config, stripWidth, stripHeight, 2, PairPixel);
                report.AppendLine($"  пара Fat+Шланга, обычная стена     разнос {spread:F2} м, {surfaceFacts}");

                Invoke(sweeping, "RebuildShape", true);
                PaintStrip(sheet, sweeping, config, stripWidth, stripHeight, 1, PairPixel);
                report.AppendLine($"  та же стена после переворота       {surfaceFacts}");

                var morph = new WallPattern(
                    new WallCutoutSpec(HoleInWallPose.HandsWide, -spread * 0.5f),
                    new WallCutoutSpec(HoleInWallPose.Crouch, spread * 0.5f),
                    true, WallTrick.Morph, HoleInWallPose.HandsUp, HoleInWallPose.SideLunge);

                sweeping.Launch(morph, NetworkClock.Now, 1f, double.MaxValue);
                Invoke(sweeping, "RebuildShape", true);
                PaintStrip(sheet, sweeping, config, stripWidth, stripHeight, 0, PairPixel);
                report.AppendLine($"  та же стена после смены формы      {surfaceFacts}");
            }
            finally
            {
                if (wall != null)
                {
                    Object.DestroyImmediate(wall);
                }
            }

            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HoleInWallCutoutProofPair.png");
            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            return path;
        }

        /// <summary>Положить на лист одну полосу: стена целиком по своей ширине.</summary>
        private static void PaintStrip(Texture2D sheet, SweepingWall wall, HoleInWallConfig config,
            int width, int height, int row, float pixel)
        {
            var grid = new bool[width * height];
            var boxes = new bool[width * height];
            float half = config.WallWidth * 0.5f;

            FillWall(wall, grid, width, height, half, pixel);
            FillColliders(wall, boxes, width, height, half, pixel);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    Color color = boxes[index] ? ColliderColor : grid[index] ? WallColor : HoleColor;
                    sheet.SetPixel(x, row * height + y, color);
                }
            }
        }

        /// <summary>Одна клетка листа: что где оказалось.</summary>
        private struct Cell
        {
            public bool[] Wall;
            public bool[] Body;
            public bool[] Collider;
            public int Overlap;
        }

        /// <summary>
        /// Собрать стену так, как её собирает арена: пять плит, два выреза.
        ///
        /// <c>Awake</c> в редакторе не зовётся, поэтому дёргаем его сами —
        /// именно он заводит полотно и пул коробок. Объекты стенда помечены
        /// <c>HideAndDontSave</c>: иначе открытая сцена геймдизайнера
        /// оказалась бы «изменённой» из-за прогона стенда.
        /// </summary>
        private static GameObject BuildWall(HoleInWallConfig config)
        {
            GameObject root = Hidden("ProofWall");
            var wall = root.AddComponent<SweepingWall>();
            string[] panels = { "panelLeft", "panelMiddle", "panelRight", "lintelFirst", "lintelSecond" };
            for (int i = 0; i < panels.Length; i++)
            {
                SetField(wall, panels[i], MakePanel(root.transform, panels[i]));
            }

            SetField(wall, "firstCutout", MakeCutout(root.transform, "Cutout_A"));
            SetField(wall, "secondCutout", MakeCutout(root.transform, "Cutout_B"));

            Invoke(wall, "Awake");
            return root;
        }

        private static GameObject Hidden(string name) =>
            EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);

        private static Transform MakePanel(Transform parent, string name)
        {
            GameObject go = Hidden(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            go.AddComponent<MeshRenderer>();
            return go.transform;
        }

        private static WallCutout MakeCutout(Transform parent, string name)
        {
            GameObject go = Hidden(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<WallCutout>();
        }

        /// <summary>Снять четыре клетки одного персонажа: вырез под него и его же силуэт.</summary>
        private static bool Capture(string prefabName, string characterName, HoleInWallConfig config,
            SweepingWall wall, Cell[] cells, int poses, System.Text.StringBuilder report, ref int failures)
        {
            string path = HoleInWallPoseClipBuilder.PlayerPrefabFolder + prefabName + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"HoleInWallCutoutProof: нет префаба {path}.");
                return false;
            }

            UnityEngine.SceneManagement.Scene preview =
                UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                var animator = instance.GetComponentInChildren<Animator>(true);
                var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (animator == null || skin == null)
                {
                    Debug.LogError($"HoleInWallCutoutProof ({characterName}): нет аватара или скиннед-меша.");
                    return false;
                }

                var shapes = new CutoutShapes(config, CutoutShapes.KeyOf(instance));
                wall.Configure(config, shapes);

                var measurer = new HoleInWallPoseClipBuilder.PoseMeasurer(animator, skin);

                for (int p = 0; p < poses; p++)
                {
                    var pose = (HoleInWallPose)(p + 1);

                    // Вырез ставим в центр дорожки: клетка листа кадрируется
                    // вокруг него, и сдвиг только мешал бы сравнению.
                    wall.Launch(new WallPattern(new WallCutoutSpec(pose, 0f), default, false,
                        WallTrick.None, pose, HoleInWallPose.None), NetworkClock.Now, 1f, double.MaxValue);

                    cells[p] = Rasterize(wall, measurer, HoleInWallPoseClipBuilder.MusclesOf(p));

                    Vector2 size = shapes.Size(pose);
                    if (cells[p].Overlap > 0 || frontTriangles == 0)
                    {
                        failures++;
                    }

                    float tolerance = shapes.Tolerance(pose);
                    report.AppendLine(
                        $"  {characterName,-8} {HoleInWallPoseClipBuilder.PoseTitles[p],-9} " +
                        $"{size.x,5:F2} × {size.y,5:F2}   ±{tolerance:F2}" +
                        (tolerance < config.HitTolerance - 0.001f ? "*" : " ") +
                        $"  {cells[p].Overlap,6}" +
                        (cells[p].Overlap > 0 ? "   ❌" : "   ✅") + $"   {surfaceFacts}");
                }

                return true;
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        /// <summary>
        /// Разложить по пикселям клетки: полотно стены, кожу персонажа
        /// и границы коробок столкновений.
        /// </summary>
        private static Cell Rasterize(SweepingWall wall, HoleInWallPoseClipBuilder.PoseMeasurer measurer,
            float[] muscles)
        {
            int width = Mathf.CeilToInt(2f * CellHalfWidth / PixelSize);
            int height = Mathf.CeilToInt(CellHeight / PixelSize);

            var cell = new Cell
            {
                Wall = new bool[width * height],
                Body = new bool[width * height],
                Collider = new bool[width * height]
            };

            FillWall(wall, cell.Wall, width, height);
            FillColliders(wall, cell.Collider, width, height);

            measurer.PlaceOnGround(muscles);
            measurer.RefreshBones();

            for (int v = 0; v < measurer.VertexCount; v++)
            {
                Vector3 world = measurer.WorldVertex(v);
                int x = Mathf.FloorToInt((world.x + CellHalfWidth) / PixelSize);
                int y = Mathf.FloorToInt(world.y / PixelSize);
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    continue;
                }

                int index = y * width + x;
                cell.Body[index] = true;
                if (cell.Wall[index])
                {
                    cell.Overlap++;
                }
            }

            return cell;
        }

        /// <summary>
        /// Залить полотно: берём его настоящий меш и растеризуем передние
        /// треугольники. Именно это и видит игрок — не пересчёт по контуру,
        /// а та геометрия, что уехала бы в кадр.
        /// </summary>
        private static void FillWall(SweepingWall wall, bool[] grid, int width, int height) =>
            FillWall(wall, grid, width, height, CellHalfWidth, PixelSize);

        private static void FillWall(SweepingWall wall, bool[] grid, int width, int height,
            float halfWidth, float pixel)
        {
            Transform surface = wall.transform.Find("Surface");
            var filter = surface != null ? surface.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                Debug.LogError("HoleInWallCutoutProof: у стены нет полотна — Awake не отработал");
                return;
            }

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            int front = 0;

            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]];
                Vector3 b = vertices[triangles[t + 1]];
                Vector3 c = vertices[triangles[t + 2]];

                // Только передняя грань: боковины вырезов и задняя грань
                // в проекции дали бы ту же заливку, но лишнюю работу.
                if (a.z >= 0f || b.z >= 0f || c.z >= 0f || normals[triangles[t]].z > -0.5f)
                {
                    continue;
                }

                front++;
                FillTriangle(grid, width, height, a, b, c, halfWidth, pixel);
            }

            frontTriangles = front;
            surfaceFacts = $"полотно {vertices.Length}в/{triangles.Length / 3}т, передних {front}, " +
                           $"нормаль z={normals[0].z:F2}";

            if (front == 0)
            {
                // Полотно, у которого нет ни одного треугольника лицом к игроку,
                // в игре просто не рисуется: URP Lit односторонний. Ошибка
                // молчаливая, и ловить её можно только здесь.
                Debug.LogError("HoleInWallCutoutProof: у полотна нет передних треугольников — " +
                               "обход граней развёрнут, стена будет невидимой");
            }
        }

        /// <summary>Что вышло у полотна на последней клетке. Идёт в таблицу: без этого поломку меша не отличить от поломки выреза.</summary>
        private static string surfaceFacts = string.Empty;

        /// <summary>Сколько треугольников полотна смотрят на игрока. Ноль — стена невидима.</summary>
        private static int frontTriangles;

        private static void FillTriangle(bool[] grid, int width, int height, Vector3 a, Vector3 b, Vector3 c,
            float halfWidth, float pixel)
        {
            float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
            float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

            int fromX = Mathf.Max(0, Mathf.FloorToInt((minX + halfWidth) / pixel));
            int toX = Mathf.Min(width - 1, Mathf.CeilToInt((maxX + halfWidth) / pixel));
            int fromY = Mathf.Max(0, Mathf.FloorToInt(minY / pixel));
            int toY = Mathf.Min(height - 1, Mathf.CeilToInt(maxY / pixel));

            for (int y = fromY; y <= toY; y++)
            {
                float pointY = (y + 0.5f) * pixel;
                for (int x = fromX; x <= toX; x++)
                {
                    float pointX = (x + 0.5f) * pixel - halfWidth;
                    if (Inside(a, b, c, pointX, pointY))
                    {
                        grid[y * width + x] = true;
                    }
                }
            }
        }

        private static bool Inside(Vector3 a, Vector3 b, Vector3 c, float x, float y)
        {
            float first = (b.x - a.x) * (y - a.y) - (b.y - a.y) * (x - a.x);
            float second = (c.x - b.x) * (y - b.y) - (c.y - b.y) * (x - b.x);
            float third = (a.x - c.x) * (y - c.y) - (a.y - c.y) * (x - c.x);
            return (first >= 0f && second >= 0f && third >= 0f) ||
                   (first <= 0f && second <= 0f && third <= 0f);
        }

        /// <summary>Обвести коробки столкновений: видно, где физика расходится с картинкой.</summary>
        private static void FillColliders(SweepingWall wall, bool[] grid, int width, int height) =>
            FillColliders(wall, grid, width, height, CellHalfWidth, PixelSize);

        private static void FillColliders(SweepingWall wall, bool[] grid, int width, int height,
            float halfWidth, float pixel)
        {
            var pool = (System.Collections.Generic.List<Transform>)GetField(wall, "panelPool");
            for (int i = 0; i < pool.Count; i++)
            {
                Transform panel = pool[i];
                if (panel == null || !panel.gameObject.activeSelf)
                {
                    continue;
                }

                Vector3 position = panel.localPosition;
                Vector3 scale = panel.localScale;
                Outline(grid, width, height, halfWidth, pixel,
                    position.x - scale.x * 0.5f, position.x + scale.x * 0.5f,
                    position.y - scale.y * 0.5f, position.y + scale.y * 0.5f);
            }
        }

        private static void Outline(bool[] grid, int width, int height, float halfWidth, float pixel,
            float fromX, float toX, float fromY, float toY)
        {
            int left = Mathf.FloorToInt((fromX + halfWidth) / pixel);
            int right = Mathf.FloorToInt((toX + halfWidth) / pixel);
            int bottom = Mathf.FloorToInt(fromY / pixel);
            int top = Mathf.FloorToInt(toY / pixel);

            for (int x = Mathf.Max(0, left); x <= Mathf.Min(width - 1, right); x++)
            {
                Mark(grid, width, height, x, bottom);
                Mark(grid, width, height, x, top);
            }

            for (int y = Mathf.Max(0, bottom); y <= Mathf.Min(height - 1, top); y++)
            {
                Mark(grid, width, height, left, y);
                Mark(grid, width, height, right, y);
            }
        }

        private static void Mark(bool[] grid, int width, int height, int x, int y)
        {
            if (x >= 0 && x < width && y >= 0 && y < height)
            {
                grid[y * width + x] = true;
            }
        }

        /// <summary>Положить клетку на лист.</summary>
        private static void Paint(Texture2D sheet, Cell cell, int originX, int originY, int width, int height)
        {
            Color border = cell.Overlap > 0 ? FailColor : PassColor;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    Color color;

                    if (x < BorderThickness || x >= width - BorderThickness ||
                        y < BorderThickness || y >= height - BorderThickness)
                    {
                        color = border;
                    }
                    else if (cell.Body[index])
                    {
                        color = cell.Wall[index] ? BodyInWallColor : BodyInHoleColor;
                    }
                    else if (cell.Collider[index])
                    {
                        color = ColliderColor;
                    }
                    else
                    {
                        color = cell.Wall[index] ? WallColor : HoleColor;
                    }

                    sheet.SetPixel(originX + x, originY + y, color);
                }
            }
        }

        // ========== РЕФЛЕКСИЯ ==========

        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, Private).SetValue(target, value);

        private static object GetField(object target, string name) =>
            target.GetType().GetField(name, Private).GetValue(target);

        private static void Invoke(object target, string name) =>
            target.GetType().GetMethod(name, Private).Invoke(target, null);

        private static void Invoke(object target, string name, object argument) =>
            target.GetType().GetMethod(name, Private).Invoke(target, new[] { argument });
    }
}
