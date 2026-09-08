using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Igruha.Core.UI;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Сборщик интерфейса раунда: карточка правил, плашки HUD, итоги, колесо
    /// эмоций. Один прогон переодевает все сцены сразу.
    ///
    /// <b>Зачем кодом.</b> Канвас лежит в шаблоне сцены и скопирован в девять
    /// мини-игр. Правка «в инспекторе» чинит одну копию из девяти, а YAML сцены
    /// не переживает слияние веток — та же причина, по которой кодом заданы
    /// формат камеры (<c>igruha/CLAUDE.md</c>, 2a) и перенос строк на экране
    /// правил.
    ///
    /// <b>Что сборщик трогает и чего не трогает.</b> Он владеет только своими
    /// объектами (список <see cref="OwnedNames"/>) и панелью колеса эмоций.
    /// Панели конкретных игр — ввод вопроса «Экзамена», выбор позы «Дырки
    /// в стене», полоса команд — лежат на том же канвасе и остаются нетронутыми.
    /// Компоненты <c>TutorialScreen</c>, <c>RoundHud</c> и <c>EmoteWheel</c>
    /// не пересоздаются, а перецепляются: у колеса в поле ригa камеры стоит
    /// ссылка из сцены, и пересоздание молча потеряло бы её.
    /// </summary>
    public static class HudSkin
    {
        private const string OverlayRoot = "_HudOverlay";
        private const string PlatesRoot = "_HudPlates";

        /// <summary>Объекты старого канваса, которые сборщик заменяет своими.</summary>
        private static readonly string[] OwnedNames =
        {
            "TimerText", "TutorialPanel", "ResultsPanel", "SpectatorPanel",
            "StatusLine", "CountdownLine", OverlayRoot, PlatesRoot
        };

        /// <summary>Опорное разрешение канваса: под него посчитаны все размеры ниже.</summary>
        private static readonly Vector2 Reference = new Vector2(1920f, 1080f);

        private const float CardWidth = 1180f;
        private const int HintRowCount = 9;

        /// <summary>
        /// Заготовочная высота коробки описания. Настоящую ставит
        /// <c>TutorialScreen</c> по замеру текста: карточка растёт под
        /// содержимое и упирается в потолок высоты экрана, а описание
        /// ужимается только тогда, когда потолок достигнут.
        /// </summary>
        private const float ObjectiveStartHeight = 200f;
        private const int ResultRowCount = 8;
        private const float WheelRadius = 280f;
        private const float WheelPointerRadius = 238f;

        [MenuItem("Igruha/Интерфейс/Переодеть эту сцену")]
        public static void BuildCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!Build(scene))
            {
                Debug.LogWarning("⚠️ В сцене нет канваса раунда — переодевать нечего");
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        [MenuItem("Igruha/Интерфейс/Переодеть все сцены")]
        public static void BuildAllScenes()
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project/Scenes" });
            int done = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (!Build(scene))
                {
                    continue;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                done++;
            }

            Debug.Log($"🎛 Интерфейс переодет в {done} сценах");
        }

        /// <summary>Переодеть канвас сцены. false — канваса раунда в сцене нет.</summary>
        public static bool Build(Scene scene)
        {
            Canvas canvas = FindCanvas(scene);
            if (canvas == null)
            {
                return false;
            }

            TMP_FontAsset font = FindFont(canvas);
            Scale(canvas);
            Clear(canvas);

            var tutorial = canvas.GetComponentInChildren<TutorialScreen>(true);
            var hud = canvas.GetComponentInChildren<RoundHud>(true);
            var wheel = canvas.GetComponentInChildren<EmoteWheel>(true);

            // Контейнеры заводятся только под то, что в сцене есть: в хабе
            // нет ни таймера, ни правил — там переодевается одно колесо,
            // и пустые контейнеры на его канвасе были бы мусором.
            if (hud != null)
            {
                BuildHud(hud, Root(canvas.transform, PlatesRoot, false),
                    Root(canvas.transform, OverlayRoot, true), font);
            }

            if (tutorial != null)
            {
                BuildTutorial(tutorial, Root(canvas.transform, OverlayRoot, true), font);
            }

            if (wheel != null)
            {
                BuildWheel(wheel, font);
            }

            return true;
        }

        private static Canvas FindCanvas(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var canvases = root.GetComponentsInChildren<Canvas>(true);
                for (int i = 0; i < canvases.Length; i++)
                {
                    // Канвас раунда — экранный. Мировые холсты (табло, буквы
                    // платформ, пузыри реплик) висят на арене и переодеванию
                    // не подлежат.
                    if (canvases[i].renderMode != RenderMode.WorldSpace &&
                        canvases[i].GetComponentInChildren<RoundHud>(true) != null)
                    {
                        return canvases[i];
                    }

                    if (canvases[i].renderMode != RenderMode.WorldSpace &&
                        canvases[i].GetComponentInChildren<EmoteWheel>(true) != null)
                    {
                        return canvases[i];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Шрифт берётся из самой сцены, а не назначается сборщиком: в проекте
        /// один шрифтовый ассет на весь интерфейс, и подставлять его по имени
        /// значило бы завести второй источник правды.
        /// </summary>
        private static TMP_FontAsset FindFont(Canvas canvas)
        {
            var texts = canvas.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].font != null)
                {
                    return texts[i].font;
                }
            }

            return TMP_Settings.defaultFontAsset;
        }

        private static void Scale(Canvas canvas)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Reference;

            // Половина между шириной и высотой: на 21:9 карточка не расползается
            // по ширине, на 4:3 не срезается по высоте.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private static void Clear(Canvas canvas)
        {
            for (int i = 0; i < OwnedNames.Length; i++)
            {
                Transform old = canvas.transform.Find(OwnedNames[i]);
                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                }
            }
        }

        private static Transform Root(Transform parent, string name, bool onTop)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : Node(name, parent);
            Stretch((RectTransform)go.transform);

            if (onTop)
            {
                go.transform.SetAsLastSibling();
            }
            else
            {
                go.transform.SetAsFirstSibling();
            }

            return go.transform;
        }

        // ========== HUD: таймер, статус, отсчёт, наблюдатель, итоги ==========

        private static void BuildHud(RoundHud hud, Transform plates, Transform overlay, TMP_FontAsset font)
        {
            GameObject timerPlate = Plate("TimerPlate", plates, new Vector2(0.5f, 1f), new Vector2(0f, -26f),
                new Vector2(280f, 92f));
            TMP_Text timer = Label(Node("TimerText", timerPlate.transform), font, 52f, UiSkin.TextPrimary,
                TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(timer.rectTransform, new Vector4(24f, 6f, 24f, 6f));

            GameObject statusPlate = Plate("StatusPlate", plates, new Vector2(0.5f, 1f), new Vector2(0f, -134f),
                new Vector2(920f, 62f));
            TMP_Text status = Label(Node("StatusLine", statusPlate.transform), font, 28f, UiSkin.TextPrimary,
                TextAlignmentOptions.Center, FontStyles.Normal);
            Stretch(status.rectTransform, new Vector4(28f, 4f, 28f, 4f));

            GameObject spectatorPlate = Plate("SpectatorPlate", plates, new Vector2(0.5f, 0f), new Vector2(0f, 54f),
                new Vector2(620f, 66f));
            TMP_Text spectator = Label(Node("SpectatorText", spectatorPlate.transform), font, 26f,
                UiSkin.TextSecondary, TextAlignmentOptions.Center, FontStyles.Normal);
            Stretch(spectator.rectTransform, new Vector4(24f, 4f, 24f, 4f));

            GameObject countdown = Node("CountdownLine", overlay);
            var countdownRect = (RectTransform)countdown.transform;
            Anchor(countdownRect, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(640f, 320f));
            TMP_Text countdownText = Label(countdown, font, 240f, UiSkin.TextPrimary,
                TextAlignmentOptions.Center, FontStyles.Bold);
            countdownText.outlineWidth = 0.22f;
            countdownText.outlineColor = new Color32(10, 12, 18, 220);
            countdown.AddComponent<CanvasGroup>();
            var countdownPop = countdown.AddComponent<UiPop>();
            Fields(countdownPop, ("duration", 0.18f), ("fromScale", 1.45f), ("playOnEnable", false));

            ResultRow[] rows = BuildResults(hud, overlay, font, out GameObject resultsPanel, out Button restart);

            var so = new SerializedObject(hud);
            so.FindProperty("timerText").objectReferenceValue = timer;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("countdownText").objectReferenceValue = countdownText;
            so.FindProperty("spectatorPanel").objectReferenceValue = spectatorPlate;
            so.FindProperty("spectatorText").objectReferenceValue = spectator;
            so.FindProperty("resultsPanel").objectReferenceValue = resultsPanel;
            so.FindProperty("resultsText").objectReferenceValue = null;
            so.FindProperty("restartButton").objectReferenceValue = restart;
            so.FindProperty("countdownPop").objectReferenceValue = countdownPop;
            Array(so.FindProperty("resultRows"), rows);
            so.ApplyModifiedPropertiesWithoutUndo();

            statusPlate.SetActive(false);
            spectatorPlate.SetActive(false);
            countdown.SetActive(false);
        }

        private static ResultRow[] BuildResults(RoundHud hud, Transform overlay, TMP_FontAsset font,
            out GameObject panel, out Button restart)
        {
            panel = Node("ResultsPanel", overlay);
            Stretch((RectTransform)panel.transform);
            Scrim(panel.transform);

            GameObject card = Card("Card", panel.transform, 760f, out VerticalLayoutGroup layout);
            layout.spacing = 10f;

            TMP_Text title = Label(Node("Title", card.transform), font, 54f, UiSkin.TextPrimary,
                TextAlignmentOptions.Center, FontStyles.Bold);
            title.text = "Итоги раунда";
            Line(title.gameObject, 74f);

            Divider(card.transform);

            var rows = new ResultRow[ResultRowCount];
            for (int i = 0; i < ResultRowCount; i++)
            {
                rows[i] = ResultLine(card.transform, font, i);
            }

            GameObject button = Node("RestartButton", card.transform);
            Line(button, 76f);
            Sprite(button, UiSpriteBaker.Chip, UiSkin.Accent);
            restart = button.AddComponent<Button>();
            TMP_Text buttonLabel = Label(Node("Label", button.transform), font, 30f, UiSkin.AccentInk,
                TextAlignmentOptions.Center, FontStyles.Bold);
            buttonLabel.text = "Ещё раз";
            Stretch(buttonLabel.rectTransform, new Vector4(24f, 6f, 24f, 6f));

            panel.SetActive(false);
            return rows;
        }

        private static ResultRow ResultLine(Transform parent, TMP_FontAsset font, int index)
        {
            GameObject row = Node($"Result_{index + 1}", parent);
            Line(row, 62f);
            var group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 18f;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childForceExpandWidth = false;

            // Кружок места обязан остаться кружком: растягивание по высоте
            // строки превращает его в яйцо.
            group.childForceExpandHeight = false;
            group.childControlWidth = true;
            group.childControlHeight = true;

            GameObject badge = Node("Badge", row.transform);
            Line(badge, 54f, 54f);
            Sprite(badge, UiSpriteBaker.Circle, UiSkin.Plate, Image.Type.Simple);
            TMP_Text place = Label(Node("Place", badge.transform), font, 28f, UiSkin.TextPrimary,
                TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(place.rectTransform);

            TMP_Text name = Label(Node("Name", row.transform), font, 30f, UiSkin.TextPrimary,
                TextAlignmentOptions.Left, FontStyles.Normal);
            Flexible(name.gameObject);

            var component = row.AddComponent<ResultRow>();
            Fields(component, ("badge", badge.GetComponent<Image>()), ("placeText", place), ("nameText", name));
            row.SetActive(false);
            return component;
        }

        // ========== Экран правил ==========

        private static void BuildTutorial(TutorialScreen tutorial, Transform overlay, TMP_FontAsset font)
        {
            GameObject panel = Node("TutorialPanel", overlay);
            Stretch((RectTransform)panel.transform);
            Scrim(panel.transform);

            GameObject card = Card("Card", panel.transform, CardWidth, out VerticalLayoutGroup layout);
            layout.spacing = 12f;

            // Шапка одной строкой: акцентная планка, название игры, плашка
            // категории справа. Раньше это были три строки одна под другой,
            // и они съедали 78 пикселей высоты — ровно ту полосу, которой
            // не хватало описанию «Порядка банок».
            GameObject header = Row("Header", card.transform, 78f);
            var headerGroup = header.GetComponent<HorizontalLayoutGroup>();
            headerGroup.spacing = 22f;

            GameObject accent = Node("AccentBar", header.transform);
            Line(accent, 62f, 6f);
            Sprite(accent, UiSpriteBaker.Card, UiSkin.Accent, Image.Type.Sliced, 8f);

            TMP_Text title = Label(Node("Title", header.transform), font, 60f, UiSkin.TextPrimary,
                TextAlignmentOptions.Left, FontStyles.Bold);
            Line(title.gameObject, 74f);
            Flexible(title.gameObject);

            GameObject chip = Node("CategoryChip", header.transform);
            Line(chip, 46f);
            var chipFit = chip.AddComponent<ContentSizeFitter>();
            chipFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var chipGroup = chip.AddComponent<HorizontalLayoutGroup>();
            chipGroup.padding = new RectOffset(22, 22, 0, 0);
            chipGroup.childAlignment = TextAnchor.MiddleCenter;
            chipGroup.childControlWidth = true;
            chipGroup.childControlHeight = true;
            chipGroup.childForceExpandWidth = false;
            Sprite(chip, UiSpriteBaker.Chip, UiSkin.Accent);
            TMP_Text category = Label(Node("Text", chip.transform), font, 22f, UiSkin.AccentInk,
                TextAlignmentOptions.Center, FontStyles.Bold);
            category.characterSpacing = 6f;

            TMP_Text objective = Label(Node("Objective", card.transform), font, 34f, UiSkin.TextSecondary,
                TextAlignmentOptions.TopLeft, FontStyles.Normal);
            // Описание забирает всё свободное место карточки и ужимается
            // кеглем, когда строк управления много. Кегль подбирает
            // TutorialScreen замером, а не автокеглем TMP: автокегль считает
            // себя до того, как компоновка выдаст полю его настоящую высоту,
            // и семистрочное описание «Порядка банок» ложилось поверх списка
            // клавиш.
            var objectiveElement = objective.gameObject.AddComponent<LayoutElement>();
            objectiveElement.minHeight = 90f;
            objectiveElement.preferredHeight = ObjectiveStartHeight;
            objectiveElement.flexibleHeight = 0f;

            Divider(card.transform);

            TMP_Text caption = Label(Node("Caption", card.transform), font, 22f, UiSkin.TextMuted,
                TextAlignmentOptions.Left, FontStyles.Bold);
            caption.text = "УПРАВЛЕНИЕ";
            caption.characterSpacing = 8f;
            Line(caption.gameObject, 30f);

            var rows = new TutorialHintRow[HintRowCount];
            for (int i = 0; i < HintRowCount; i++)
            {
                rows[i] = HintLine(card.transform, font, i);
            }

            Image timerFill = Footer(card.transform, font);

            var group = card.AddComponent<CanvasGroup>();
            var pop = card.AddComponent<UiPop>();
            Fields(pop, ("duration", 0.24f), ("fromScale", 0.94f), ("playOnEnable", false));

            var so = new SerializedObject(tutorial);
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("objectiveText").objectReferenceValue = objective;
            so.FindProperty("categoryText").objectReferenceValue = category;
            so.FindProperty("timerFill").objectReferenceValue = timerFill;
            so.FindProperty("cardPop").objectReferenceValue = pop;
            so.FindProperty("card").objectReferenceValue = card.transform as RectTransform;
            Array(so.FindProperty("hintRows"), rows);
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.SetActive(false);
            _ = group;
        }

        private static TutorialHintRow HintLine(Transform parent, TMP_FontAsset font, int index)
        {
            GameObject row = Node($"Hint_{index + 1}", parent);
            Line(row, 42f);
            var group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 18f;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childControlWidth = true;
            group.childControlHeight = true;

            GameObject cap = Node("KeyCap", row.transform);
            Line(cap, 40f);
            var capElement = cap.GetComponent<LayoutElement>();
            capElement.minWidth = 92f;
            var capFit = cap.AddComponent<ContentSizeFitter>();
            capFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var capGroup = cap.AddComponent<HorizontalLayoutGroup>();
            capGroup.padding = new RectOffset(20, 20, 0, 0);
            capGroup.childAlignment = TextAnchor.MiddleCenter;
            capGroup.childControlWidth = true;
            capGroup.childControlHeight = true;
            capGroup.childForceExpandWidth = false;
            Sprite(cap, UiSpriteBaker.KeyCap, UiSkin.Plate, Image.Type.Sliced, 2f);

            GameObject edge = Node("Edge", cap.transform);
            Stretch((RectTransform)edge.transform);
            Sprite(edge, UiSpriteBaker.KeyCapEdge, UiSkin.CardEdge, Image.Type.Sliced, 2f);
            edge.AddComponent<LayoutElement>().ignoreLayout = true;

            TMP_Text key = Label(Node("Key", cap.transform), font, 26f, UiSkin.TextPrimary,
                TextAlignmentOptions.Center, FontStyles.Bold);

            TMP_Text action = Label(Node("Action", row.transform), font, 28f, UiSkin.TextSecondary,
                TextAlignmentOptions.Left, FontStyles.Normal);
            Flexible(action.gameObject);

            var component = row.AddComponent<TutorialHintRow>();
            Fields(component, ("keyCap", cap), ("keyText", key), ("actionText", action));
            row.SetActive(false);
            return component;
        }

        /// <summary>Низ карточки: полоса автостарта и напоминание, что её можно пропустить.</summary>
        private static Image Footer(Transform parent, TMP_FontAsset font)
        {
            GameObject footer = Node("Footer", parent);
            Line(footer, 44f);
            var group = footer.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 24f;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;

            // Полоса — именно полоса: без этого компоновка растянет её
            // на всю высоту подвала, и вместо линии выйдет жёлтый овал.
            group.childForceExpandHeight = false;

            GameObject track = Node("Track", footer.transform);
            Line(track, 10f);
            Flexible(track);
            Sprite(track, UiSpriteBaker.Card, UiSkin.Plate, Image.Type.Sliced, 8f);

            GameObject fill = Node("Fill", track.transform);
            Stretch((RectTransform)fill.transform);
            Image image = Sprite(fill, UiSpriteBaker.Card, UiSkin.Accent, Image.Type.Sliced, 8f);
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;

            TMP_Text hint = Label(Node("SkipHint", footer.transform), font, 22f, UiSkin.TextMuted,
                TextAlignmentOptions.Right, FontStyles.Normal);
            hint.text = "Любая клавиша — начать";
            Line(hint.gameObject, 44f, 360f);

            return image;
        }

        // ========== Колесо эмоций ==========

        /// <summary>
        /// Переодеть колесо насмешек: кольцо из восьми секторов, подписи
        /// по кругу, ступица в центре.
        ///
        /// ⚠️ Колесо входит в замороженное (<c>igruha/CLAUDE.md</c>, раздел 0):
        /// восемь секторов, порядок по часовой стрелке с верхнего, клавиша Tab
        /// и связь с <c>PlayerEmoteAbility</c> остаются ровно такими же.
        /// Меняется только вид, и только по прямой просьбе геймдизайнера.
        /// Сам компонент не пересоздаётся: в нём лежит ссылка на риг камеры
        /// из сцены, и новый объект потерял бы её молча.
        /// </summary>
        private static void BuildWheel(EmoteWheel wheel, TMP_FontAsset font)
        {
            Transform old = wheel.transform.Find("Panel");
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            var wheelRect = wheel.transform as RectTransform;
            if (wheelRect != null)
            {
                Stretch(wheelRect);
            }

            GameObject panel = Node("Panel", wheel.transform);
            Stretch((RectTransform)panel.transform);
            Scrim(panel.transform, 0.55f);

            GameObject ring = Node("Ring", panel.transform);
            Anchor((RectTransform)ring.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(WheelRadius * 2.2f, WheelRadius * 2.2f));

            var slots = new EmoteWheelSlot[8];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = WheelSlot(ring.transform, font, i);
            }

            // Курсор кладётся под ступицу: в покое колесо ставит его в центр,
            // и поверх ступицы он читался бы как жёлтая клякса на надписи.
            // Уезжая к сектору, он выходит из-под неё.
            GameObject pointer = Node("Pointer", ring.transform);
            Anchor((RectTransform)pointer.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 34f));
            Sprite(pointer, UiSpriteBaker.Circle, UiSkin.Accent, Image.Type.Simple);

            GameObject hub = Node("Hub", ring.transform);
            Anchor((RectTransform)hub.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250f, 250f));
            Sprite(hub, UiSpriteBaker.Circle, UiSkin.Card, Image.Type.Simple);

            TMP_Text hubKey = Label(Node("Key", hub.transform), font, 40f, UiSkin.Accent,
                TextAlignmentOptions.Center, FontStyles.Bold);
            hubKey.text = "Tab";
            Anchor(hubKey.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 26f), new Vector2(220f, 60f));

            TMP_Text hubHint = Label(Node("Hint", hub.transform), font, 20f, UiSkin.TextMuted,
                TextAlignmentOptions.Center, FontStyles.Normal);
            hubHint.text = "отпусти — станцуешь";
            Anchor(hubHint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -34f), new Vector2(230f, 56f));

            panel.SetActive(false);

            var so = new SerializedObject(wheel);
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("pointer").objectReferenceValue = pointer.transform;
            so.FindProperty("pointerRadius").floatValue = WheelPointerRadius;
            Array(so.FindProperty("slots"), slots);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static EmoteWheelSlot WheelSlot(Transform ring, TMP_FontAsset font, int index)
        {
            // Сектор в текстуре смотрит вверх, номера эмоций идут по часовой
            // стрелке с верхнего — значит поворот отрицательный.
            float angle = -45f * index;

            GameObject slot = Node($"Slot_{index + 1}", ring);
            var rect = (RectTransform)slot.transform;
            Anchor(rect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(WheelRadius * 2f, WheelRadius * 2f));
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            Image background = Sprite(slot, UiSpriteBaker.Sector, UiSkin.PlateOnScene, Image.Type.Simple);
            background.raycastTarget = false;

            // Подпись живёт отдельно от сектора и не поворачивается вместе
            // с ним: текст, повёрнутый на 135°, читается вверх ногами.
            float radians = (90f + angle) * Mathf.Deg2Rad;
            var offset = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (WheelRadius * 0.74f);
            TMP_Text label = Label(Node($"Label_{index + 1}", ring), font, 24f, UiSkin.TextPrimary,
                TextAlignmentOptions.Center, FontStyles.Bold);
            Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), offset, new Vector2(196f, 62f));
            label.textWrappingMode = TextWrappingModes.Normal;

            var component = slot.AddComponent<EmoteWheelSlot>();
            Fields(component,
                ("background", background),
                ("label", label),
                ("idleColor", UiSkin.PlateOnScene),
                ("hoveredColor", UiSkin.Accent),
                ("emptyColor", new Color(UiSkin.PlateOnScene.r, UiSkin.PlateOnScene.g, UiSkin.PlateOnScene.b, 0.35f)),
                ("idleLabelColor", UiSkin.TextPrimary),
                ("hoveredLabelColor", UiSkin.AccentInk));
            return component;
        }

        // ========== Кирпичи ==========

        private static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>Тёмная подложка модального экрана плюс виньетка по краям кадра.</summary>
        private static void Scrim(Transform parent, float alpha = 1f)
        {
            GameObject scrim = Node("Scrim", parent);
            Stretch((RectTransform)scrim.transform);
            Color color = UiSkin.Scrim;
            color.a *= alpha;
            Sprite(scrim, null, color, Image.Type.Simple);

            GameObject vignette = Node("Vignette", parent);
            Stretch((RectTransform)vignette.transform);
            Sprite(vignette, UiSpriteBaker.Vignette, new Color(0f, 0f, 0f, 0.45f * alpha), Image.Type.Simple);
        }

        /// <summary>Карточка: подложка, обводка, вертикальная компоновка, рост по содержимому.</summary>
        private static GameObject Card(string name, Transform parent, float width, out VerticalLayoutGroup layout,
            float fixedHeight = 0f)
        {
            GameObject card = Node(name, parent);
            var rect = (RectTransform)card.transform;
            Anchor(rect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, Mathf.Max(100f, fixedHeight)));
            Sprite(card, UiSpriteBaker.Card, UiSkin.Card);

            GameObject edge = Node("Edge", card.transform);
            Stretch((RectTransform)edge.transform);
            Sprite(edge, UiSpriteBaker.Stroke, UiSkin.CardEdge);
            edge.AddComponent<LayoutElement>().ignoreLayout = true;

            layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(56, 56, 44, 40);
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            if (fixedHeight <= 0f)
            {
                var fitter = card.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            return card;
        }

        private static void Divider(Transform parent)
        {
            GameObject line = Node("Divider", parent);
            Line(line, 2f);
            Sprite(line, UiSpriteBaker.Card, new Color(1f, 1f, 1f, 0.08f), Image.Type.Sliced, 20f);
        }

        private static GameObject Plate(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            GameObject plate = Node(name, parent);
            Anchor((RectTransform)plate.transform, anchor, position, size);
            Sprite(plate, UiSpriteBaker.Chip, UiSkin.PlateOnScene);
            return plate;
        }

        /// <summary>
        /// Картинка с девятислайсовым скруглением.
        ///
        /// <paramref name="pixelsPerUnit"/> ужимает радиус скругления:
        /// у спрайта он 40 пикселей, и на полоске высотой 8 две угловые зоны
        /// по 42 не помещаются — Unity ужимает слайсы пропорционально, и
        /// полоска выходит эллипсом. Множитель 8 делает радиус 5, и полоска
        /// остаётся полоской.
        /// </summary>
        private static Image Sprite(GameObject go, string path, Color color, Image.Type type = Image.Type.Sliced,
            float pixelsPerUnit = 1f)
        {
            var image = go.AddComponent<Image>();
            image.sprite = path != null ? AssetDatabase.LoadAssetAtPath<Sprite>(path) : null;
            image.type = image.sprite != null ? type : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            image.pixelsPerUnitMultiplier = pixelsPerUnit;
            return image;
        }

        /// <summary>Строка-обёртка: прижимает вложенное к левому краю карточки.</summary>
        private static GameObject Row(string name, Transform parent, float height)
        {
            GameObject row = Node(name, parent);
            Line(row, height);
            var group = row.AddComponent<HorizontalLayoutGroup>();
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;
            return row;
        }

        private static TMP_Text Label(GameObject go, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align, FontStyles style)
        {
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        /// <summary>Фиксированная высота (и, если задана, ширина) элемента компоновки.</summary>
        private static void Line(GameObject go, float height, float width = 0f)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = go.AddComponent<LayoutElement>();
            }

            element.minHeight = height;
            element.preferredHeight = height;

            // Ноль, а не «не задано»: иначе свободную высоту карточки
            // компоновка раздаёт поровну всем строкам, и вместо просторного
            // описания получается описание в три строки и восемь раздутых
            // строк управления.
            element.flexibleHeight = 0f;

            if (width > 0f)
            {
                element.preferredWidth = width;
                element.flexibleWidth = 0f;
            }
        }

        private static void Flexible(GameObject go)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = go.AddComponent<LayoutElement>();
            }

            element.flexibleWidth = 1f;
        }

        private static void Stretch(RectTransform rect)
        {
            Stretch(rect, Vector4.zero);
        }

        /// <summary>Растянуть по родителю с отступами (лево, низ, право, верх).</summary>
        private static void Stretch(RectTransform rect, Vector4 margins)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(margins.x, margins.y);
            rect.offsetMax = new Vector2(-margins.z, -margins.w);
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, anchor.y);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        /// <summary>Проставить приватные поля компонента через SerializedObject.</summary>
        private static void Fields(Object target, params (string Name, object Value)[] values)
        {
            var so = new SerializedObject(target);
            for (int i = 0; i < values.Length; i++)
            {
                SerializedProperty property = so.FindProperty(values[i].Name);
                if (property == null)
                {
                    Debug.LogWarning($"HudSkin: у {target.GetType().Name} нет поля {values[i].Name}");
                    continue;
                }

                switch (values[i].Value)
                {
                    case float f: property.floatValue = f; break;
                    case bool b: property.boolValue = b; break;
                    case Color c: property.colorValue = c; break;
                    case Object o: property.objectReferenceValue = o; break;
                    default: Debug.LogWarning($"HudSkin: тип поля {values[i].Name} не поддержан"); break;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Array<T>(SerializedProperty property, IReadOnlyList<T> items) where T : Object
        {
            property.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
        }
    }
}
