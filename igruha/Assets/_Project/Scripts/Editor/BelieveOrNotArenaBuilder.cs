using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Spawning;
using Igruha.Core.UI;
using Igruha.Minigames.BelieveOrNot;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Собирает «Верю / не верю» целиком: ассеты, сцену, арену, интерфейс
    /// и связи компонентов. Геометрия серая — арт приезжает в фазе 4.
    ///
    /// Всё строится кодом по той же причине, что «Экзамен» и цирк: размеры
    /// живут в <see cref="BelieveOrNotConfig"/>, и пересобрать зал после правки
    /// числа должно быть одним нажатием, а не часом в редакторе.
    ///
    /// Второе соображение важнее первого: сцены не переживают слияние веток.
    /// Собранная кодом сцена восстанавливается из конфига, разъехавшаяся
    /// руками — нет.
    /// </summary>
    public static class BelieveOrNotArenaBuilder
    {
        private const string SceneName = "BelieveOrNot";
        private const string ScenePath = "Assets/_Project/Scenes/Minigames/" + SceneName + ".unity";
        private const string TemplateScenePath = "Assets/_Project/Scenes/MinigameTemplate.unity";
        private const string SettingsFolder = "Assets/_Project/Settings/Gameplay/Minigames";

        private const string ArenaRoot = "_Arena";
        private const float WallThickness = 0.4f;

        // ========== 1. АССЕТЫ ==========

        [MenuItem("Igruha/Верю не верю/1. Создать ассеты")]
        public static void CreateAssets()
        {
            BelieveOrNotConfig config = LoadOrCreate<BelieveOrNotConfig>("BelieveOrNotConfig");
            QuickPhraseSet knower = LoadOrCreate<QuickPhraseSet>("BelieveKnowerPhrases");
            QuickPhraseSet decider = LoadOrCreate<QuickPhraseSet>("BelieveDeciderPhrases");
            MinigameDefinition definition = LoadOrCreate<MinigameDefinition>("BelieveOrNot");

            FillPhrases(knower, "Знающий",
                "Тебе повезло, не трогай",
                "Меняйся, я тебе добра желаю",
                "У тебя пустая, я вижу",
                "Клянусь, я не вру",
                "Делай что хочешь");

            FillPhrases(decider, "Решающий",
                "Верю",
                "Врёшь",
                "Почему ты такой спокойный?",
                "Ты слишком стараешься");

            FillDefinition(definition, config);

            AssetDatabase.SaveAssets();
            Debug.Log("🎴 Ассеты «Верю / не верю» готовы: конфиг, Definition и два набора реплик", config);
        }

        private static void FillPhrases(QuickPhraseSet set, string role, params string[] phrases)
        {
            var so = new SerializedObject(set);
            so.FindProperty("roleName").stringValue = role;

            SerializedProperty list = so.FindProperty("phrases");
            list.arraySize = phrases.Length;
            for (int i = 0; i < phrases.Length; i++)
            {
                list.GetArrayElementAtIndex(i).stringValue = phrases[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FillDefinition(MinigameDefinition definition, BelieveOrNotConfig config)
        {
            var so = new SerializedObject(definition);
            so.FindProperty("displayName").stringValue = "Верю / не верю";

            // Team: при 4–8 играют командами, при 2–3 счёт личный (спека 6.3).
            so.FindProperty("category").enumValueIndex = (int)MinigameCategory.Team;
            so.FindProperty("sceneName").stringValue = SceneName;

            // Это страховка на зависший кон, а не длина матча: штатный матч
            // на четырёх конах занимает около 212 с.
            so.FindProperty("roundDuration").floatValue = config != null ? config.MatchTimeoutSeconds : 360f;
            so.FindProperty("minPlayers").intValue = 2;
            so.FindProperty("maxPlayers").intValue = 8;
            so.FindProperty("cameraMode").enumValueIndex = (int)CameraMode.ThirdPerson;
            so.FindProperty("objective").stringValue =
                "Один за столом видел свою карточку. Второй — нет, и решает: оставить коробки или поменять. " +
                "У кого в итоге галочка, тот и выиграл кон. Остальные — болеют, подсказывают и мешают друг другу.";

            SerializedProperty hints = so.FindProperty("controlHints");
            hints.arraySize = 4;
            hints.GetArrayElementAtIndex(0).stringValue = "WASD — бег, ЛКМ — толкнуть";
            hints.GetArrayElementAtIndex(1).stringValue = "За столом: 1–5 — реплика";
            hints.GetArrayElementAtIndex(2).stringValue = "Решающий: ← оставить, → поменять";
            hints.GetArrayElementAtIndex(3).stringValue = "Tab — насмешки";

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ========== 2. СЦЕНА ==========

        [MenuItem("Igruha/Верю не верю/2. Собрать сцену")]
        public static void BuildScene()
        {
            BelieveOrNotConfig config = Find<BelieveOrNotConfig>();
            if (config == null)
            {
                EditorUtility.DisplayDialog("Верю / не верю",
                    "Не найден BelieveOrNotConfig. Сначала пункт «1. Создать ассеты».", "Ок");
                return;
            }

            if (!EnsureScene())
            {
                return;
            }

            StripTemplate();
            BelieveTable table = BuildArena(config);
            PlaceSpawnPoints(config);
            ApplyLighting(config);
            WireManager(config, table);
            RegisterInBuildSettings();

            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"🎴 Сцена «Верю / не верю» собрана: зал {config.HallWidth:F1}×{config.HallDepth:F1} м, " +
                      $"стол ⌀{config.TableDiameter:F2} м, зона зрителей R={config.SpectatorZoneRadius:F1} м, " +
                      $"кон {config.RoundSeconds:F0} с", table);
        }

        /// <summary>Открыть сцену игры, создав её копией шаблона, если её ещё нет.</summary>
        private static bool EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (!File.Exists(TemplateScenePath))
                {
                    EditorUtility.DisplayDialog("Верю / не верю",
                        $"Нет шаблона сцены {TemplateScenePath} — копировать нечего.", "Ок");
                    return false;
                }

                if (!AssetDatabase.CopyAsset(TemplateScenePath, ScenePath))
                {
                    EditorUtility.DisplayDialog("Верю / не верю", "Не удалось скопировать шаблон сцены.", "Ок");
                    return false;
                }

                AssetDatabase.Refresh();
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return true;
        }

        /// <summary>
        /// Убрать из копии шаблона то, чего в этой игре нет: ловушки, предметы
        /// и зону падения. Ям здесь нет вовсе, падать некуда — <c>KillZone</c>
        /// не нужен.
        /// </summary>
        private static void StripTemplate()
        {
            DestroyByName("_Traps");
            DestroyByName("_Pickups");
            DestroyByName("_Bounds");
            DestroyByName(ArenaRoot);

            TemplateMinigame template = Object.FindFirstObjectByType<TemplateMinigame>(FindObjectsInactive.Include);
            if (template != null)
            {
                Object.DestroyImmediate(template);
            }
        }

        // ========== АРЕНА ==========

        private static BelieveTable BuildArena(BelieveOrNotConfig config)
        {
            var root = new GameObject(ArenaRoot);
            Transform parent = root.transform;

            BuildHall(parent, config);
            GameObject table = BuildTable(parent, config);
            BuildBarrier(parent, config);
            BuildLamp(parent, config);

            return SetUpTable(table, parent, config);
        }

        private static void BuildHall(Transform parent, BelieveOrNotConfig config)
        {
            float w = config.HallWidth;
            float d = config.HallDepth;
            float h = config.CeilingHeight;
            float halfW = w * 0.5f;
            float halfD = d * 0.5f;

            SetLayer(CreateBox(parent, "Floor", new Vector3(w, WallThickness, d),
                new Vector3(0f, -WallThickness * 0.5f, 0f), new Color(0.10f, 0.12f, 0.11f)), "Ground");

            SetLayer(CreateBox(parent, "Ceiling", new Vector3(w, WallThickness, d),
                new Vector3(0f, h, 0f), new Color(0.06f, 0.06f, 0.07f)), "Ground");

            // ⚠️ Стены обязаны лежать на Ground: геометрия на Default для камеры
            // прозрачна, и деоклюдер выпустит её наружу (igruha/CLAUDE.md, 2a).
            var wallColor = new Color(0.14f, 0.10f, 0.13f);
            SetLayer(CreateBox(parent, "Wall_North", new Vector3(w, h, WallThickness),
                new Vector3(0f, h * 0.5f, halfD), wallColor), "Ground");
            SetLayer(CreateBox(parent, "Wall_South", new Vector3(w, h, WallThickness),
                new Vector3(0f, h * 0.5f, -halfD), wallColor), "Ground");
            SetLayer(CreateBox(parent, "Wall_West", new Vector3(WallThickness, h, d),
                new Vector3(-halfW, h * 0.5f, 0f), wallColor), "Ground");
            SetLayer(CreateBox(parent, "Wall_East", new Vector3(WallThickness, h, d),
                new Vector3(halfW, h * 0.5f, 0f), wallColor), "Ground");
        }

        private static GameObject BuildTable(Transform parent, BelieveOrNotConfig config)
        {
            var table = new GameObject("Table");
            table.transform.SetParent(parent, false);

            float radius = config.TableDiameter * 0.5f;

            GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            top.name = "TableTop";
            top.transform.SetParent(table.transform, false);
            top.transform.localScale = new Vector3(config.TableDiameter, config.TableHeight * 0.5f, config.TableDiameter);
            top.transform.localPosition = new Vector3(0f, config.TableHeight * 0.5f, 0f);
            Paint(top, new Color(0.09f, 0.24f, 0.15f));

            // У примитива-цилиндра капсульный коллайдер: столешница получилась
            // бы куполом. Меняем на коробку — она плоская сверху, и коробки
            // на ней стоят там, где поставлены.
            Object.DestroyImmediate(top.GetComponent<Collider>());
            var box = top.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 1f, 1f);

            SetLayer(top, "Ground");

            for (int seat = 0; seat < BelieveTable.SeatCount; seat++)
            {
                Vector3 direction = SeatDirection(seat);

                // Стул стоит за спиной, а не под сидящим: капсула персонажа
                // радиусом 0.36 м, и стул ближе просто выталкивает его с места.
                GameObject chair = CreateBox(table.transform, $"Chair_{seat}",
                    new Vector3(0.55f, config.TableHeight * 0.75f, 0.55f),
                    direction * (config.SeatDistance + 0.7f) + Vector3.up * config.TableHeight * 0.375f,
                    new Color(0.18f, 0.08f, 0.09f));
                SetLayer(chair, "Ground");
            }

            return table;
        }

        /// <summary>
        /// Невидимая крышка над столом: не даёт залезть на стол и спихнуть
        /// коробки.
        ///
        /// Это <b>объём над столешницей</b>, а не кольцо вокруг стола. Кольцо
        /// не годится: сидящие стоят вплотную к столу, и любое кольцо между
        /// столом и стульями упирается в них — физика выталкивает севшего
        /// с его места, и он оказывается внутри стола. Замерено на прогоне
        /// 25.08: точка посадки 1.80 м, а персонаж оказывался на 1.46 м.
        ///
        /// Капсула, а не коробка: коробка углами вылезает за круглый стол
        /// и снова достаёт до сидящих.
        ///
        /// ⚠️ Слой <c>Ignore Raycast</c>, а не <c>PlayerBarrier</c>. Последний
        /// входит в маску деоклюдера (`igruha/CLAUDE.md`, 2a), и камера
        /// зрителя, подбежавшего к столу, упёрлась бы в невидимую стену
        /// и нырнула ему в затылок. Барьер обязан держать тело и пропускать
        /// камеру — а это и есть слой вне маски.
        /// </summary>
        private static void BuildBarrier(Transform parent, BelieveOrNotConfig config)
        {
            var barrier = new GameObject("TableTopBlocker_Invisible");
            barrier.transform.SetParent(parent, false);
            barrier.transform.localPosition = Vector3.zero;

            float radius = config.BarrierRadius;
            var collider = barrier.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.radius = radius;

            // Цилиндрическая часть должна занять ровно объём над столешницей;
            // полусферы уходят внутрь стола и под пол, где никому не мешают.
            collider.height = config.BarrierHeight + radius * 2f;
            collider.center = new Vector3(0f, config.TableHeight + config.BarrierHeight * 0.5f, 0f);

            SetLayer(barrier, "Ignore Raycast");
        }

        private static void BuildLamp(Transform parent, BelieveOrNotConfig config)
        {
            var lamp = new GameObject("TableLamp");
            lamp.transform.SetParent(parent, false);
            lamp.transform.localPosition = new Vector3(0f, config.LampHeight, 0f);
            lamp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = config.LampSpotAngle;
            light.intensity = config.LampIntensity;
            light.color = config.LampColor;
            light.range = config.LampHeight * 3f;
            light.shadows = LightShadows.Soft;

            GameObject shade = CreateBox(parent, "LampShade",
                new Vector3(0.9f, 0.35f, 0.9f),
                new Vector3(0f, config.LampHeight + 0.2f, 0f),
                new Color(0.05f, 0.04f, 0.04f));
            SetLayer(shade, "Ground");
        }

        /// <summary>
        /// Довести стол до рабочего вида: места, точки камер, цели взгляда,
        /// пузыри реплик и две коробки.
        /// </summary>
        private static BelieveTable SetUpTable(GameObject table, Transform parent, BelieveOrNotConfig config)
        {
            var component = table.AddComponent<BelieveTable>();

            var seatAnchors = new Transform[BelieveTable.SeatCount];
            var cameraAnchors = new Transform[BelieveTable.SeatCount];
            var lookTargets = new Transform[BelieveTable.SeatCount];
            var bubbles = new SpeechBubble[BelieveTable.SeatCount];
            var boxes = new BelieveBox[BelieveTable.SeatCount];

            for (int seat = 0; seat < BelieveTable.SeatCount; seat++)
            {
                Vector3 direction = SeatDirection(seat);
                Vector3 seatPosition = direction * config.SeatDistance;

                var anchor = new GameObject($"Seat_{seat}");
                anchor.transform.SetParent(table.transform, false);
                anchor.transform.localPosition = seatPosition;
                // Лицом к столу, то есть к оппоненту напротив.
                anchor.transform.localRotation = Quaternion.LookRotation(-direction, Vector3.up);
                seatAnchors[seat] = anchor.transform;

                // Смотрят обе камеры в одну точку над центром стола, а не
                // в лицо оппоненту: только так в кадр помещаются и коробки,
                // и лицо, а свой затылок остаётся за нижней границей.
                var look = new GameObject($"LookTarget_{seat}");
                look.transform.SetParent(table.transform, false);
                look.transform.localPosition = Vector3.up * config.SeatLookHeight;
                lookTargets[seat] = look.transform;

                var bubble = new GameObject($"Bubble_{seat}");
                bubble.transform.SetParent(table.transform, false);
                bubble.transform.localPosition = seatPosition + Vector3.up * 1.9f;
                bubbles[seat] = BuildBubble(bubble);

                boxes[seat] = BuildBox(table.transform, config, seat);
            }

            // Точки камер ставятся вторым проходом: каждая смотрит на цель
            // напротив, а цели появляются только в первом.
            for (int seat = 0; seat < BelieveTable.SeatCount; seat++)
            {
                Vector3 direction = SeatDirection(seat);
                Vector3 position = direction * (config.SeatDistance + config.SeatCameraBack) +
                                   Vector3.up * config.SeatCameraHeight;

                var camAnchor = new GameObject($"SeatCamera_{seat}");
                camAnchor.transform.SetParent(table.transform, false);
                camAnchor.transform.localPosition = position;

                int opponent = BelieveTable.SeatCount - 1 - seat;
                Vector3 toOpponent = lookTargets[opponent].localPosition - position;
                camAnchor.transform.localRotation = Quaternion.LookRotation(toOpponent, Vector3.up);
                cameraAnchors[seat] = camAnchor.transform;
            }

            var so = new SerializedObject(component);
            AssignArray(so, "seatAnchors", seatAnchors);
            AssignArray(so, "seatCameraAnchors", cameraAnchors);
            AssignArray(so, "seatLookTargets", lookTargets);
            AssignArray(so, "seatBubbles", bubbles);
            AssignArray(so, "boxes", boxes);
            so.FindProperty("boxOffset").floatValue = config.BoxOffset;
            so.FindProperty("boxHeight").floatValue = config.TableHeight + config.BoxSize * 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();

            return component;
        }

        /// <summary>
        /// Коробка: корпус, крышка на петле, две карточки внутри и отблеск
        /// из щели. Обе коробки строятся одним и тем же кодом — иначе они
        /// перестанут быть неразличимыми, а на этом держится вся игра.
        /// </summary>
        private static BelieveBox BuildBox(Transform parent, BelieveOrNotConfig config, int seat)
        {
            float size = config.BoxSize;
            var root = new GameObject($"Box_{seat}");
            root.transform.SetParent(parent, false);
            root.transform.localPosition =
                SeatDirection(seat) * config.BoxOffset + Vector3.up * (config.TableHeight + size * 0.5f);
            root.transform.localRotation = Quaternion.LookRotation(-SeatDirection(seat), Vector3.up);

            var boxColor = new Color(0.34f, 0.22f, 0.12f);
            GameObject body = CreateBox(root.transform, "Body", new Vector3(size, size * 0.7f, size * 0.75f),
                Vector3.zero, boxColor);
            Object.DestroyImmediate(body.GetComponent<Collider>());

            // Петля сзади: крышка обязана откидываться назад, а не подниматься
            // параллельно столу.
            var hinge = new GameObject("LidHinge");
            hinge.transform.SetParent(root.transform, false);
            hinge.transform.localPosition = new Vector3(0f, size * 0.35f, -size * 0.375f);

            GameObject lid = CreateBox(hinge.transform, "Lid", new Vector3(size, size * 0.12f, size * 0.75f),
                new Vector3(0f, 0f, size * 0.375f), boxColor);
            Object.DestroyImmediate(lid.GetComponent<Collider>());

            GameObject win = CreateBox(root.transform, "Card_Win", new Vector3(size * 0.7f, 0.02f, size * 0.5f),
                new Vector3(0f, size * 0.3f, 0f), new Color(0.35f, 0.72f, 0.4f));
            Object.DestroyImmediate(win.GetComponent<Collider>());
            win.SetActive(false);

            GameObject lose = CreateBox(root.transform, "Card_Lose", new Vector3(size * 0.7f, 0.02f, size * 0.5f),
                new Vector3(0f, size * 0.3f, 0f), new Color(0.75f, 0.32f, 0.3f));
            Object.DestroyImmediate(lose.GetComponent<Collider>());
            lose.SetActive(false);

            var glow = new GameObject("PeekGlow");
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, size * 0.3f, 0f);
            Light glowLight = glow.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = new Color(1f, 0.8f, 0.5f);
            glowLight.intensity = 2.5f;
            glowLight.range = 1.2f;
            glow.SetActive(false);

            ParticleSystem gag = BuildGagPuff(root.transform, size);

            var component = root.AddComponent<BelieveBox>();
            var so = new SerializedObject(component);
            so.FindProperty("lid").objectReferenceValue = hinge.transform;
            so.FindProperty("winCard").objectReferenceValue = win;
            so.FindProperty("loseCard").objectReferenceValue = lose;
            so.FindProperty("peekGlow").objectReferenceValue = glow;
            so.FindProperty("gagPuff").objectReferenceValue = gag;
            so.ApplyModifiedPropertiesWithoutUndo();

            return component;
        }

        /// <summary>
        /// Облачко в лицо проигравшему. Чистая косметика: персонажа не двигает,
        /// мест не меняет, следующему кону не мешает. Ему нужен панчлайн,
        /// а не наказание.
        /// </summary>
        private static ParticleSystem BuildGagPuff(Transform parent, float size)
        {
            var go = new GameObject("GagPuff");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, size * 0.45f, 0f);
            go.transform.localRotation = Quaternion.Euler(-60f, 0f, 0f);

            var system = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 1.1f;
            main.startSpeed = 2.2f;
            main.startSize = 0.35f;
            main.startColor = new Color(0.85f, 0.85f, 0.88f, 0.7f);
            main.gravityModifier = -0.05f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.05f;

            // ⚠️ У ParticleSystemRenderer по умолчанию встроенный материал не
            // из URP: в сборке он стал бы фиолетовым, хотя в редакторе выглядит
            // нормально (тот же класс бага, что STATE 3.9).
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null)
            {
                renderer.sharedMaterial = new Material(shader);
            }

            return system;
        }

        private static SpeechBubble BuildBubble(GameObject host)
        {
            var canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            host.AddComponent<CanvasScaler>();

            var rect = (RectTransform)host.transform;
            rect.sizeDelta = new Vector2(320f, 90f);
            rect.localScale = Vector3.one * 0.004f;

            var panel = new GameObject("Root", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 0.85f);

            TMP_Text label = CreateText(panelRect, "Label", string.Empty, 26f, TextAlignmentOptions.Center);

            var bubble = host.AddComponent<SpeechBubble>();
            var so = new SerializedObject(bubble);
            so.FindProperty("root").objectReferenceValue = panel;
            so.FindProperty("label").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();

            return bubble;
        }

        // ========== СПАВНЫ И СВЕТ ==========

        /// <summary>
        /// Разложить стартовые точки по окружности вокруг стола, лицом к нему.
        /// Точки роли <c>Special</c> шаблона не трогаем: особых мест на арене
        /// нет, за стол игроков переносит рассадка, а не спавнер.
        /// </summary>
        private static void PlaceSpawnPoints(BelieveOrNotConfig config)
        {
            SpawnPoint[] points = Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var defaults = new List<SpawnPoint>(8);

            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].Role == SpawnRole.Default)
                {
                    defaults.Add(points[i]);
                }
            }

            defaults.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            for (int i = 0; i < defaults.Count; i++)
            {
                float angle = 360f / Mathf.Max(1, defaults.Count) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                defaults[i].transform.SetPositionAndRotation(
                    direction * config.SpawnRingRadius + Vector3.up * 0.1f,
                    Quaternion.LookRotation(-direction, Vector3.up));
            }
        }

        /// <summary>
        /// Свет — часть механики, а не украшение: в темноте зрители не
        /// отвлекают от лица соперника. Поэтому ставится уже в блокауте,
        /// а не откладывается на арт.
        /// </summary>
        private static void ApplyLighting(BelieveOrNotConfig config)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = config.AmbientColor;
            RenderSettings.fog = false;

            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type != LightType.Directional)
                {
                    continue;
                }

                // Не выключаем совсем: без него силуэты зрителей сливаются
                // в чёрное пятно и по залу непонятно, где кто.
                lights[i].intensity = 0.06f;
                lights[i].color = new Color(0.55f, 0.6f, 0.8f);
                lights[i].shadows = LightShadows.None;
            }
        }

        // ========== ИНТЕРФЕЙС И СВЯЗИ ==========

        private static void WireManager(BelieveOrNotConfig config, BelieveTable table)
        {
            var bootstrap = Object.FindFirstObjectByType<MinigameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap == null)
            {
                Debug.LogError("🎴 В сцене нет MinigameBootstrap — мини-игра не стартует");
                return;
            }

            GameObject manager = bootstrap.gameObject;

            BelieveOrNotMinigame game = manager.GetComponent<BelieveOrNotMinigame>()
                                        ?? manager.AddComponent<BelieveOrNotMinigame>();
            MinigameStageState stage = manager.GetComponent<MinigameStageState>()
                                       ?? manager.AddComponent<MinigameStageState>();
            BelieveDebugBot bot = manager.GetComponent<BelieveDebugBot>()
                                  ?? manager.AddComponent<BelieveDebugBot>();

            var hud = Object.FindFirstObjectByType<RoundHud>(FindObjectsInactive.Include);
            var tutorial = Object.FindFirstObjectByType<TutorialScreen>(FindObjectsInactive.Include);
            var timer = Object.FindFirstObjectByType<RoundTimer>(FindObjectsInactive.Include);
            var camera = Object.FindFirstObjectByType<MinigameCameraController>(FindObjectsInactive.Include);
            var canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);

            Transform uiRoot = canvas != null ? canvas.transform : null;
            BelievePeekView peek = BuildPeekView(uiRoot);
            BelieveDecisionPanel decision = BuildDecisionPanel(uiRoot);
            QuickPhrasePanel phrases = BuildPhrasePanel(uiRoot);
            Transform fixedRig = BuildFixedRig(camera, config);

            // Ассеты перечитываются здесь, а не берутся из аргумента: между
            // началом сборки и этой строкой успевает случиться и копирование
            // сцены, и AssetDatabase.Refresh, после которого прежняя ссылка
            // указывает на выгруженный объект и записывается как пустая.
            var so = new SerializedObject(game);
            so.FindProperty("definition").objectReferenceValue = Find<MinigameDefinition>("BelieveOrNot");
            so.FindProperty("roundTimer").objectReferenceValue = timer;
            so.FindProperty("tutorialScreen").objectReferenceValue = tutorial;
            so.FindProperty("hud").objectReferenceValue = hud;
            so.FindProperty("config").objectReferenceValue = Find<BelieveOrNotConfig>("BelieveOrNotConfig");
            so.FindProperty("table").objectReferenceValue = table;
            so.FindProperty("stageState").objectReferenceValue = stage;
            so.FindProperty("cameraController").objectReferenceValue = camera;
            so.FindProperty("peekView").objectReferenceValue = peek;
            so.FindProperty("decisionPanel").objectReferenceValue = decision;
            so.FindProperty("phrasePanel").objectReferenceValue = phrases;
            so.FindProperty("knowerPhrases").objectReferenceValue = Find<QuickPhraseSet>("BelieveKnowerPhrases");
            so.FindProperty("deciderPhrases").objectReferenceValue = Find<QuickPhraseSet>("BelieveDeciderPhrases");
            so.ApplyModifiedPropertiesWithoutUndo();

            VerifyWiring(game);

            var botSo = new SerializedObject(bot);
            botSo.FindProperty("game").objectReferenceValue = game;
            botSo.FindProperty("table").objectReferenceValue = table;
            botSo.ApplyModifiedPropertiesWithoutUndo();

            var bootSo = new SerializedObject(bootstrap);
            bootSo.FindProperty("minigame").objectReferenceValue = game;
            bootSo.ApplyModifiedPropertiesWithoutUndo();

            if (fixedRig != null)
            {
                var tableSo = new SerializedObject(table);
                tableSo.FindProperty("fixedCameraRig").objectReferenceValue = fixedRig;
                tableSo.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Проверить, что после сборки не осталось пустых ссылок.
        ///
        /// Сборщик сцены кодом молчаливо ошибается легче, чем человек в
        /// инспекторе: пустое поле видно только в рантайме и только как
        /// «мини-игра не настроена». Дешевле сказать об этом сразу и по имени.
        /// </summary>
        private static void VerifyWiring(BelieveOrNotMinigame game)
        {
            string[] required =
            {
                "definition", "roundTimer", "tutorialScreen", "hud", "config", "table",
                "stageState", "cameraController", "peekView", "decisionPanel", "phrasePanel",
                "knowerPhrases", "deciderPhrases"
            };

            var missing = new List<string>();
            var so = new SerializedObject(game);

            for (int i = 0; i < required.Length; i++)
            {
                SerializedProperty property = so.FindProperty(required[i]);
                if (property == null || property.objectReferenceValue == null)
                {
                    missing.Add(required[i]);
                }
            }

            if (missing.Count > 0)
            {
                Debug.LogError($"🎴 Сцена собрана, но пустыми остались ссылки: {string.Join(", ", missing)}", game);
            }
        }

        /// <summary>
        /// Фиксированный риг сидящих. Он один на сцену: игра переставляет его
        /// к нужному месту перед показом — режим <c>Fixed</c> в Core выбирает
        /// риг, но позицию не задаёт.
        /// </summary>
        private static Transform BuildFixedRig(MinigameCameraController controller, BelieveOrNotConfig config)
        {
            if (controller == null)
            {
                return null;
            }

            var so = new SerializedObject(controller);
            SerializedProperty property = so.FindProperty("fixedRig");

            var existing = property.objectReferenceValue as CinemachineCamera;
            if (existing == null)
            {
                var go = new GameObject("BelieveSeatRig", typeof(CinemachineCamera));
                existing = go.GetComponent<CinemachineCamera>();
                property.objectReferenceValue = existing;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Риг живёт рядом с остальными в _Camera, а не в корне сцены:
            // пересборка не должна оставлять сирот на верхнем уровне.
            existing.transform.SetParent(controller.transform, false);

            LensSettings lens = existing.Lens;
            lens.FieldOfView = config.SeatCameraFov;
            existing.Lens = lens;
            existing.gameObject.SetActive(false);

            return existing.transform;
        }

        private static BelievePeekView BuildPeekView(Transform uiRoot)
        {
            GameObject host = EnsureUiHost(uiRoot, "PeekView");
            BelievePeekView view = host.GetComponent<BelievePeekView>() ?? host.AddComponent<BelievePeekView>();

            RectTransform root = CreatePanel(host.transform, "Root", new Vector2(460f, 300f), Vector2.zero,
                new Color(0.04f, 0.04f, 0.06f, 0.94f));

            var cardGo = new GameObject("Card", typeof(RectTransform), typeof(Image));
            var cardRect = (RectTransform)cardGo.transform;
            cardRect.SetParent(root, false);
            cardRect.sizeDelta = new Vector2(240f, 150f);
            cardRect.anchoredPosition = new Vector2(0f, 40f);
            var cardImage = cardGo.GetComponent<Image>();

            TMP_Text cardLabel = CreateText(cardRect, "CardLabel", "—", 40f, TextAlignmentOptions.Center);
            TMP_Text hint = CreateText(root, "Hint", string.Empty, 20f, TextAlignmentOptions.Center);
            ((RectTransform)hint.transform).anchoredPosition = new Vector2(0f, -70f);
            TMP_Text countdown = CreateText(root, "Countdown", string.Empty, 30f, TextAlignmentOptions.Center);
            ((RectTransform)countdown.transform).anchoredPosition = new Vector2(0f, -115f);

            var so = new SerializedObject(view);
            so.FindProperty("root").objectReferenceValue = root.gameObject;
            so.FindProperty("cardImage").objectReferenceValue = cardImage;
            so.FindProperty("cardLabel").objectReferenceValue = cardLabel;
            so.FindProperty("hintText").objectReferenceValue = hint;
            so.FindProperty("countdownText").objectReferenceValue = countdown;
            so.ApplyModifiedPropertiesWithoutUndo();

            root.gameObject.SetActive(false);
            return view;
        }

        private static BelieveDecisionPanel BuildDecisionPanel(Transform uiRoot)
        {
            GameObject host = EnsureUiHost(uiRoot, "DecisionPanel");
            BelieveDecisionPanel panel = host.GetComponent<BelieveDecisionPanel>()
                                         ?? host.AddComponent<BelieveDecisionPanel>();

            RectTransform root = CreatePanel(host.transform, "Root", new Vector2(720f, 190f),
                new Vector2(0f, -260f), new Color(0.04f, 0.04f, 0.06f, 0.9f));

            Button keep = CreateButton(root, "KeepButton", "← ОСТАВИТЬ", new Vector2(-170f, 20f));
            Button swap = CreateButton(root, "SwapButton", "ПОМЕНЯТЬ →", new Vector2(170f, 20f));

            TMP_Text hint = CreateText(root, "Hint", string.Empty, 20f, TextAlignmentOptions.Center);
            ((RectTransform)hint.transform).anchoredPosition = new Vector2(0f, -55f);
            TMP_Text countdown = CreateText(root, "Countdown", string.Empty, 28f, TextAlignmentOptions.Center);
            ((RectTransform)countdown.transform).anchoredPosition = new Vector2(0f, 75f);

            var so = new SerializedObject(panel);
            so.FindProperty("root").objectReferenceValue = root.gameObject;
            so.FindProperty("keepButton").objectReferenceValue = keep;
            so.FindProperty("swapButton").objectReferenceValue = swap;
            so.FindProperty("hintText").objectReferenceValue = hint;
            so.FindProperty("countdownText").objectReferenceValue = countdown;
            so.ApplyModifiedPropertiesWithoutUndo();

            root.gameObject.SetActive(false);
            return panel;
        }

        private static QuickPhrasePanel BuildPhrasePanel(Transform uiRoot)
        {
            GameObject host = EnsureUiHost(uiRoot, "PhrasePanel");
            QuickPhrasePanel panel = host.GetComponent<QuickPhrasePanel>() ?? host.AddComponent<QuickPhrasePanel>();

            RectTransform root = CreatePanel(host.transform, "Root", new Vector2(420f, 260f),
                new Vector2(-430f, -140f), new Color(0.04f, 0.04f, 0.06f, 0.86f));

            TMP_Text hint = CreateText(root, "Hint", string.Empty, 19f, TextAlignmentOptions.Center);
            var hintRect = (RectTransform)hint.transform;
            hintRect.anchorMin = new Vector2(0f, 1f);
            hintRect.anchorMax = new Vector2(1f, 1f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.sizeDelta = new Vector2(0f, 28f);
            hintRect.anchoredPosition = Vector2.zero;

            var containerGo = new GameObject("Rows", typeof(RectTransform));
            var container = (RectTransform)containerGo.transform;
            container.SetParent(root, false);
            container.anchorMin = new Vector2(0f, 1f);
            container.anchorMax = new Vector2(1f, 1f);
            container.pivot = new Vector2(0.5f, 1f);
            container.offsetMin = new Vector2(10f, 0f);
            container.offsetMax = new Vector2(-10f, -34f);
            container.sizeDelta = new Vector2(-20f, 210f);

            var so = new SerializedObject(panel);
            so.FindProperty("root").objectReferenceValue = root.gameObject;
            so.FindProperty("container").objectReferenceValue = container;
            so.FindProperty("hintText").objectReferenceValue = hint;
            so.ApplyModifiedPropertiesWithoutUndo();

            root.gameObject.SetActive(false);
            return panel;
        }

        // ========== СБОРКА ==========

        /// <summary>
        /// Прописать сцену в Build Settings. ⚠️ В Addressables её класть нельзя:
        /// NGO опознаёт сцены по индексу в списке сборки, и сцена без индекса
        /// по сети не загрузится (MinigameTemplate_HOWTO).
        /// </summary>
        private static void RegisterInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == ScenePath)
                {
                    scenes[i] = new EditorBuildSettingsScene(ScenePath, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ========== МЕЛОЧИ ==========

        /// <summary>Направление от центра стола к месту: одно вперёд, второе назад.</summary>
        private static Vector3 SeatDirection(int seat) => seat == 0 ? Vector3.forward : Vector3.back;

        private static GameObject EnsureUiHost(Transform uiRoot, string name)
        {
            if (uiRoot != null)
            {
                Transform existing = uiRoot.Find(name);
                if (existing != null)
                {
                    Object.DestroyImmediate(existing.gameObject);
                }
            }

            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(uiRoot, false);

            var rect = (RectTransform)host.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return host;
        }

        private static RectTransform CreatePanel(Transform parent, string name, Vector2 size, Vector2 position,
            Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            go.GetComponent<Image>().color = color;
            return rect;
        }

        private static Button CreateButton(Transform parent, string name, string caption, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(300f, 70f);
            rect.anchoredPosition = position;
            go.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.2f, 0.95f);

            CreateText(rect, "Label", caption, 26f, TextAlignmentOptions.Center);
            return go.GetComponent<Button>();
        }

        private static TMP_Text CreateText(Transform parent, string name, string content, float size,
            TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(420f, 60f);
            rect.anchoredPosition = Vector2.zero;

            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = new Color(0.93f, 0.91f, 0.85f);
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateBox(Transform parent, string name, Vector3 size, Vector3 position, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            Paint(go, color);
            return go;
        }

        /// <summary>
        /// ⚠️ <c>CreatePrimitive</c> вешает встроенный Default-Material, шейдер
        /// которого не из URP и в сборку не попадает: в билде объект стал бы
        /// фиолетовым, хотя в редакторе выглядит нормально (STATE 3.9).
        /// </summary>
        private static void Paint(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("Шейдер 'Universal Render Pipeline/Lit' не найден — блокаут будет фиолетовым в сборке");
                return;
            }

            renderer.sharedMaterial = new Material(shader) { color = color };
        }

        private static void SetLayer(GameObject go, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                Debug.LogError($"Слоя '{layerName}' нет в проекте — {go.name} поведёт себя не так, как задумано");
                return;
            }

            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayer(child.gameObject, layerName);
            }
        }

        private static void DestroyByName(string name)
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void AssignArray(SerializedObject so, string propertyName, Object[] values)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static T LoadOrCreate<T>(string assetName) where T : ScriptableObject
        {
            T existing = Find<T>(assetName);
            if (existing != null)
            {
                return existing;
            }

            if (!Directory.Exists(SettingsFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
                AssetDatabase.Refresh();
            }

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, $"{SettingsFolder}/{assetName}.asset");
            return created;
        }

        private static T Find<T>(string assetName = null) where T : Object
        {
            string filter = assetName == null ? $"t:{typeof(T).Name}" : $"t:{typeof(T).Name} {assetName}";
            string[] guids = AssetDatabase.FindAssets(filter);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (assetName != null && Path.GetFileNameWithoutExtension(path) != assetName)
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                {
                    return asset;
                }
            }

            return null;
        }
    }
}
