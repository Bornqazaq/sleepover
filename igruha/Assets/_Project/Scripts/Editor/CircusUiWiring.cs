using TMPro;
using UnityEditor;
using UnityEngine;
using Igruha.Core.UI;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.Stopwatch;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Возврат ссылок интерфейсу обеих цирковых игр после пересборки.
    ///
    /// 🔴 <b>Зачем понадобилось.</b> <see cref="CircusWiring"/> уже возвращает
    /// контроллеру клетки и медведя — то, что пересоздаёт билдер арены. Ровно
    /// та же дыра оказалась в интерфейсе, и её никто не закрыл: арт-фаза
    /// (дресс шатра, коммит 58bbf81) пересобрала сцены, и вместе с ней тихо
    /// обнулилось
    ///
    /// — <c>StopwatchScoreboard.board</c> у «Секундомера»: одна пустая ссылка
    ///   гасила разом и четыре грани над ямой, и экранное табло в углу, потому
    ///   что экранная панель — это пятая грань того же <see cref="WorldScoreboard"/>;
    /// — <c>CansOrderMinigame.localHud</c> и четыре текстовых поля
    ///   <see cref="CansOrderLocalHud"/> у «Порядка банок»: подсказка про E,
    ///   строка управления, своя карточка результата и прошлый круг.
    ///
    /// Ошибка тихая по построению: и <c>WorldScoreboard</c>, и
    /// <c>CansOrderLocalHud</c> написаны так, что при пустой ссылке молча
    /// выходят — в консоли ни строчки, а в игре просто ничего не появляется.
    /// Игрок при этом не видит ни задания, ни результата: обе игры про то,
    /// чтобы читать табло, и без него они не игры вовсе.
    ///
    /// <b>Почему поля восстанавливаются кодом.</b> Причина та же, что у
    /// <see cref="CircusWiring"/> и <see cref="MinigameHudLines"/>: инспектор —
    /// не то место, где живёт правда. Всё, что не восстанавливается
    /// пересборкой, теряется при следующей же пересборке, и теряется молча.
    ///
    /// Поля <see cref="CansOrderLocalHud"/> собраны руками в фазе каркаса,
    /// поэтому здесь действует правило «есть — беру, нет — создаю»: готовые
    /// объекты не трогаем (у них выверенная геометрия), пропавшие поднимаем
    /// заново с теми же числами.
    /// </summary>
    internal static class CircusUiWiring
    {
        private const string CanvasPath = "_UI/Canvas";
        private const string ScoreboardPath = "_Arena/Scoreboard";

        // Экранный интерфейс «Секундомера». В сцене «Порядка банок» это чужие
        // объекты, и они не безобидны: панель 600×448 в левом верхнем углу
        // непрозрачна и ложится ровно поверх строки прошлого круга.
        private const string StopwatchHudName = "StopwatchHud";
        private const string StopwatchStatusName = "StopwatchStatus";

        // Имена и геометрия подсказок «Порядка банок». Числа — из сцены фазы
        // каркаса, выставлены под опорное разрешение канваса 1920×1080.
        private const string PromptName = "ShelfPromptLabel";
        private const string ControlsName = "ShelfControlsLabel";
        private const string RevealName = "RevealCardLabel";
        private const string LastCircleName = "LastCircleLabel";

        /// <summary>
        /// Слой модальных экранов раунда — правила, итоги, отсчёт. Собирает его
        /// <c>HudSkin</c>, и лежат они внутри него, а не поодиночке на канвасе:
        /// раньше здесь поднимались три панели по именам.
        /// </summary>
        private const string ModalRoot = "_HudOverlay";

        [MenuItem("Igruha/Цирк/Перевязать интерфейс")]
        private static void RewireMenu()
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            int fixedUp = active.name == "Stopwatch" ? ApplyStopwatch() : ApplyCansOrder();

            if (fixedUp > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(active);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(active);
            }

            Debug.Log($"Интерфейс сцены «{active.name}» перевязан: восстановлено ссылок — {fixedUp}.");
        }

        /// <summary>
        /// «Секундомер»: вернуть табло контроллеру. Возвращает число
        /// восстановленных ссылок — ноль значит «всё и так было на месте».
        /// </summary>
        internal static int ApplyStopwatch()
        {
            var scoreboard = Object.FindFirstObjectByType<StopwatchScoreboard>(FindObjectsInactive.Include);
            if (scoreboard == null)
            {
                Debug.LogWarning("CircusUiWiring: в сцене нет StopwatchScoreboard — табло вешать не на что.");
                return 0;
            }

            WorldScoreboard board = FindBoard();
            int fixedUp = board == null ? 0 : SetReference(scoreboard, "board", board);
            fixedUp += FitScreenHud();
            fixedUp += LiftModalPanels();
            return fixedUp;
        }

        /// <summary>
        /// «Порядок банок»: вернуть контроллеру экранную панель, панели —
        /// её четыре текстовых поля, а табло — само табло и кассету
        /// расстановок. Чужой экранный интерфейс «Секундомера» из сцены
        /// убирается: он перекрывает строку прошлого круга.
        /// </summary>
        internal static int ApplyCansOrder()
        {
            int fixedUp = 0;

            var hud = Object.FindFirstObjectByType<CansOrderLocalHud>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning("CircusUiWiring: в сцене нет CansOrderLocalHud — подсказки вешать не на что.");
            }
            else
            {
                fixedUp += WireHudLabels(hud);

                var game = Object.FindFirstObjectByType<CansOrderMinigame>(FindObjectsInactive.Include);
                if (game != null)
                {
                    // Без этой ссылки панель остаётся без правил игры и без
                    // своей полки: BindRules и BindLocalShelf просто некому
                    // позвать, и карточка результата с прошлым кругом молчат,
                    // даже когда сами поля расставлены.
                    fixedUp += SetReference(game, "localHud", hud);
                }
            }

            var boardScript = Object.FindFirstObjectByType<CanOrderBoard>(FindObjectsInactive.Include);
            if (boardScript != null)
            {
                WorldScoreboard board = FindBoard();
                if (board != null)
                {
                    fixedUp += SetReference(boardScript, "board", board);
                }

                var panel = Object.FindFirstObjectByType<CanOrderArrangementPanel>(FindObjectsInactive.Include);
                if (panel != null)
                {
                    fixedUp += SetReference(boardScript, "panel", panel);
                }
            }

            fixedUp += DropForeign(StopwatchHudName);
            fixedUp += DropForeign(StopwatchStatusName);
            fixedUp += LiftModalPanels();
            return fixedUp;
        }

        /// <summary>
        /// Четыре подсказки экрана. Существующие переиспользуются как есть:
        /// их геометрию выверяли на плейтесте, и пересоздание её потеряло бы.
        /// </summary>
        private static int WireHudLabels(CansOrderLocalHud hud)
        {
            Transform canvas = FindCanvas();
            if (canvas == null)
            {
                return 0;
            }

            TMP_Text prompt = EnsureLabel(canvas, PromptName,
                new Vector2(0.5f, 0f), new Vector2(0f, 240f), new Vector2(1100f, 60f),
                40f, TextAlignmentOptions.Center, new Color(1f, 0.92f, 0.35f), true);

            TMP_Text controls = EnsureLabel(canvas, ControlsName,
                new Vector2(0.5f, 0f), new Vector2(0f, 188f), new Vector2(1600f, 44f),
                26f, TextAlignmentOptions.Center, new Color(0.82f, 0.85f, 0.90f), true);

            TMP_Text reveal = EnsureLabel(canvas, RevealName,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(900f, 260f),
                40f, TextAlignmentOptions.Center, Color.white, true);

            TMP_Text lastCircle = EnsureLabel(canvas, LastCircleName,
                new Vector2(0f, 1f), new Vector2(48f, -40f), new Vector2(560f, 150f),
                28f, TextAlignmentOptions.TopLeft, new Color(0.86f, 0.88f, 0.92f), false);

            int fixedUp = SetReference(hud, "promptLabel", prompt);
            fixedUp += SetReference(hud, "controlsLabel", controls);
            fixedUp += SetReference(hud, "revealLabel", reveal);
            fixedUp += SetReference(hud, "lastCircleLabel", lastCircle);
            return fixedUp;
        }

        /// <summary>
        /// Текстовое поле на канвасе: нашлось по имени — берём его, не нашлось —
        /// поднимаем с указанной геометрией.
        /// </summary>
        private static TMP_Text EnsureLabel(Transform canvas, string objectName, Vector2 anchor,
            Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment,
            Color color, bool wrap)
        {
            Transform existing = canvas.Find(objectName);
            if (existing != null)
            {
                var found = existing.GetComponent<TMP_Text>();
                if (found != null)
                {
                    return found;
                }

                Object.DestroyImmediate(existing.gameObject);
            }

            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }

            text.fontSize = fontSize;
            text.enableAutoSizing = false;
            text.alignment = alignment;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.color = color;
            // Подсказки не должны перехватывать клики: в окне выставления ЛКМ
            // работает по полке, и прозрачное поле во весь экран съедало бы
            // нажатия.
            text.raycastTarget = false;
            text.text = string.Empty;
            Debug.Log($"CircusUiWiring: поле «{objectName}» создано заново — в сцене его не было.");
            return text;
        }

        /// <summary>
        /// Отдать экранной грани её же подложку, чтобы та подрезалась по числу
        /// строк. Панель построена на восемь, а лобби бывает и на двоих —
        /// пустая нижняя половина занимает четверть экрана.
        /// </summary>
        private static int FitScreenHud()
        {
            Transform canvas = FindCanvas();
            Transform hud = canvas != null ? canvas.Find(StopwatchHudName) : null;
            var face = hud != null ? hud.GetComponent<WorldScoreboardFace>() : null;
            return face == null ? 0 : SetReference(face, "autoHeightPanel", (RectTransform)hud);
        }

        /// <summary>
        /// Поднять модальные экраны над игровым интерфейсом.
        ///
        /// 🔴 uGUI рисует детей канваса в порядке иерархии, и обучалка стояла
        /// вторым ребёнком, а экранное табло — шестым. То есть панель табло
        /// ложилась поверх обучалки: пока идёт заставка, табло ещё пустое, и
        /// её левая треть просто гасла тёмным прямоугольником — вместе с
        /// текстом описания под ним. То же самое ждало экран результатов.
        ///
        /// Порядок между самими экранами сохраняется: они не пересекаются,
        /// и переставлять их друг относительно друга незачем.
        /// </summary>
        private static int LiftModalPanels()
        {
            Transform canvas = FindCanvas();
            if (canvas == null)
            {
                return 0;
            }

            Transform modals = canvas.Find(ModalRoot);
            if (modals == null || modals.GetSiblingIndex() == canvas.childCount - 1)
            {
                return 0;
            }

            // Подсказки «Порядка банок» создаются этим же прогоном и встают
            // последними — то есть поверх правил и итогов. Слой модальных
            // экранов поднимается обратно.
            modals.SetAsLastSibling();
            Debug.Log("CircusUiWiring: слой модальных экранов поднят над игровым интерфейсом.");
            return 1;
        }

        /// <summary>
        /// Убрать с канваса объект чужой игры. Возвращает 1, если что-то
        /// действительно удалено.
        /// </summary>
        private static int DropForeign(string canvasChild)
        {
            Transform canvas = FindCanvas();
            Transform found = canvas != null ? canvas.Find(canvasChild) : null;
            if (found == null)
            {
                return 0;
            }

            Object.DestroyImmediate(found.gameObject);
            Debug.Log($"CircusUiWiring: с канваса убран «{canvasChild}» — экранный интерфейс чужой игры.");
            return 1;
        }

        private static WorldScoreboard FindBoard()
        {
            GameObject boardGo = GameObject.Find(ScoreboardPath);
            if (boardGo == null)
            {
                Debug.LogWarning($"CircusUiWiring: не найдено {ScoreboardPath} — пересобери арену.");
                return null;
            }

            var board = boardGo.GetComponent<WorldScoreboard>();
            if (board == null)
            {
                Debug.LogWarning($"CircusUiWiring: на {ScoreboardPath} нет WorldScoreboard.");
            }

            return board;
        }

        private static Transform FindCanvas()
        {
            GameObject canvas = GameObject.Find(CanvasPath);
            if (canvas == null)
            {
                Debug.LogWarning($"CircusUiWiring: не найден {CanvasPath} — экранный интерфейс вешать некуда.");
                return null;
            }

            return canvas.transform;
        }

        /// <summary>
        /// Присвоить ссылку через <see cref="SerializedObject"/>. Возвращает 1,
        /// только если ссылка действительно поменялась: счётчик в логе должен
        /// означать «столько было сломано», а не «столько полей потрогали».
        /// </summary>
        private static int SetReference(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty reference = serialized.FindProperty(property);
            if (reference == null)
            {
                Debug.LogWarning($"CircusUiWiring: у {target.GetType().Name} нет поля «{property}».", target);
                return 0;
            }

            if (reference.objectReferenceValue == value)
            {
                return 0;
            }

            reference.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"CircusUiWiring: {target.GetType().Name}.{property} ← «{(value == null ? "пусто" : value.name)}».", target);
            return 1;
        }
    }
}
