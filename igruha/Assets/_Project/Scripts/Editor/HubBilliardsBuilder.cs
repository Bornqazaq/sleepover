using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Core.Hub.Activities;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Собирает бильярд в хабе. Визуал стола — модель из Blender
    /// (<c>Art/Hub/Billiards</c>): ножки, царга, сукно, лузы. Родная мебель
    /// <c>_HubOriginal/Games/Billiards</c> гасится целиком (нарисованные шары
    /// и прежний «ящик»). Физика, игровые шары и станция живут в
    /// <c>_HubBilliards</c>.
    /// </summary>
    public static class HubBilliardsBuilder
    {
        private const string RootName = "_HubBilliards";
        private const string ScenePath = "Assets/_Project/Scenes/Hub.unity";
        private const string TableFbx = "Assets/_Project/Art/Hub/Billiards/Models/HubBilliardsTable.fbx";

        private static readonly Vector3 TableCenter = new Vector3(6.80f, 0f, 1.70f);

        /// <summary>Сукно по замеру мебели HO_Sage, не внешний габарит.</summary>
        private const float PlayWidth = 1.23f;
        private const float PlayLength = 2.51f;
        private const float ClothY = 0.95f;
        private const float RailHeight = 0.08f;
        private const float RailThickness = 0.11f;

        private const float StandOffset = 0.85f;
        private const float BallRadius = 0.042f;
        private const float BallMass = 0.55f;
        private const float CueMass = 0.6f;
        private const float CueInset = 0.48f;
        private const float RackInset = 0.58f;

        /// <summary>
        /// Радиус триггера лузы. Раньше 0.09 — шар задевал краем и пропадал.
        /// Теперь чуть больше радиуса шара: надо реально провалиться в отверстие.
        /// </summary>
        private const float PocketRadius = 0.052f;

        [MenuItem("Igruha/Хаб/Собрать бильярд")]
        public static void Apply()
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != ScenePath)
            {
                throw new InvalidOperationException("Открой Hub.unity вне Play Mode.");
            }

            HubOriginalAssets.Import();
            AssetDatabase.ImportAsset(TableFbx, ImportAssetOptions.ForceUpdate);

            GameObject existing = GameObject.Find(RootName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            Transform root = new GameObject(RootName).transform;

            HideOriginalFurniture();
            PlaceBlenderTable(root);
            BuildPhysics(root);
            BilliardsPocket[] pockets = BuildPockets(root);
            Transform standPoint = BuildStandPoint(root);
            BilliardsBall cueBall = BuildBall(root, CueHome(), 0, true);
            BilliardsBall[] objectBalls = BuildRack(root);
            Transform aimLine = BuildAimLine(root);
            HubActivityPowerGauge gauge = BuildGauge(root);

            BuildStation(root, standPoint, cueBall, objectBalls, pockets, aimLine, gauge);

            Physics.SyncTransforms();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"Billiards rebuilt with Blender table: {objectBalls.Length + 1} balls, " +
                      $"pocketR={PocketRadius}, play {PlayWidth:F2}x{PlayLength:F2}");
        }

        /// <summary>
        /// Старая мебель — комбинированный «ящик» с нарисованными шарами.
        /// Гасим рендер, коллайдеры комнаты оставляем.
        /// </summary>
        private static void HideOriginalFurniture()
        {
            var table = GameObject.Find("_HubOriginal/Games/Billiards");
            if (table == null)
            {
                return;
            }

            foreach (var view in table.GetComponentsInChildren<Renderer>(true))
            {
                view.enabled = false;
            }
        }

        private static void PlaceBlenderTable(Transform root)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(TableFbx);
            if (source == null)
            {
                throw new InvalidOperationException($"Нет модели стола: {TableFbx}. Сначала tools/blender/hub_billiards_table.py");
            }

            var table = (GameObject)PrefabUtility.InstantiatePrefab(source, root);
            table.name = "TableVisual";
            // Blender Z-up → Unity Y-up: пустой корень FBX не конвертит детей,
            // без -90° по X стол встаёт стеной.
            table.transform.SetPositionAndRotation(TableCenter, Quaternion.Euler(-90f, 0f, 0f));

            foreach (var renderer in table.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = MaterialForPart(renderer.gameObject.name);
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic);
            }

            // Коллайдеры из FBX не нужны — физику строим сами под игровой размер.
            foreach (var col in table.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(col);
            }
        }

        private static Material MaterialForPart(string name)
        {
            if (name.StartsWith("Cloth", StringComparison.Ordinal))
            {
                return HubOriginalAssets.Mat("Sage");
            }

            if (name.StartsWith("Cushion", StringComparison.Ordinal) || name.StartsWith("Bed", StringComparison.Ordinal))
            {
                return HubOriginalAssets.Mat("Oak");
            }

            if (name.StartsWith("Pocket", StringComparison.Ordinal))
            {
                return HubOriginalAssets.Mat("Ink");
            }

            if (name.StartsWith("Sight", StringComparison.Ordinal))
            {
                return HubOriginalAssets.Mat("Cream");
            }

            return HubOriginalAssets.Mat("Walnut");
        }

        /// <summary>
        /// Только невидимые коллайдеры. Рисует стол из Blender.
        /// </summary>
        private static void BuildPhysics(Transform root)
        {
            var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cloth.name = "ClothCollider";
            cloth.transform.SetParent(root, false);
            cloth.transform.position = new Vector3(TableCenter.x, ClothY - 0.025f, TableCenter.z);
            cloth.transform.localScale = new Vector3(PlayWidth, 0.05f, PlayLength);
            Object.DestroyImmediate(cloth.GetComponent<MeshRenderer>());
            cloth.GetComponent<BoxCollider>().material = PhysicsAssets.Cloth();

            float halfW = PlayWidth * 0.5f;
            float halfL = PlayLength * 0.5f;
            float railY = ClothY + RailHeight * 0.5f;
            float shortLen = PlayWidth - PocketRadius * 2.6f;
            float longSeg = (PlayLength - PocketRadius * 2.8f) * 0.5f;
            float longCenter = halfL * 0.5f + PocketRadius * 0.15f;
            var cushion = PhysicsAssets.Cushion();

            void Rail(string name, Vector3 pos, Vector3 size)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(root, false);
                go.transform.position = pos;
                go.transform.localScale = size;
                Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
                go.GetComponent<BoxCollider>().material = cushion;
            }

            float wx = TableCenter.x - halfW - RailThickness * 0.5f;
            float ex = TableCenter.x + halfW + RailThickness * 0.5f;
            Rail("Rail_W_S", new Vector3(wx, railY, TableCenter.z - longCenter),
                new Vector3(RailThickness, RailHeight, longSeg));
            Rail("Rail_W_N", new Vector3(wx, railY, TableCenter.z + longCenter),
                new Vector3(RailThickness, RailHeight, longSeg));
            Rail("Rail_E_S", new Vector3(ex, railY, TableCenter.z - longCenter),
                new Vector3(RailThickness, RailHeight, longSeg));
            Rail("Rail_E_N", new Vector3(ex, railY, TableCenter.z + longCenter),
                new Vector3(RailThickness, RailHeight, longSeg));
            Rail("Rail_S", new Vector3(TableCenter.x, railY, TableCenter.z - halfL - RailThickness * 0.5f),
                new Vector3(shortLen, RailHeight, RailThickness));
            Rail("Rail_N", new Vector3(TableCenter.x, railY, TableCenter.z + halfL + RailThickness * 0.5f),
                new Vector3(shortLen, RailHeight, RailThickness));
        }

        private static BilliardsPocket[] BuildPockets(Transform root)
        {
            float halfW = PlayWidth * 0.5f;
            float halfL = PlayLength * 0.5f;
            // Триггер чуть ниже сукна: шар должен провалиться, а не задеть краешек.
            float y = ClothY - 0.01f;

            var spots = new[]
            {
                new Vector3(TableCenter.x - halfW, y, TableCenter.z - halfL),
                new Vector3(TableCenter.x + halfW, y, TableCenter.z - halfL),
                new Vector3(TableCenter.x - halfW, y, TableCenter.z + halfL),
                new Vector3(TableCenter.x + halfW, y, TableCenter.z + halfL),
                new Vector3(TableCenter.x - halfW, y, TableCenter.z),
                new Vector3(TableCenter.x + halfW, y, TableCenter.z),
            };

            var pockets = new BilliardsPocket[spots.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                var go = new GameObject($"Pocket_{i}");
                go.transform.SetParent(root, false);
                go.transform.position = spots[i];

                var trigger = go.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = PocketRadius;

                pockets[i] = go.AddComponent<BilliardsPocket>();
            }

            return pockets;
        }

        private static Transform BuildStandPoint(Transform root)
        {
            float south = TableCenter.z - PlayLength * 0.5f;
            var go = new GameObject("StandPoint");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(
                new Vector3(TableCenter.x, 0f, south - StandOffset),
                Quaternion.LookRotation(Vector3.forward, Vector3.up));
            return go.transform;
        }

        private static Vector3 CueHome()
        {
            float south = TableCenter.z - PlayLength * 0.5f;
            return new Vector3(TableCenter.x, ClothY + BallRadius, south + CueInset);
        }

        private static BilliardsBall[] BuildRack(Transform root)
        {
            float north = TableCenter.z + PlayLength * 0.5f;
            Vector3 head = new Vector3(TableCenter.x, ClothY + BallRadius, north - RackInset);
            float step = BallRadius * 2.08f;
            var balls = new System.Collections.Generic.List<BilliardsBall>();
            int number = 1;

            for (int row = 0; row < 5; row++)
            {
                for (int i = 0; i <= row; i++)
                {
                    float x = head.x + (i - row * 0.5f) * step;
                    float z = head.z - row * step * 0.866f;
                    balls.Add(BuildBall(root, new Vector3(x, head.y, z), number, false));
                    number++;
                }
            }

            return balls.ToArray();
        }

        /// <summary>
        /// Один шар — одна сфера с текстурой: полоса и номер запечены в PNG.
        /// Вложенная «полоса-сфера» давала шов покебола.
        /// </summary>
        private static BilliardsBall BuildBall(Transform root, Vector3 position, int number, bool cue)
        {
            string name = cue ? "CueBall" : $"Ball_{number:00}";
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * (BallRadius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = BallSkin(number, cue);

            var shape = go.GetComponent<SphereCollider>();
            shape.material = PhysicsAssets.PoolBall();

            var body = go.AddComponent<Rigidbody>();
            body.mass = cue ? CueMass : BallMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations = 16;
            body.solverVelocityIterations = 6;
            body.maxDepenetrationVelocity = 2f;

            AddNetworking(go);

            var ball = go.AddComponent<BilliardsBall>();
            var serialized = new SerializedObject(ball);
            serialized.FindProperty("isCueBall").boolValue = cue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ball.CaptureHome();

            return ball;
        }

        private static readonly Color[] BallColors =
        {
            new Color(0.92f, 0.92f, 0.88f),
            new Color(0.92f, 0.78f, 0.12f),
            new Color(0.12f, 0.28f, 0.72f),
            new Color(0.78f, 0.12f, 0.12f),
            new Color(0.42f, 0.14f, 0.55f),
            new Color(0.92f, 0.48f, 0.08f),
            new Color(0.10f, 0.48f, 0.22f),
            new Color(0.48f, 0.10f, 0.12f),
            new Color(0.06f, 0.06f, 0.07f),
        };

        private static Color ColorOf(int number)
        {
            int solid = number <= 8 ? number : number - 8;
            return solid is >= 1 and <= 8 ? BallColors[solid] : BallColors[0];
        }

        private static Material BallSkin(int number, bool cue)
        {
            string key = cue ? "Cue" : $"Ball_{number:00}";
            string matPath = $"Assets/_Project/Art/Hub/Billiards/Materials/Pool{key}.mat";
            string texPath = $"Assets/_Project/Art/Hub/Billiards/Textures/Pool{key}.png";

            EnsureFolder("Assets/_Project/Art/Hub/Billiards/Materials");
            EnsureFolder("Assets/_Project/Art/Hub/Billiards/Textures");

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (texture == null)
            {
                texture = BakeBallTexture(number, cue);
                System.IO.File.WriteAllBytes(
                    System.IO.Path.Combine(Application.dataPath, "_Project/Art/Hub/Billiards/Textures",
                        $"Pool{key}.png"),
                    texture.EncodeToPNG());
                AssetDatabase.ImportAsset(texPath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
                if (importer != null)
                {
                    importer.sRGBTexture = true;
                    importer.mipmapEnabled = true;
                    importer.SaveAndReimport();
                }

                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, matPath);
            }

            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", 0.9f);
            material.SetFloat("_Metallic", 0.06f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>
        /// UV сферы Unity: U — долгота, V — от низа к верху. Полоса — пояс
        /// по экватору, номер — белый кружок у полюса.
        /// </summary>
        private static Texture2D BakeBallTexture(int number, bool cue)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            Color cream = BallColors[0];
            Color paint = cue ? cream : ColorOf(number);
            bool striped = !cue && number >= 9;

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)(size - 1);
                for (int x = 0; x < size; x++)
                {
                    Color c;
                    if (cue)
                    {
                        c = cream;
                    }
                    else if (striped)
                    {
                        c = v > 0.34f && v < 0.66f ? paint : cream;
                    }
                    else
                    {
                        c = paint;
                    }

                    pixels[y * size + x] = c;
                }
            }

            if (!cue)
            {
                // Белый кружок с цифрой у «макушки» (V ≈ 0.82).
                int cx = size / 2;
                int cy = (int)(size * 0.82f);
                int radius = size / 11;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (dx * dx + dy * dy > radius * radius)
                        {
                            continue;
                        }

                        int px = cx + dx;
                        int py = cy + dy;
                        if (px >= 0 && px < size && py >= 0 && py < size)
                        {
                            pixels[py * size + px] = Color.white;
                        }
                    }
                }

                StampDigit(pixels, size, number, Color.black, cx, cy, radius);
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static void StampDigit(Color[] pixels, int size, int number, Color ink, int cx, int cy, int badgeR)
        {
            string[][] glyphs =
            {
                new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
                new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
                new[] { "01110", "10001", "00001", "00110", "01000", "10000", "11111" },
                new[] { "01110", "10001", "00001", "00110", "00001", "10001", "01110" },
                new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
                new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
                new[] { "01110", "10000", "11110", "10001", "10001", "10001", "01110" },
                new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
                new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
                new[] { "01110", "10001", "10001", "01111", "00001", "00001", "01110" },
            };

            void DrawOne(int digit, int originX, int originY, int cell)
            {
                string[] g = glyphs[Mathf.Clamp(digit, 0, 9)];
                for (int row = 0; row < 7; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        if (g[row][col] != '1')
                        {
                            continue;
                        }

                        for (int py = 0; py < cell; py++)
                        {
                            for (int px = 0; px < cell; px++)
                            {
                                int x = originX + col * cell + px;
                                int y = originY + row * cell + py;
                                if (x >= 0 && x < size && y >= 0 && y < size)
                                {
                                    pixels[y * size + x] = ink;
                                }
                            }
                        }
                    }
                }
            }

            int cell = Mathf.Max(1, badgeR / 6);
            int glyphW = 5 * cell;
            int glyphH = 7 * cell;
            int oy = cy - glyphH / 2;

            if (number >= 10)
            {
                DrawOne(number / 10, cx - glyphW - 1, oy, cell);
                DrawOne(number % 10, cx + 1, oy, cell);
            }
            else
            {
                DrawOne(number, cx - glyphW / 2, oy, cell);
            }
        }

        // Старые хелперы полосы/бейджа сняты — всё в BakeBallTexture.

        private static Transform BuildAimLine(Transform root)
        {
            // Тонкий «кий»: тёмный цилиндр. Раньше толстый кремовый куб читался
            // палкой, брошенной поперёк стола.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "AimLine";
            go.transform.SetParent(root, false);
            go.transform.localScale = new Vector3(0.018f, 0.7f, 0.018f);
            go.GetComponent<MeshRenderer>().sharedMaterial = HubOriginalAssets.Mat("Walnut");
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.SetActive(false);
            return go.transform;
        }

        private static HubActivityPowerGauge BuildGauge(Transform root)
        {
            var go = new GameObject("PowerGauge");
            go.transform.SetParent(root, false);
            return go.AddComponent<HubActivityPowerGauge>();
        }

        private static void BuildStation(Transform root, Transform standPoint, BilliardsBall cueBall,
            BilliardsBall[] objectBalls, BilliardsPocket[] pockets, Transform aimLine,
            HubActivityPowerGauge gauge)
        {
            var go = new GameObject("BilliardsStation");
            go.transform.SetParent(root, false);
            go.transform.position = standPoint.position;

            var zone = go.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.size = new Vector3(1.8f, 2f, 1.6f);
            zone.center = new Vector3(0f, 0.8f, 0.4f);

            go.AddComponent<NetworkObject>();
            var station = go.AddComponent<BilliardsStation>();

            var serialized = new SerializedObject(station);
            serialized.FindProperty("standPoint").objectReferenceValue = standPoint;
            serialized.FindProperty("activityName").stringValue = "бильярд";
            serialized.FindProperty("gauge").objectReferenceValue = gauge;
            serialized.FindProperty("cueBall").objectReferenceValue = cueBall;
            serialized.FindProperty("aimLine").objectReferenceValue = aimLine;
            serialized.FindProperty("aimLineLength").floatValue = 1.35f;

            SerializedProperty objects = serialized.FindProperty("objectBalls");
            objects.arraySize = objectBalls.Length;
            for (int i = 0; i < objectBalls.Length; i++)
            {
                objects.GetArrayElementAtIndex(i).objectReferenceValue = objectBalls[i];
            }

            SerializedProperty pocketList = serialized.FindProperty("pockets");
            pocketList.arraySize = pockets.Length;
            for (int i = 0; i < pockets.Length; i++)
            {
                pocketList.GetArrayElementAtIndex(i).objectReferenceValue = pockets[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddNetworking(GameObject go)
        {
            go.AddComponent<NetworkObject>();
            var transport = go.AddComponent<NetworkTransform>();
            transport.SyncScaleX = transport.SyncScaleY = transport.SyncScaleZ = false;
            transport.Interpolate = true;
        }
    }
}
