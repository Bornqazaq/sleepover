using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Igruha.Core.Minigame;
using Igruha.Core.UI;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Снимки экранов интерфейса без запуска игры.
    ///
    /// Нужны затем, что иначе оценить вид можно только войдя в игру, дождавшись
    /// раунда и успев нажать снимок за те шесть секунд, что висит заставка
    /// правил. Здесь экран наполняется учебными данными, канвас на время
    /// переводится в режим «из камеры» и рисуется в текстуру.
    ///
    /// Прозрачность панелей выставляется вручную: появление
    /// (<see cref="UiPop"/>) идёт в <c>Update</c>, а в редакторе вне игры
    /// его никто не крутит — панель осталась бы на нулевой прозрачности,
    /// то есть на снимке был бы пустой экран.
    /// </summary>
    public static class UiPreview
    {
        private const string OutputFolder = "Assets/Screenshots";
        private const int Width = 1920;
        private const int Height = 1080;

        /// <summary>Учебные имена для колеса эмоций: реальные приходят от персонажа.</summary>
        private static readonly string[] DemoEmotes =
        {
            "Тверк", "Робот", "Чечётка", "Вжух", "Лунная", "Поклон", "Флекс", "Шаффл"
        };

        private static readonly string[] DemoPlayers =
        {
            "Аза", "Босс", "Толстый", "Гёрл", "Майлз", "МайБой", "Шланга", "Карлан"
        };

        [MenuItem("Igruha/Интерфейс/Снимки экранов этой сцены")]
        public static void CaptureCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            Canvas canvas = FindCanvas();
            if (canvas == null)
            {
                Debug.LogWarning("⚠️ В сцене нет канваса раунда — снимать нечего");
                return;
            }

            Directory.CreateDirectory(OutputFolder);

            Capture(canvas, scene.name, "hud", ShowHud);
            Capture(canvas, scene.name, "tutorial", ShowTutorial);
            Capture(canvas, scene.name, "results", ShowResults);
            Capture(canvas, scene.name, "wheel", ShowWheel);

            AssetDatabase.Refresh();
            Debug.Log($"📸 Снимки интерфейса «{scene.name}» лежат в {OutputFolder}");
        }

        private static Canvas FindCanvas()
        {
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].renderMode != RenderMode.WorldSpace &&
                    canvases[i].GetComponentInChildren<RoundHud>(true) != null)
                {
                    return canvases[i];
                }
            }

            return null;
        }

        private static void Capture(Canvas canvas, string sceneName, string state, System.Func<Canvas, bool> setup)
        {
            HideAll(canvas);
            if (!setup(canvas))
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            Render(canvas, $"{OutputFolder}/UI_{sceneName}_{state}.png");
            HideAll(canvas);
        }

        private static void HideAll(Canvas canvas)
        {
            Hide(canvas, "_HudOverlay/TutorialPanel");
            Hide(canvas, "_HudOverlay/ResultsPanel");
            Hide(canvas, "_HudOverlay/CountdownLine");
            Hide(canvas, "_HudPlates/StatusPlate");
            Hide(canvas, "_HudPlates/SpectatorPlate");

            var wheel = canvas.GetComponentInChildren<EmoteWheel>(true);
            if (wheel != null)
            {
                Transform panel = wheel.transform.Find("Panel");
                if (panel != null)
                {
                    panel.gameObject.SetActive(false);
                }
            }
        }

        private static void Hide(Canvas canvas, string path)
        {
            Transform target = canvas.transform.Find(path);
            if (target != null)
            {
                target.gameObject.SetActive(false);
            }
        }

        /// <summary>Плашки раунда: таймер, строка роли, отсчёт. Учебные значения.</summary>
        private static bool ShowHud(Canvas canvas)
        {
            Transform plates = canvas.transform.Find("_HudPlates");
            if (plates == null)
            {
                return false;
            }

            Set(plates, "TimerPlate/TimerText", "1:24");
            Show(plates, "StatusPlate", "Ты Ведущий: загадай вопрос и отметь верный ответ");
            Show(plates, "SpectatorPlate", "Смотрим за: Карлан");

            Transform countdown = canvas.transform.Find("_HudOverlay/CountdownLine");
            if (countdown != null)
            {
                countdown.gameObject.SetActive(true);
                var text = countdown.GetComponent<TMP_Text>();
                if (text != null)
                {
                    text.text = "3";
                }

                Wake(countdown);
            }

            return true;
        }

        private static void Show(Transform root, string plate, string value)
        {
            Transform target = root.Find(plate);
            if (target == null)
            {
                return;
            }

            target.gameObject.SetActive(true);
            var text = target.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = value;
            }
        }

        private static void Set(Transform root, string path, string value)
        {
            Transform target = root.Find(path);
            var text = target != null ? target.GetComponent<TMP_Text>() : null;
            if (text != null)
            {
                text.text = value;
            }
        }

        private static bool ShowTutorial(Canvas canvas)
        {
            var tutorial = canvas.GetComponentInChildren<TutorialScreen>(true);
            MinigameDefinition definition = FindDefinition(canvas.gameObject.scene.name);
            if (tutorial == null || definition == null)
            {
                return false;
            }

            tutorial.Show(definition, null);
            Wake(canvas.transform.Find("_HudOverlay/TutorialPanel/Card"));
            return true;
        }

        private static bool ShowResults(Canvas canvas)
        {
            var hud = canvas.GetComponentInChildren<RoundHud>(true);
            if (hud == null)
            {
                return false;
            }

            var rows = canvas.GetComponentsInChildren<ResultRow>(true);
            for (int i = 0; i < rows.Length; i++)
            {
                if (i < DemoPlayers.Length)
                {
                    rows[i].Set(i + 1, DemoPlayers[i]);
                }
                else
                {
                    rows[i].Clear();
                }
            }

            Transform panel = canvas.transform.Find("_HudOverlay/ResultsPanel");
            if (panel == null)
            {
                return false;
            }

            panel.gameObject.SetActive(true);
            Wake(panel.Find("Card"));
            return true;
        }

        private static bool ShowWheel(Canvas canvas)
        {
            var wheel = canvas.GetComponentInChildren<EmoteWheel>(true);
            Transform panel = wheel != null ? wheel.transform.Find("Panel") : null;
            if (panel == null)
            {
                return false;
            }

            var slots = panel.GetComponentsInChildren<EmoteWheelSlot>(true);
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].Bind(i < DemoEmotes.Length ? DemoEmotes[i] : null);
                slots[i].SetHovered(i == 2);
            }

            panel.gameObject.SetActive(true);
            Wake(panel);
            return true;
        }

        /// <summary>Довести панель до конца появления: в редакторе его никто не проигрывает.</summary>
        private static void Wake(Transform target)
        {
            if (target == null)
            {
                return;
            }

            var group = target.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
            }

            target.localScale = Vector3.one;

            var rect = target as RectTransform;
            if (rect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }
        }

        private static MinigameDefinition FindDefinition(string sceneName)
        {
            string[] guids = AssetDatabase.FindAssets("t:MinigameDefinition");
            MinigameDefinition fallback = null;

            for (int i = 0; i < guids.Length; i++)
            {
                var definition = AssetDatabase.LoadAssetAtPath<MinigameDefinition>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (definition == null)
                {
                    continue;
                }

                if (definition.SceneName == sceneName)
                {
                    return definition;
                }

                fallback ??= definition;
            }

            return fallback;
        }

        /// <summary>
        /// Нарисовать канвас в файл. Экранный канвас камерой не снимается,
        /// поэтому на время съёмки он переводится в режим «из камеры»
        /// и возвращается обратно.
        /// </summary>
        private static void Render(Canvas canvas, string path)
        {
            var cameraGo = new GameObject("__UiPreviewCamera", typeof(Camera));
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.28f, 0.31f, 0.35f);

            // Далеко от арены: иначе в кадр попадёт геометрия сцены.
            cameraGo.transform.position = new Vector3(9000f, 9000f, 9000f);

            RenderMode mode = canvas.renderMode;
            Camera world = canvas.worldCamera;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 20f;
            Canvas.ForceUpdateCanvases();

            var texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = texture;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
            shot.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(path, shot.EncodeToPNG());

            camera.targetTexture = null;
            canvas.renderMode = mode;
            canvas.worldCamera = world;

            Object.DestroyImmediate(shot);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(cameraGo);
        }
    }
}
