using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Core.Hub.Activities;
using static Igruha.EditorTools.HubCompactPass;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Собирает бильярд на существующем столе хаба: сукно, борта, лузы,
    /// 16 шаров, метку игрока, прицел и шкалу силы. Спека —
    /// <c>docs/hub-activities.md</c> §9.2; счёта нет — как у боулинга.
    ///
    /// Живёт своим корнем <c>_HubBilliards</c>, а не внутри <c>_HubOriginal</c>:
    /// тот корень сносит арт-проход хаба, а здесь физика и сетевые объекты.
    /// Проход идемпотентный.
    /// </summary>
    public static class HubBilliardsBuilder
    {
        private const string RootName = "_HubBilliards";
        private const string ScenePath = "Assets/_Project/Scenes/Hub.unity";

        // ===== геометрия стола, замерена по мебели хаба =====

        private static readonly Vector3 TableCenter = new Vector3(6.80f, 0f, 1.70f);

        /// <summary>Внешний габарит мебели по X (короткая сторона).</summary>
        private const float OuterWidth = 1.62f;

        /// <summary>Внешний габарит мебели по Z (длинная сторона).</summary>
        private const float OuterLength = 2.92f;

        /// <summary>Высота сукна над полом.</summary>
        private const float ClothY = 0.95f;

        /// <summary>Толщина борта сверху сукна.</summary>
        private const float RailHeight = 0.07f;

        /// <summary>Ширина борта: сукно уже внешнего габарита.</summary>
        private const float RailWidth = 0.14f;

        private const float PlayWidth = OuterWidth - RailWidth * 2f;
        private const float PlayLength = OuterLength - RailWidth * 2f;

        // ===== расстановка =====

        /// <summary>
        /// Метка у южного короткого края. Смотрит вдоль стола (+Z).
        /// Отступ от сукна, чтобы капсула 0.36 не пересеклась с битком.
        /// </summary>
        private const float StandOffset = 0.85f;

        private const float BallRadius = 0.045f;
        private const float BallMass = 0.55f;
        private const float CueMass = 0.6f;

        /// <summary>Биток — ближе к игроку, с зазором от капсулы и от борта.</summary>
        private const float CueInset = 0.42f;

        /// <summary>Вершина пирамиды — ближе к дальнему борту.</summary>
        private const float RackInset = 0.55f;

        private const float PocketRadius = 0.09f;

        [MenuItem("Igruha/Хаб/Собрать бильярд")]
        public static void Apply()
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != ScenePath)
            {
                throw new InvalidOperationException("Открой Hub.unity вне Play Mode.");
            }

            HubOriginalAssets.Import();

            GameObject existing = GameObject.Find(RootName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            Transform root = new GameObject(RootName).transform;

            HideDecorativeTable();
            BuildClothAndRails(root);
            BilliardsPocket[] pockets = BuildPockets(root);
            Transform standPoint = BuildStandPoint(root);
            BilliardsBall cueBall = BuildBall(root, CueHome(), true, "CueBall", CueMaterial());
            BilliardsBall[] objectBalls = BuildRack(root);
            Transform aimLine = BuildAimLine(root);
            HubActivityPowerGauge gauge = BuildGauge(root);

            BuildStation(root, standPoint, cueBall, objectBalls, pockets, aimLine, gauge);

            Physics.SyncTransforms();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"Billiards built: {objectBalls.Length + 1} balls, pockets={pockets.Length}, " +
                      $"play {PlayWidth:F2}x{PlayLength:F2}, clothY={ClothY}");
        }

        /// <summary>
        /// Мебель несёт нарисованные шары на сукне. Выборочно их не погасить —
        /// куски сидят в комбинированном меше. Гасим рендер целиком, коллайдеры
        /// мебели остаются: они не рисуются и держат стол в комнате.
        /// </summary>
        private static void HideDecorativeTable()
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

        private static void BuildClothAndRails(Transform root)
        {
            var g = new HubOriginalGeometry();

            // Сукно — тонкая плита на высоте мебели.
            g.Box(new Vector3(TableCenter.x, ClothY - 0.02f, TableCenter.z),
                new Vector3(PlayWidth, 0.04f, PlayLength), M("Sage"));

            // Ноги/цоколь — чтобы стол не висел после гашения мебели.
            g.Box(new Vector3(TableCenter.x, ClothY * 0.5f, TableCenter.z),
                new Vector3(OuterWidth - 0.08f, ClothY - 0.04f, OuterLength - 0.08f), M("Walnut"));

            float halfW = PlayWidth * 0.5f;
            float halfL = PlayLength * 0.5f;
            float railY = ClothY + RailHeight * 0.5f;

            // Длинные борта (вдоль Z).
            g.Box(new Vector3(TableCenter.x - halfW - RailWidth * 0.5f, railY, TableCenter.z),
                new Vector3(RailWidth, RailHeight, PlayLength), M("Walnut"));
            g.Box(new Vector3(TableCenter.x + halfW + RailWidth * 0.5f, railY, TableCenter.z),
                new Vector3(RailWidth, RailHeight, PlayLength), M("Walnut"));

            // Короткие борта (вдоль X), с вырезом под угловые лузы — короче сукна.
            float shortLen = PlayWidth - PocketRadius * 2.2f;
            g.Box(new Vector3(TableCenter.x, railY, TableCenter.z - halfL - RailWidth * 0.5f),
                new Vector3(shortLen, RailHeight, RailWidth), M("Walnut"));
            g.Box(new Vector3(TableCenter.x, railY, TableCenter.z + halfL + RailWidth * 0.5f),
                new Vector3(shortLen, RailHeight, RailWidth), M("Walnut"));

            g.Build(root, "Table");

            // Физика бортов и сукна — отдельные коллайдеры: batched-меш без них.
            BuildPlaySurfaceCollider(root);
            BuildRailColliders(root);
        }

        private static void BuildPlaySurfaceCollider(Transform root)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ClothCollider";
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(TableCenter.x, ClothY - 0.025f, TableCenter.z);
            go.transform.localScale = new Vector3(PlayWidth, 0.05f, PlayLength);
            Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
            go.GetComponent<BoxCollider>().material = PhysicsAssets.Cloth();
        }

        private static void BuildRailColliders(Transform root)
        {
            float halfW = PlayWidth * 0.5f;
            float halfL = PlayLength * 0.5f;
            float railY = ClothY + RailHeight * 0.5f;
            float shortLen = PlayWidth - PocketRadius * 2.2f;
            // Длинный борт рвём на две половины с зазором под боковую лузу.
            float longSeg = (PlayLength - PocketRadius * 2.4f) * 0.5f;
            float longCenter = halfL * 0.5f + PocketRadius * 0.2f;
            var mat = PhysicsAssets.Cushion();

            void Rail(string name, Vector3 pos, Vector3 size)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(root, false);
                go.transform.position = pos;
                go.transform.localScale = size;
                Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
                go.GetComponent<BoxCollider>().material = mat;
            }

            float wx = TableCenter.x - halfW - RailWidth * 0.5f;
            float ex = TableCenter.x + halfW + RailWidth * 0.5f;
            Rail("Rail_W_S", new Vector3(wx, railY, TableCenter.z - longCenter),
                new Vector3(RailWidth, RailHeight, longSeg));
            Rail("Rail_W_N", new Vector3(wx, railY, TableCenter.z + longCenter),
                new Vector3(RailWidth, RailHeight, longSeg));
            Rail("Rail_E_S", new Vector3(ex, railY, TableCenter.z - longCenter),
                new Vector3(RailWidth, RailHeight, longSeg));
            Rail("Rail_E_N", new Vector3(ex, railY, TableCenter.z + longCenter),
                new Vector3(RailWidth, RailHeight, longSeg));
            Rail("Rail_S", new Vector3(TableCenter.x, railY, TableCenter.z - halfL - RailWidth * 0.5f),
                new Vector3(shortLen, RailHeight, RailWidth));
            Rail("Rail_N", new Vector3(TableCenter.x, railY, TableCenter.z + halfL + RailWidth * 0.5f),
                new Vector3(shortLen, RailHeight, RailWidth));
        }

        private static BilliardsPocket[] BuildPockets(Transform root)
        {
            float halfW = PlayWidth * 0.5f;
            float halfL = PlayLength * 0.5f;
            float y = ClothY + 0.02f;

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
            var g = new HubOriginalGeometry();

            for (int i = 0; i < spots.Length; i++)
            {
                g.Sphere(spots[i] + Vector3.down * 0.02f, PocketRadius * 0.85f, M("Ink"));

                var go = new GameObject($"Pocket_{i}");
                go.transform.SetParent(root, false);
                go.transform.position = spots[i];

                var trigger = go.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = PocketRadius;

                pockets[i] = go.AddComponent<BilliardsPocket>();
            }

            g.Build(root, "Pockets", castShadows: false);
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

            // Шаг пирамиды чуть больше диаметра, чтобы шары не клинили на старте.
            float step = BallRadius * 2.05f;
            var balls = new System.Collections.Generic.List<BilliardsBall>();
            int number = 1;

            string[] palette =
            {
                "Cream", "Red", "Blue", "Cream", "Red",
                "Blue", "Cream", "Ink", "Red", "Blue",
                "Cream", "Red", "Blue", "Cream", "Red",
            };

            for (int row = 0; row < 5; row++)
            {
                for (int i = 0; i <= row; i++)
                {
                    float x = head.x + (i - row * 0.5f) * step;
                    float z = head.z - row * step * 0.866f;
                    string mat = palette[(number - 1) % palette.Length];
                    balls.Add(BuildBall(root, new Vector3(x, head.y, z), false,
                        $"Ball_{number:00}", HubOriginalAssets.Mat(mat)));
                    number++;
                }
            }

            return balls.ToArray();
        }

        private static BilliardsBall BuildBall(Transform root, Vector3 position, bool cue, string name, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * (BallRadius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;

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

        private static Material CueMaterial()
        {
            const string path = "Assets/_Project/Art/Hub/Original/Materials/HO_CueBall.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.88f));
            material.SetFloat("_Smoothness", 0.9f);
            material.SetFloat("_Metallic", 0.05f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Transform BuildAimLine(Transform root)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "AimLine";
            go.transform.SetParent(root, false);
            go.transform.localScale = new Vector3(0.03f, 0.03f, 1.4f);
            go.GetComponent<MeshRenderer>().sharedMaterial = HubOriginalAssets.Mat("Cream");
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
