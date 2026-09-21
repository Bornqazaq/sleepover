using System;
using TMPro;
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
    /// Собирает боулинг на существующей дорожке хаба: кегли, шар, борта,
    /// метку игрока, табличку и шкалу силы. Спека — `docs/hub-activities.md`.
    ///
    /// Живёт своим корнем `_HubBowling`, а не внутри `_HubOriginal`: тот
    /// корень сносит и пересобирает арт-проход хаба, а здесь физика и сетевые
    /// объекты, которым пересборка арта ни к чему.
    ///
    /// Проход идемпотентный: гонять можно сколько угодно раз.
    /// </summary>
    public static class HubBowlingBuilder
    {
        private const string RootName = "_HubBowling";
        private const string ScenePath = "Assets/_Project/Scenes/Hub.unity";

        // ===== геометрия дорожки, замерена по мебели хаба =====

        /// <summary>Верх настила дорожки: мебель кладёт его на 0.26 м.</summary>
        private const float LaneTop = 0.26f;

        /// <summary>Середина дорожки по Z.</summary>
        private const float LaneCenterZ = 8.2f;

        /// <summary>Ширина настила.</summary>
        private const float LaneWidth = 1.7f;

        /// <summary>Западный край настила — со стороны игрока.</summary>
        private const float LaneStartX = -8.55f;

        /// <summary>Передняя грань задника в восточном конце.</summary>
        private const float LaneEndX = -3.04f;

        // ===== расстановка =====

        private const float StandX = -8.2f;
        /// <summary>
        /// Шар стоит в 0.75 м перед меткой. Ближе нельзя: радиус капсулы
        /// персонажа 0.36, радиус шара 0.11, и на прежних 0.30 м шар оказывался
        /// внутри игрока — физика выталкивала его вбок ещё до броска, отчего
        /// «кинул прямо, а ушло в сторону».
        /// </summary>
        private const float BallHomeX = -7.45f;

        /// <summary>Вершина треугольника кеглей — ближняя к игроку.</summary>
        private const float PinsHeadX = -4.3f;

        /// <summary>Шаг между кеглями поперёк дорожки.</summary>
        private const float PinStepZ = 0.3f;

        /// <summary>Шаг между рядами. Даёт равносторонний треугольник: 0.30 × cos 30°.</summary>
        private const float PinStepX = 0.26f;

        private const int PinRows = 4;

        // ===== размеры снарядов =====

        private const float BallRadius = 0.11f;
        private const float BallMass = 6f;
        private const float PinHeight = 0.38f;
        private const float PinRadius = 0.06f;
        private const float PinMass = 1.5f;

        private const float RailHeight = 0.12f;
        private const float RailThickness = 0.08f;

        [MenuItem("Igruha/Хаб/Собрать боулинг")]
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

            Transform standPoint = BuildStandPoint(root);
            Transform ballHome = BuildBallHome(root);
            BuildRails(root);
            BowlingBall ball = BuildBall(root, ballHome.position);
            BowlingPin[] pins = BuildPins(root);
            HubActivityPowerGauge gauge = BuildGauge(root);

            BuildStation(root, standPoint, ballHome, ball, pins, gauge);
            HideDecorativeBall();

            Physics.SyncTransforms();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"Боулинг собран: {pins.Length} кеглей, шар радиусом {BallRadius} м, " +
                      $"метка X={StandX}, игровая длина {Mathf.Abs(LaneEndX - StandX):F2} м.");
        }

        /// <summary>
        /// Мебель дорожки несёт нарисованный шар — теперь их видно два, и
        /// игрок целится не тем. Гасим рендер: сама мебель строится арт-проходом
        /// хаба, трогать её файл незачем, а этот проход всё равно идёт следом.
        /// </summary>
        private static void HideDecorativeBall()
        {
            var decor = GameObject.Find("_HubOriginal/Games/BowlingLane/HO_Blue");
            if (decor == null)
            {
                return;
            }

            var view = decor.GetComponent<Renderer>();
            if (view != null)
            {
                view.enabled = false;
            }
        }

        // ================== части ==================

        private static Transform BuildStandPoint(Transform root)
        {
            var go = new GameObject("StandPoint");
            go.transform.SetParent(root, false);

            // Смотрит вдоль дорожки, на кегли: +X.
            go.transform.SetPositionAndRotation(
                new Vector3(StandX, LaneTop, LaneCenterZ),
                Quaternion.LookRotation(Vector3.right, Vector3.up));

            return go.transform;
        }

        private static Transform BuildBallHome(Transform root)
        {
            var go = new GameObject("BallHome");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(BallHomeX, LaneTop, LaneCenterZ);

            return go.transform;
        }

        /// <summary>
        /// Борта по краям настила: шар не улетает в комнату, а промах уходит
        /// вдоль борта и честно приносит ноль.
        /// </summary>
        private static void BuildRails(Transform root)
        {
            float length = LaneEndX - LaneStartX;
            float centerX = (LaneStartX + LaneEndX) * 0.5f;
            float edge = LaneWidth * 0.5f - RailThickness * 0.5f;

            foreach (float side in new[] { -1f, 1f })
            {
                var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.name = side < 0 ? "Rail_S" : "Rail_N";
                rail.transform.SetParent(root, false);
                rail.transform.position = new Vector3(centerX, LaneTop + RailHeight * 0.5f, LaneCenterZ + side * edge);
                rail.transform.localScale = new Vector3(length, RailHeight, RailThickness);
                rail.GetComponent<MeshRenderer>().sharedMaterial = HubOriginalAssets.Mat("Oak");
                GameObjectUtility.SetStaticEditorFlags(rail, StaticEditorFlags.BatchingStatic);
            }
        }

        private static BowlingBall BuildBall(Transform root, Vector3 home)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "BowlingBall";
            go.transform.SetParent(root, false);
            go.transform.position = home + Vector3.up * BallRadius;
            go.transform.localScale = Vector3.one * (BallRadius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = HubOriginalAssets.Mat("Blue");

            var shape = go.GetComponent<SphereCollider>();
            shape.material = PhysicsAssets.Ball();

            var body = go.AddComponent<Rigidbody>();
            body.mass = BallMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations = 16;
            body.solverVelocityIterations = 6;

            AddNetworking(go);

            return go.AddComponent<BowlingBall>();
        }

        /// <summary>
        /// Десять кеглей треугольником, вершиной к игроку. Ряды идут к заднику,
        /// поэтому последний остаётся в полуметре от него — иначе разлёт упирался
        /// бы в стенку сразу и выглядел вяло.
        /// </summary>
        private static BowlingPin[] BuildPins(Transform root)
        {
            var group = new GameObject("Pins").transform;
            group.SetParent(root, false);

            var pins = new System.Collections.Generic.List<BowlingPin>();

            for (int row = 0; row < PinRows; row++)
            {
                float x = PinsHeadX + row * PinStepX;

                for (int i = 0; i <= row; i++)
                {
                    float z = LaneCenterZ + (i - row * 0.5f) * PinStepZ;
                    pins.Add(BuildPin(group, new Vector3(x, LaneTop, z), pins.Count + 1));
                }
            }

            return pins.ToArray();
        }

        private static BowlingPin BuildPin(Transform parent, Vector3 footPosition, int number)
        {
            var go = new GameObject($"Pin_{number:00}");
            go.transform.SetParent(parent, false);
            go.transform.position = footPosition + Vector3.up * (PinHeight * 0.5f);

            var view = new GameObject("View");
            view.transform.SetParent(go.transform, false);
            // Меш строится от пятки, а корень стоит в середине высоты: так
            // капсула-коллайдер и центр масс совпадают с телом кегли.
            view.transform.localPosition = new Vector3(0f, -PinHeight * 0.5f, 0f);
            view.AddComponent<MeshFilter>().sharedMesh = PinMeshAsset();
            view.AddComponent<MeshRenderer>().sharedMaterial = HubOriginalAssets.Mat("Cream");

            BuildPinStripes(view.transform);

            // Коллайдер — капсула: валится естественнее составного меша и дешевле.
            var shape = go.AddComponent<CapsuleCollider>();
            shape.height = PinHeight;
            shape.radius = PinRadius;
            shape.direction = 1;
            shape.material = PhysicsAssets.Pin();

            var rigid = go.AddComponent<Rigidbody>();
            rigid.mass = PinMass;
            rigid.interpolation = RigidbodyInterpolation.Interpolate;
            rigid.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Тонкая капсула под ударом вчетверо более тяжёлого шара продавливается
            // сквозь настил на стандартных шести итерациях: кегля уходит в пол.
            rigid.solverIterations = 16;
            rigid.solverVelocityIterations = 6;
            rigid.maxDepenetrationVelocity = 3f;

            AddNetworking(go);

            return go.AddComponent<BowlingPin>();
        }

        /// <summary>Две красные полоски на шее — по ним кегля и читается кеглей.</summary>
        private static void BuildPinStripes(Transform view)
        {
            foreach (float height in new[] { 0.74f, 0.81f })
            {
                float radius = PinProfileRadius(height) * 1.04f;

                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stripe.name = "Stripe";
                stripe.transform.SetParent(view, false);
                stripe.transform.localPosition = new Vector3(0f, PinHeight * height, 0f);
                stripe.transform.localScale = new Vector3(radius * 2f, PinHeight * 0.018f, radius * 2f);
                stripe.GetComponent<MeshRenderer>().sharedMaterial = HubOriginalAssets.Mat("Red");
                Object.DestroyImmediate(stripe.GetComponent<Collider>());
            }
        }

        /// <summary>
        /// Силуэт кегли: узкая пятка, живот, шея, голова. Долями от полного
        /// радиуса — так профиль не зависит от того, какой размер выберут потом.
        /// </summary>
        private static readonly Vector2[] PinProfile =
        {
            new Vector2(0.000f, 0.42f),
            new Vector2(0.040f, 0.47f),
            new Vector2(0.100f, 0.64f),
            new Vector2(0.180f, 0.84f),
            new Vector2(0.260f, 0.96f),
            new Vector2(0.340f, 1.00f),
            new Vector2(0.430f, 0.94f),
            new Vector2(0.520f, 0.76f),
            new Vector2(0.610f, 0.55f),
            new Vector2(0.690f, 0.42f),
            new Vector2(0.760f, 0.41f),
            new Vector2(0.820f, 0.47f),
            new Vector2(0.880f, 0.56f),
            new Vector2(0.930f, 0.57f),
            new Vector2(0.965f, 0.48f),
            new Vector2(0.990f, 0.28f),
            new Vector2(1.000f, 0.00f),
        };

        private static float PinProfileRadius(float height)
        {
            for (int i = 1; i < PinProfile.Length; i++)
            {
                if (height > PinProfile[i].x)
                {
                    continue;
                }

                Vector2 a = PinProfile[i - 1];
                Vector2 b = PinProfile[i];
                float t = Mathf.InverseLerp(a.x, b.x, height);

                return Mathf.Lerp(a.y, b.y, t) * PinRadius;
            }

            return 0f;
        }

        /// <summary>
        /// Меш кегли — один на все десять: тело вращения по профилю. Хранится
        /// ассетом, иначе сцена потеряет его при следующем открытии.
        /// </summary>
        private static Mesh PinMeshAsset()
        {
            const string folder = "Assets/_Project/Art/Hub/Original/Meshes";
            const string path = folder + "/BowlingPin.asset";

            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Art/Hub/Original", "Meshes");
            }

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            Mesh mesh = existing != null ? existing : new Mesh { name = "BowlingPin" };
            mesh.Clear();

            const int segments = 20;
            int rings = PinProfile.Length;

            var vertices = new Vector3[rings * (segments + 1) + 1];
            var uv = new Vector2[vertices.Length];
            int v = 0;

            for (int r = 0; r < rings; r++)
            {
                float y = PinProfile[r].x * PinHeight;
                float radius = PinProfile[r].y * PinRadius;

                for (int s = 0; s <= segments; s++)
                {
                    float angle = s / (float)segments * Mathf.PI * 2f;
                    vertices[v] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                    uv[v] = new Vector2(s / (float)segments, PinProfile[r].x);
                    v++;
                }
            }

            // Донышко одной точкой в центре пятки — кегля не должна просвечивать снизу.
            int bottomCenter = v;
            vertices[v] = Vector3.zero;
            uv[v] = new Vector2(0.5f, 0f);

            var triangles = new System.Collections.Generic.List<int>((rings - 1) * segments * 6 + segments * 3);

            for (int r = 0; r < rings - 1; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * (segments + 1) + s;
                    int b = a + 1;
                    int c = a + segments + 1;
                    int d = c + 1;

                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            for (int s = 0; s < segments; s++)
            {
                triangles.Add(bottomCenter);
                triangles.Add(s);
                triangles.Add(s + 1);
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            return mesh;
        }

        private static HubActivityPowerGauge BuildGauge(Transform root)
        {
            var go = new GameObject("PowerGauge");
            go.transform.SetParent(root, false);

            return go.AddComponent<HubActivityPowerGauge>();
        }

        /// <summary>
        /// Сама станция: коллайдер-триггер, которым её находит
        /// <c>PlayerInteractor</c>, плюс сетевой объект — без него намерение
        /// взаимодействия некуда адресовать.
        /// </summary>
        private static void BuildStation(Transform root, Transform standPoint, Transform ballHome,
            BowlingBall ball, BowlingPin[] pins, HubActivityPowerGauge gauge)
        {
            var go = new GameObject("BowlingStation");
            go.transform.SetParent(root, false);
            go.transform.position = standPoint.position;

            var zone = go.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.size = new Vector3(1.6f, 2f, 1.6f);
            zone.center = new Vector3(0f, 0.8f, 0f);

            go.AddComponent<NetworkObject>();
            var station = go.AddComponent<BowlingStation>();

            var serialized = new SerializedObject(station);
            serialized.FindProperty("standPoint").objectReferenceValue = standPoint;
            serialized.FindProperty("activityName").stringValue = "боулинг";
            serialized.FindProperty("gauge").objectReferenceValue = gauge;
            serialized.FindProperty("ball").objectReferenceValue = ball;
            serialized.FindProperty("ballHome").objectReferenceValue = ballHome;
            serialized.FindProperty("releaseHeight").floatValue = BallRadius + 0.01f;

            SerializedProperty list = serialized.FindProperty("pins");
            list.arraySize = pins.Length;
            for (int i = 0; i < pins.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = pins[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Сетевая обвязка снаряда: объект сцены плюс транспорт положения.
        /// Префабов не заводим — им пришлось бы жить в списке сетевых префабов,
        /// а его расхождение уже стоило проекту часов.
        /// </summary>
        private static void AddNetworking(GameObject go)
        {
            go.AddComponent<NetworkObject>();

            var transport = go.AddComponent<NetworkTransform>();
            transport.SyncScaleX = transport.SyncScaleY = transport.SyncScaleZ = false;
            transport.Interpolate = true;
        }
    }

    /// <summary>
    /// Физические материалы забав. Отдельный класс, потому что их переиспользуют
    /// бильярд и дартс, когда до них дойдёт очередь.
    /// </summary>
    internal static class PhysicsAssets
    {
        private const string Folder = "Assets/_Project/Settings/Physics";

        /// <summary>Шар боулинга: катится далеко и не скачет.</summary>
        internal static PhysicsMaterial Ball() => Load("BowlingBall", 0.06f, 0.05f, 0.05f);

        /// <summary>Кегля: цепляется за настил, чтобы не разъезжаться от сквозняка.</summary>
        internal static PhysicsMaterial Pin() => Load("BowlingPin", 0.45f, 0.4f, 0.1f);

        private static PhysicsMaterial Load(string name, float staticFriction, float dynamicFriction, float bounciness)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Settings", "Physics");
            }

            string path = $"{Folder}/{name}.asset";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);

            if (material == null)
            {
                material = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(material, path);
            }

            material.staticFriction = staticFriction;
            material.dynamicFriction = dynamicFriction;
            material.bounciness = bounciness;
            material.frictionCombine = PhysicsMaterialCombine.Average;
            material.bounceCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(material);

            return material;
        }
    }
}
