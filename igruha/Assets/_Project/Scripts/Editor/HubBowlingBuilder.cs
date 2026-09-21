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
        private const float BallHomeX = -7.9f;

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
            HubActivityBoard board = BuildBoard(root);
            HubActivityPowerGauge gauge = BuildGauge(root);

            BuildStation(root, standPoint, ballHome, ball, pins, board, gauge);

            Physics.SyncTransforms();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"Боулинг собран: {pins.Length} кеглей, шар радиусом {BallRadius} м, " +
                      $"метка X={StandX}, игровая длина {Mathf.Abs(LaneEndX - StandX):F2} м.");
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

            // Тело кегли: цилиндр с шариком-головой. Коллайдер — капсула:
            // она валится естественнее составного меша и дешевле.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(PinRadius * 2f, PinHeight * 0.5f, PinRadius * 2f);
            body.GetComponent<MeshRenderer>().sharedMaterial = HubOriginalAssets.Mat("Cream");
            Object.DestroyImmediate(body.GetComponent<Collider>());

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(go.transform, false);
            head.transform.localPosition = new Vector3(0f, PinHeight * 0.5f, 0f);
            head.transform.localScale = Vector3.one * (PinRadius * 1.5f);
            head.GetComponent<MeshRenderer>().sharedMaterial = HubOriginalAssets.Mat("Red");
            Object.DestroyImmediate(head.GetComponent<Collider>());

            var shape = go.AddComponent<CapsuleCollider>();
            shape.height = PinHeight;
            shape.radius = PinRadius;
            shape.direction = 1;
            shape.material = PhysicsAssets.Pin();

            var rigid = go.AddComponent<Rigidbody>();
            rigid.mass = PinMass;
            rigid.interpolation = RigidbodyInterpolation.Interpolate;
            rigid.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            AddNetworking(go);

            return go.AddComponent<BowlingPin>();
        }

        private static HubActivityBoard BuildBoard(Transform root)
        {
            var go = new GameObject("Board");
            go.transform.SetParent(root, false);

            // Над задником, лицом к игроку — то есть смотрит в −X.
            go.transform.SetPositionAndRotation(
                new Vector3(LaneEndX + 0.1f, 1.45f, LaneCenterZ),
                Quaternion.Euler(0f, -90f, 0f));

            TMP_Text status = BuildLine(go.transform, "Status", new Vector3(0f, 0.22f, 0f), 1.6f);
            TMP_Text best = BuildLine(go.transform, "Best", Vector3.zero, 0.8f);

            var board = go.AddComponent<HubActivityBoard>();

            var serialized = new SerializedObject(board);
            serialized.FindProperty("statusLine").objectReferenceValue = status;
            serialized.FindProperty("bestLine").objectReferenceValue = best;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return board;
        }

        private static TMP_Text BuildLine(Transform parent, string name, Vector3 localPosition, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            var label = go.AddComponent<TextMeshPro>();
            label.font = HubBarAssets.LetteringFont();
            label.text = string.Empty;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.color = HubCozyMaterials.Hex("F1DBAE");
            label.rectTransform.sizeDelta = new Vector2(1.6f, 0.3f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            return label;
        }

        /// <summary>
        /// Шкала силы стоит перед игроком, у начала дорожки: смотреть на неё
        /// и на кегли надо одновременно.
        /// </summary>
        private static HubActivityPowerGauge BuildGauge(Transform root)
        {
            var go = new GameObject("PowerGauge");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(StandX + 0.9f, 1.15f, LaneCenterZ);

            const float length = 0.9f;
            const float height = 0.08f;

            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Back";
            back.transform.SetParent(go.transform, false);
            back.transform.localScale = new Vector3(length, height, 0.02f);
            back.GetComponent<MeshRenderer>().sharedMaterial = HubOriginalAssets.Mat("Ink");
            Object.DestroyImmediate(back.GetComponent<Collider>());

            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "Fill";
            fill.transform.SetParent(go.transform, false);
            fill.transform.localPosition = new Vector3(0f, 0f, -0.012f);
            fill.transform.localScale = new Vector3(length, height * 0.7f, 0.02f);
            fill.GetComponent<MeshRenderer>().sharedMaterial = HubOriginalAssets.Mat("Ochre");
            Object.DestroyImmediate(fill.GetComponent<Collider>());

            var gauge = go.AddComponent<HubActivityPowerGauge>();

            var serialized = new SerializedObject(gauge);
            serialized.FindProperty("fill").objectReferenceValue = fill.transform;
            serialized.FindProperty("root").objectReferenceValue = go;
            serialized.FindProperty("length").floatValue = length;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return gauge;
        }

        /// <summary>
        /// Сама станция: коллайдер-триггер, которым её находит
        /// <c>PlayerInteractor</c>, плюс сетевой объект — без него намерение
        /// взаимодействия некуда адресовать.
        /// </summary>
        private static void BuildStation(Transform root, Transform standPoint, Transform ballHome,
            BowlingBall ball, BowlingPin[] pins, HubActivityBoard board, HubActivityPowerGauge gauge)
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
            serialized.FindProperty("board").objectReferenceValue = board;
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
