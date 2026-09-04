using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Igruha.Core.UI;
using Igruha.Minigames.Circus;
using Igruha.Minigames.Stopwatch;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Реквизит «Секундомера»: красная кнопка на тумбе в слот каждой клетки.
    ///
    /// Отдельно от билдера арены, потому что арена общая с «Порядком банок»:
    /// там в тот же слот встанет полка с банками. Клетка про содержимое слота
    /// не знает, и это единственное место, которое знает.
    ///
    /// Кнопка — настоящий префаб, в отличие от арены: у неё нет ни одного
    /// размера, который приходил бы из конфига, поэтому замораживать нечего.
    /// </summary>
    internal static class StopwatchPropBuilder
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs/Minigames/Stopwatch";
        private const string PrefabPath = PrefabFolder + "/CageButton.prefab";
        private const string HudObjectName = "StopwatchHud";
        private const string StatusObjectName = "StopwatchStatus";

        // ---- Кнопка после дресса 4.1. Замер до/после — в шапке BuildPrefab.
        private const float PedestalHeight = 0.85f;

        /// <summary>Диаметр коллайдера тумбы. Чуть уже видимой бочки, чтобы игрок не упирался в воздух перед ней.</summary>
        private const float PedestalDiameter = 0.62f;

        /// <summary>Купол кнопки: сплюснутая сфера, а не цилиндр.</summary>
        private const float CapHeight = 0.21f;
        private const float CapDiameter = 0.42f;

        /// <summary>Ободок вокруг купола — то, во что кнопка утоплена.</summary>
        private const float RimHeight = 0.07f;
        private const float RimDiameter = 0.54f;

        [MenuItem("Igruha/Minigames/Rebuild Stopwatch Props")]
        private static void Rebuild()
        {
            GameObject prefab = BuildPrefab();
            if (prefab == null)
            {
                return;
            }

            GameObject cagesRoot = GameObject.Find("_Arena/Cages");
            if (cagesRoot == null)
            {
                Debug.LogError("StopwatchPropBuilder: открой сцену Stopwatch — не найден _Arena/Cages.");
                return;
            }

            var stations = cagesRoot.GetComponentsInChildren<CageStation>(true);
            int placed = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                Transform slot = stations[i].PropSlot;
                if (slot == null)
                {
                    Debug.LogWarning($"StopwatchPropBuilder: у клетки {stations[i].name} нет слота реквизита", stations[i]);
                    continue;
                }

                for (int c = slot.childCount - 1; c >= 0; c--)
                {
                    Object.DestroyImmediate(slot.GetChild(c).gameObject);
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slot);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                placed++;
            }

            int faces = BuildScreenHud();

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log($"Реквизит «Секундомера» расставлен: кнопок {placed} из {stations.Length} клеток, граней табло {faces} (включая экранное).");
        }


        // Экранное табло. Размеры в пикселях канваса.
        private const float HudWidth = 600f;
        private const float HudPadding = 18f;
        private const float HudTitleHeight = 50f;
        private const float HudSubtitleHeight = 42f;
        private const float HudRowHeight = 40f;
        private const int HudRowCapacity = 8;
        private const float HudTitleFont = 34f;
        private const float HudSubtitleFont = 29f;
        private const float HudRowFont = 26f;

        /// <summary>
        /// Личное табло в углу экрана. Через прутья клетки мировое табло
        /// читается плохо, а результаты — единственное, что игроку в этот
        /// момент нужно разобрать точно.
        ///
        /// Своего кода не требует: это ещё одна грань <see cref="WorldScoreboard"/>.
        /// Правила игры пишут содержимое один раз, и оно приезжает и на четыре
        /// грани над ямой, и сюда. Мировое табло при этом остаётся —
        /// по нему читается общая картина, по экранному свои цифры.
        /// </summary>
        private static int BuildScreenHud()
        {
            GameObject canvas = GameObject.Find("_UI/Canvas");
            if (canvas == null)
            {
                Debug.LogWarning("StopwatchPropBuilder: не найден _UI/Canvas — экранное табло не построено.");
                return 0;
            }

            Transform existing = canvas.transform.Find(HudObjectName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            TMP_FontAsset font = FindFont();
            float height = HudTitleHeight + HudSubtitleHeight + HudRowHeight * HudRowCapacity + HudPadding * 2f;

            var rootGo = new GameObject(HudObjectName, typeof(RectTransform));
            rootGo.transform.SetParent(canvas.transform, false);
            var root = (RectTransform)rootGo.transform;
            // Левый верхний угол: центр занят таймером, низ — подсказками.
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(28f, -28f);
            root.sizeDelta = new Vector2(HudWidth, height);

            var backdrop = rootGo.AddComponent<Image>();
            backdrop.color = new Color(0.02f, 0.02f, 0.04f, 0.72f);
            backdrop.raycastTarget = false;

            float cursor = -HudPadding;
            TMP_Text title = MakeHudText(root, "Title", font, HudTitleFont, TextAlignmentOptions.MidlineLeft,
                cursor, HudTitleHeight, HudPadding, HudWidth - HudPadding * 2f);
            title.color = new Color(1f, 0.86f, 0.45f);
            cursor -= HudTitleHeight;
            TMP_Text subtitle = MakeHudText(root, "Subtitle", font, HudSubtitleFont, TextAlignmentOptions.MidlineLeft,
                cursor, HudSubtitleHeight, HudPadding, HudWidth - HudPadding * 2f);
            cursor -= HudSubtitleHeight;

            var labels = new TMP_Text[HudRowCapacity];
            var values = new TMP_Text[HudRowCapacity];
            for (int i = 0; i < HudRowCapacity; i++)
            {
                float y = cursor - HudRowHeight * i;
                labels[i] = MakeHudText(root, $"Row_{i + 1}_Label", font, HudRowFont, TextAlignmentOptions.MidlineLeft,
                    y, HudRowHeight, HudPadding, (HudWidth - HudPadding * 2f) * 0.6f);
                values[i] = MakeHudText(root, $"Row_{i + 1}_Value", font, HudRowFont, TextAlignmentOptions.MidlineRight,
                    y, HudRowHeight, HudPadding + (HudWidth - HudPadding * 2f) * 0.6f, (HudWidth - HudPadding * 2f) * 0.4f);
                // Значение — то, ради чего игрок сюда смотрит: держим его ярче подписи.
                labels[i].color = new Color(0.78f, 0.80f, 0.86f);
            }

            var face = rootGo.AddComponent<WorldScoreboardFace>();
            var faceSo = new SerializedObject(face);
            faceSo.FindProperty("title").objectReferenceValue = title;
            faceSo.FindProperty("subtitle").objectReferenceValue = subtitle;
            SerializedProperty labelsProperty = faceSo.FindProperty("rowLabels");
            SerializedProperty valuesProperty = faceSo.FindProperty("rowValues");
            labelsProperty.arraySize = HudRowCapacity;
            valuesProperty.arraySize = HudRowCapacity;
            for (int i = 0; i < HudRowCapacity; i++)
            {
                labelsProperty.GetArrayElementAtIndex(i).objectReferenceValue = labels[i];
                valuesProperty.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            faceSo.ApplyModifiedPropertiesWithoutUndo();

            BuildStatusLine(canvas.transform, font, root);
            return RegisterFaces(face);
        }

        /// <summary>
        /// Крупная строка «отсчёт идёт» под панелью. Единственный элемент
        /// интерфейса, у каждого игрока свой: остальное — общее табло.
        /// </summary>
        private static void BuildStatusLine(Transform canvas, TMP_FontAsset font, RectTransform panel)
        {
            Transform existing = canvas.Find(StatusObjectName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = new GameObject(StatusObjectName, typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(panel.anchoredPosition.x, panel.anchoredPosition.y - panel.sizeDelta.y - 14f);
            rect.sizeDelta = new Vector2(HudWidth, 56f);

            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }

            text.fontSizeMin = 26f;
            text.fontSizeMax = 44f;
            text.enableAutoSizing = true;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = string.Empty;

            var hud = go.AddComponent<StopwatchLocalHud>();
            var so = new SerializedObject(hud);
            so.FindProperty("label").objectReferenceValue = text;
            so.FindProperty("game").objectReferenceValue = Object.FindAnyObjectByType<StopwatchMinigame>(FindObjectsInactive.Include);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Собрать грани заново: четыре над ямой плюс экранная. Билдер арены
        /// знает только про свои четыре и при пересборке затирает список,
        /// поэтому экранную грань дописываем здесь.
        /// </summary>
        private static int RegisterFaces(WorldScoreboardFace hud)
        {
            GameObject boardGo = GameObject.Find("_Arena/Scoreboard");
            if (boardGo == null)
            {
                Debug.LogWarning("StopwatchPropBuilder: не найдено _Arena/Scoreboard — пересобери арену.");
                return 0;
            }

            var board = boardGo.GetComponent<WorldScoreboard>();
            var faces = new List<WorldScoreboardFace>(5);
            faces.AddRange(boardGo.GetComponentsInChildren<WorldScoreboardFace>(true));
            faces.Add(hud);
            board.SetFaces(faces);
            board.Clear();
            return faces.Count;
        }

        private static TMP_Text MakeHudText(RectTransform parent, string name, TMP_FontAsset font, float fontSize,
            TextAlignmentOptions alignment, float top, float height, float left, float width)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, top);
            rect.sizeDelta = new Vector2(width, height);

            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }

            text.fontSizeMin = fontSize * 0.6f;
            text.fontSizeMax = fontSize;
            text.enableAutoSizing = true;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        private static TMP_FontAsset FindFont()
        {
            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            TextMeshProUGUI existing = Object.FindAnyObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
            return existing != null ? existing.font : null;
        }

        /// <summary>Покрасить примитив тоном общей палитры цирка.</summary>
        private static void PaintCircus(GameObject go, CircusPalette.Tone tone)
        {
            Material material = CircusPalette.Get(tone);
            if (material != null)
            {
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        /// <summary>
        /// Префаб кнопки. Дресс 4.1 заменил заглушку блокаута целиком:
        /// цилиндр-тумба и цилиндр-крышка стали цирковой бочкой со звёздами
        /// и красным куполом в металлическом ободке.
        ///
        /// 🔴 <b>Коллайдер взаимодействия вырос — и это объявлено.</b>
        ///
        /// | | было | стало |
        /// |---|---|---|
        /// | высота тумбы | 0.55 м | 0.85 м |
        /// | диаметр тумбы | 0.42 м | 0.62 м |
        /// | купол | цилиндр Ø 0.30 × 0.12 | сфера Ø 0.42 × 0.21 |
        /// | верх кнопки | 0.67 м | 1.06 м |
        ///
        /// Причина роста: на 0.55 м кнопка приходилась персонажу ниже колена,
        /// и в кадре из клетки её съедал решётчатый пол — а это единственное
        /// действие в игре. На 0.85 м купол выходит на уровень пояса.
        ///
        /// <b>Поиск интерактива это не ломает.</b> <c>PlayerInteractor</c>
        /// берёт <c>OverlapSphere</c> радиусом 1.80 м от корня персонажа,
        /// то есть от пола клетки. Спавн стоит в 0.72 м от центра клетки, где
        /// кнопка. До центра коллайдера было √(0.72² + 0.275²) = 0.77 м, стало
        /// √(0.72² + 0.425²) = 0.84 м — обе величины втрое меньше радиуса
        /// поиска, и ближняя точка коллайдера ещё ближе.
        ///
        /// Ширина 0.62 м выбрана <b>уже</b> видимой бочки (0.67 м): игрок
        /// должен упираться в бочку, а не в воздух перед ней.
        /// </summary>
        private static GameObject BuildPrefab()
        {
            if (!Directory.Exists(PrefabFolder))
            {
                Directory.CreateDirectory(PrefabFolder);
                AssetDatabase.Refresh();
            }

            var root = new GameObject("CageButton");
            try
            {
                // Тумба. Коллайдер на ней же: PlayerInteractor ищет интерактив
                // через OverlapSphere, и без коллайдера кнопка невидима для поиска.
                //
                // Рендерер тумбы гаснет — её место занимает цирковая бочка
                // пака. Сама коробка остаётся: она и есть объём, в который
                // упирается игрок и который находит поиск интерактива.
                GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pedestal.name = "Pedestal";
                pedestal.transform.SetParent(root.transform, false);
                pedestal.transform.localPosition = new Vector3(0f, PedestalHeight * 0.5f, 0f);
                pedestal.transform.localScale = new Vector3(PedestalDiameter, PedestalHeight * 0.5f, PedestalDiameter);
                pedestal.GetComponent<MeshRenderer>().enabled = false;

                CircusDress.Prop(root.transform, "Barrel", CircusDress.BarrelPath,
                    root.transform.position, 0f, PedestalHeight);

                // Ободок, в который утоплен купол. Без него красный шар просто
                // лежит на бочке и не читается кнопкой.
                GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                rim.name = "Rim";
                rim.transform.SetParent(root.transform, false);
                rim.transform.localPosition = new Vector3(0f, PedestalHeight + RimHeight * 0.5f, 0f);
                rim.transform.localScale = new Vector3(RimDiameter, RimHeight * 0.5f, RimDiameter);
                Object.DestroyImmediate(rim.GetComponent<Collider>());
                PaintCircus(rim, CircusPalette.Tone.CageMetal);

                // Купол — сплюснутая сфера, а не цилиндр: единственное действие
                // в игре обязано выглядеть кнопкой, которую хочется ударить
                // ладонью, а не крышкой люка.
                //
                // Материал белый и это не описка: цвет купола ведёт CageButton
                // через MaterialPropertyBlock — покой, свой отсчёт, чужой
                // отсчёт. Любая своя окраска под ним перемножилась бы
                // с назначенной, и «горит» перестало бы отличаться от «нет».
                GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cap.name = "Lamp";
                cap.transform.SetParent(root.transform, false);
                cap.transform.localPosition = new Vector3(0f, PedestalHeight + RimHeight * 0.4f, 0f);
                cap.transform.localScale = new Vector3(CapDiameter, CapHeight * 2f, CapDiameter);
                Object.DestroyImmediate(cap.GetComponent<Collider>());
                PaintCircus(cap, CircusPalette.Tone.ButtonBase);

                // Лампа-маяк: сама кнопка мелкая и её заслоняет спина персонажа,
                // а залитая светом клетка читается и краем глаза.
                var beaconGo = new GameObject("Beacon");
                beaconGo.transform.SetParent(root.transform, false);
                beaconGo.transform.localPosition = new Vector3(0f, PedestalHeight + CapHeight + 0.35f, 0f);
                var beacon = beaconGo.AddComponent<Light>();
                beacon.type = LightType.Point;
                // Свет заметный, но не заливающий: на полной яркости клетка
                // становилась сплошным красным пятном и табло в ней тонуло.
                beacon.range = 3.2f;
                beacon.intensity = 1.8f;
                beacon.enabled = false;

                var button = root.AddComponent<CageButton>();
                var serialized = new SerializedObject(button);
                serialized.FindProperty("lamp").objectReferenceValue = cap.GetComponent<Renderer>();
                serialized.FindProperty("beacon").objectReferenceValue = beacon;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
