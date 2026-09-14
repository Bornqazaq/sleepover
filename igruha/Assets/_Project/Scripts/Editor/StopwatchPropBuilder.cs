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

            int faces = BuildLocalFeedback();

            // Кнопки только что появились — перевязываем звук, чтобы
            // StopwatchAudio получил их ссылки. Ставить звук раньше нечего:
            // до этой строки кнопок в сцене нет.
            GameObject arena = GameObject.Find("_Arena");
            if (arena != null)
            {
                CircusSfx.Build(arena.transform,
                    arena.GetComponentsInChildren<CageStation>(true),
                    arena.GetComponentInChildren<PitBear>(true));
            }

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (arena != null)
            {
                CircusNightProps.Apply(arena.transform);
                CircusCraftBuilder.ApplyWindowsAndHud(arena.transform);
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log($"Реквизит «Секундомера» расставлен: кнопок {placed} из {stations.Length} клеток, граней табло {faces}.");
        }


        /// <summary>Оставляет личную подсказку; результаты читаются на четырёх гранях над ареной.</summary>
        private static int BuildLocalFeedback()
        {
            GameObject canvas = GameObject.Find("_UI/Canvas");
            if (canvas == null)
            {
                Debug.LogWarning("StopwatchPropBuilder: не найден _UI/Canvas — личная подсказка не построена.");
                return 0;
            }

            Transform existing = canvas.transform.Find(HudObjectName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            BuildStatusLine(canvas.transform, FindFont());
            return RegisterFaces();
        }

        /// <summary>
        /// Крупная строка «отсчёт идёт» внизу экрана. Единственный элемент
        /// интерфейса, у каждого игрока свой: остальное — общее табло.
        /// </summary>
        private static void BuildStatusLine(Transform canvas, TMP_FontAsset font)
        {
            Transform existing = canvas.Find(StatusObjectName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = new GameObject(StatusObjectName, typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
            rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 132f);
            rect.sizeDelta = new Vector2(900f, 60f);

            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }

            text.fontSizeMin = 26f;
            text.fontSizeMax = 44f;
            text.enableAutoSizing = true;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = string.Empty;

            var hud = go.AddComponent<StopwatchLocalHud>();
            var so = new SerializedObject(hud);
            so.FindProperty("label").objectReferenceValue = text;
            so.FindProperty("game").objectReferenceValue = Object.FindAnyObjectByType<StopwatchMinigame>(FindObjectsInactive.Include);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Перепривязать четыре грани общего табло после пересборки.</summary>
        private static int RegisterFaces()
        {
            GameObject boardGo = GameObject.Find("_Arena/Scoreboard");
            if (boardGo == null)
            {
                Debug.LogWarning("StopwatchPropBuilder: не найдено _Arena/Scoreboard — пересобери арену.");
                return 0;
            }

            var board = boardGo.GetComponent<WorldScoreboard>();
            var faces = new List<WorldScoreboardFace>(4);
            faces.AddRange(boardGo.GetComponentsInChildren<WorldScoreboardFace>(true));
            board.SetFaces(faces);
            board.Clear();

            // Вернуть табло контроллеру после пересборки арены.
            CircusUiWiring.ApplyStopwatch();
            return faces.Count;
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

                CircusCraftBuilder.Add(root.transform, "CN_ButtonPedestal", Vector3.zero);

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
