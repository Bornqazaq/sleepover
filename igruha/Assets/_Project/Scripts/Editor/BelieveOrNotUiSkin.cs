using Igruha.Core.UI;
using Igruha.Minigames.BelieveOrNot;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Интерфейс «Верю / не верю» в клубном стиле (<see cref="UiTheme"/>, IGR-565):
    /// брифинг, таймер, отсчёт, итоги, карта знающего, решение, реплики,
    /// строки роли и реплики, облачка над сидящими.
    ///
    /// <b>Порядок.</b> Идёт после <see cref="UiSkinPass"/>: общий скин владеет
    /// таймером, брифингом и итогами и пересобирает их с нуля. Всё, что здесь
    /// заведено сверху, заводится заново тем же прогоном — пересборка сцены
    /// воспроизводит интерфейс целиком.
    ///
    /// <b>Раскладка экрана сидящего — от лица соперника.</b> Лицо стоит в центре
    /// кадра, шкатулки — под ним. Поэтому реплика ушла под таймер (раньше
    /// она стояла ровно на лице), решение — в правую колонку (раньше закрывало
    /// ближнюю шкатулку), карта знающего и реплики — в левую.
    ///
    /// Колесо эмоций не трогается: оно заморожено (<c>igruha/CLAUDE.md</c>, раздел 0).
    /// </summary>
    internal static class BelieveOrNotUiSkin
    {
        /// <summary>Центр боковых колонок от центра экрана, px опорного разрешения 1920×1080.</summary>
        private const float ColumnX = 548f;

        /// <summary>Верх боковых колонок над центром экрана.</summary>
        private const float ColumnTop = 150f;

        private const float TalkFromTop = 126f;
        private const float RoleFromBottom = 16f;

        private static readonly Color DeciderColor = new Color(0.74f, 0.85f, 0.94f, 1f);

        [MenuItem("Igruha/Верю не верю/Оформить интерфейс")]
        public static void ApplyMenu()
        {
            Apply();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        internal static void Apply()
        {
            UiFonts.EnsureAll();
            UiTheme.EnsureSpriteImports();
            BelieveCardArt.EnsureAssets();

            Transform canvas = FindScreenCanvas();
            if (canvas == null)
            {
                throw new System.InvalidOperationException("Нет экранного канваса «Canvas» в сцене.");
            }

            Timer(canvas);
            Countdown(canvas);
            Tutorial(canvas.Find("_HudOverlay/TutorialPanel"));
            Results(canvas.Find("_HudOverlay/ResultsPanel"));
            ScenePlate(canvas.Find("_HudPlates/StatusPlate"));
            ScenePlate(canvas.Find("_HudPlates/SpectatorPlate"));
            Peek(canvas.Find("PeekView"));
            Decision(canvas.Find("DecisionPanel"));
            Phrases(canvas.Find("PhrasePanel"));
            SeatLines(canvas.Find("SeatHud"));

            foreach (SpeechBubble bubble in Object.FindObjectsByType<SpeechBubble>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Bubble(bubble.transform);
            }
        }

        // ================= Общий HUD =================

        private static void Timer(Transform canvas)
        {
            Transform plate = canvas.Find("_HudPlates/TimerPlate");
            if (plate == null)
            {
                return;
            }

            var rect = (RectTransform)plate;
            rect.sizeDelta = new Vector2(232f, 80f);
            rect.anchoredPosition = new Vector2(0f, -26f);
            UiTheme.Paint(plate.GetComponent<Image>(), UiSpriteBaker.Chip, UiTheme.Surface);
            UiTheme.Overlay(plate, "ClubEdge", UiTheme.ChipStroke, UiTheme.BrassEdge);

            // Цвет таймера каждый кадр ставит RoundHud (обычный / последние секунды).
            UiTheme.Text(Text(plate, "TimerText"), UiFonts.Numbers, 50f, UiTheme.Cream, 2f);

            // Плашку отдаём RoundHud, чтобы он убирал её на итогах: раунд
            // кончился, а секунды под затемнением продолжали бежать.
            var hud = Object.FindFirstObjectByType<RoundHud>(FindObjectsInactive.Include);
            if (hud != null)
            {
                var so = new SerializedObject(hud);
                so.FindProperty("timerPlate").objectReferenceValue = plate.gameObject;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void Countdown(Transform canvas)
        {
            TMP_Text line = Text(canvas, "_HudOverlay/CountdownLine");
            if (line == null)
            {
                return;
            }

            UiTheme.Text(line, UiFonts.Numbers, 240f, UiTheme.Cream);
            line.fontSharedMaterial = UiTheme.ShadowMaterial(UiFonts.Numbers);
        }

        private static void Tutorial(Transform panel)
        {
            if (panel == null)
            {
                return;
            }

            Paint(panel, "Scrim", null, UiTheme.Scrim);
            Transform card = panel.Find("Card");
            Paint(card, null, UiSpriteBaker.Card, UiTheme.Surface);
            Paint(card, "Edge", UiSpriteBaker.Stroke, UiTheme.BrassEdge);

            Paint(card, "Header/AccentBar", UiSpriteBaker.Card, UiTheme.Brass);
            UiTheme.Title(Text(card, "Header/Title"), 62f);
            Paint(card, "Header/CategoryChip", UiSpriteBaker.Chip, UiTheme.Brass);
            UiTheme.Caption(Text(card, "Header/CategoryChip/Text"), 17f, UiTheme.BrassInk);

            UiTheme.Text(Text(card, "Objective"), UiFonts.SansMedium, 30f, UiTheme.Parchment);
            Ornament(card.Find("Divider"));
            UiTheme.Caption(Text(card, "Caption"), 18f);

            foreach (Transform row in card)
            {
                if (!row.name.StartsWith("Hint_"))
                {
                    continue;
                }

                Paint(row, "KeyCap", UiSpriteBaker.KeyCap, UiTheme.SurfaceRaised);
                Paint(row, "KeyCap/Edge", UiSpriteBaker.KeyCapEdge, UiTheme.BrassEdge);
                UiTheme.Text(Text(row, "KeyCap/Key"), UiFonts.SansBold, 23f, UiTheme.Cream);
                UiTheme.Text(Text(row, "Action"), UiFonts.SansMedium, 25f, UiTheme.Parchment);
            }

            Paint(card, "Footer/Track", UiSpriteBaker.Card, UiTheme.SurfaceSunken);
            Paint(card, "Footer/Track/Fill", UiSpriteBaker.Card, UiTheme.Brass);
            UiTheme.Text(Text(card, "Footer/SkipHint"), UiFonts.SansMedium, 19f, UiTheme.Muted);
        }

        private static void Results(Transform panel)
        {
            if (panel == null)
            {
                return;
            }

            Paint(panel, "Scrim", null, UiTheme.Scrim);
            Transform card = panel.Find("Card");
            Paint(card, null, UiSpriteBaker.Card, UiTheme.Surface);
            Paint(card, "Edge", UiSpriteBaker.Stroke, UiTheme.BrassEdge);

            // Надзаголовок и подпись — как в брифинге «Дырки в стене»: сухой
            // список мест не говорит, чей это был кон и что будет дальше.
            TMP_Text eyebrow = EnsureText(card, "Eyebrow", "ВЕРЮ / НЕ ВЕРЮ · ИТОГИ КОНА");
            UiTheme.Caption(eyebrow, 18f, UiTheme.Brass);
            eyebrow.alignment = TextAlignmentOptions.Center;
            Row(eyebrow.transform, 26f).transform.SetSiblingIndex(0);

            UiTheme.Title(Text(card, "Title"), 56f);
            Ornament(card.Find("Divider"));

            // Цвет медали и имени победителя ставит ResultRow по месту.
            foreach (Transform row in card)
            {
                if (!row.name.StartsWith("Result_"))
                {
                    continue;
                }

                UiTheme.Text(Text(row, "Badge/Place"), UiFonts.Numbers, 28f, UiTheme.Cream);
                UiTheme.Text(Text(row, "Name"), UiFonts.SansBold, 30f, UiTheme.Cream);
            }

            TMP_Text footnote = EnsureText(card, "Footnote", "Победит тот, кто чаще читал соперника");
            UiTheme.Text(footnote, UiFonts.SansMedium, 20f, UiTheme.Muted);
            footnote.alignment = TextAlignmentOptions.Center;
            Row(footnote.transform, 30f);

            Transform restart = card.Find("RestartButton");
            footnote.transform.SetSiblingIndex(restart != null ? restart.GetSiblingIndex() : card.childCount - 1);

            // Карточка выезжает, а не появляется рывком: тот же UiPop, что
            // держит брифинг в «Дырке в стене».
            if (card.GetComponent<UiPop>() == null)
            {
                card.gameObject.AddComponent<UiPop>();
            }

            Paint(restart, null, UiSpriteBaker.Chip, UiTheme.Brass);
            UiTheme.Text(Text(restart, "Label"), UiFonts.SerifBold, 30f, UiTheme.BrassInk, UiTheme.TitleSpacing);
            UiTheme.ButtonStates(restart != null ? restart.GetComponent<Button>() : null, brassFill: true);
        }

        /// <summary>Строка вертикальной раскладки фиксированной высоты.</summary>
        private static LayoutElement Row(Transform target, float height)
        {
            var element = target.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = target.gameObject.AddComponent<LayoutElement>();
            }

            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
            return element;
        }

        private static void ScenePlate(Transform plate)
        {
            if (plate == null)
            {
                return;
            }

            UiTheme.Paint(plate.GetComponent<Image>(), UiSpriteBaker.Chip, UiTheme.PlateOnScene);
            UiTheme.Overlay(plate, "ClubEdge", UiTheme.ChipStroke, UiTheme.BrassHairline);
            foreach (TMP_Text text in plate.GetComponentsInChildren<TMP_Text>(true))
            {
                UiTheme.Text(text, UiFonts.SansMedium, text.fontSize, UiTheme.Cream);
            }
        }

        // ================= Панели игры =================

        /// <summary>Карта знающего: лицо карточки слева, пояснение справа.</summary>
        private static void Peek(Transform view)
        {
            Transform root = view != null ? view.Find("Root") : null;
            if (root == null)
            {
                return;
            }

            var rootRect = (RectTransform)root;
            SetBox(rootRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(-ColumnX, ColumnTop), new Vector2(520f, 290f));
            Paint(root, null, UiSpriteBaker.Card, UiTheme.Surface);
            Paint(root, "Edge", UiSpriteBaker.Stroke, UiTheme.BrassEdge);

            var card = (RectTransform)root.Find("Card");
            SetBox(card, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 18f), new Vector2(150f, 210f));
            Image face = card.GetComponent<Image>();
            face.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BelieveCardArt.WinFace);
            face.preserveAspect = true;
            face.color = Color.white;

            var label = (RectTransform)card.Find("CardLabel");
            SetBox(label, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(170f, 24f));
            TMP_Text labelText = label.GetComponent<TMP_Text>();
            UiTheme.Caption(labelText, 15f, UiTheme.Parchment);
            labelText.alignment = TextAlignmentOptions.Center;

            TMP_Text heading = EnsureText(root, "Heading", "Твоя карта");
            SetBox((RectTransform)heading.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(204f, -30f), new Vector2(290f, 24f));
            UiTheme.Caption(heading, 16f);
            heading.alignment = TextAlignmentOptions.TopLeft;

            TMP_Text hint = Text(root, "Hint");
            SetBox((RectTransform)hint.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(204f, -66f), new Vector2(290f, 120f));
            UiTheme.Text(hint, UiFonts.SansMedium, 23f, UiTheme.Cream);
            hint.alignment = TextAlignmentOptions.TopLeft;
            hint.textWrappingMode = TextWrappingModes.Normal;

            TMP_Text countdown = Text(root, "Countdown");
            SetBox((RectTransform)countdown.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(204f, 30f), new Vector2(290f, 48f));
            UiTheme.Text(countdown, UiFonts.SansBold, 38f, UiTheme.BrassBright);
            countdown.alignment = TextAlignmentOptions.BottomLeft;

            var peek = view.GetComponent<BelievePeekView>();
            var so = new SerializedObject(peek);
            so.FindProperty("winSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(BelieveCardArt.WinFace);
            so.FindProperty("loseSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(BelieveCardArt.LoseFace);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Решение: правая колонка, отсчёт до конца уговоров с подписью.</summary>
        private static void Decision(Transform panel)
        {
            Transform root = panel != null ? panel.Find("Root") : null;
            if (root == null)
            {
                return;
            }

            SetBox((RectTransform)root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(ColumnX, ColumnTop), new Vector2(500f, 236f));
            Paint(root, null, UiSpriteBaker.Card, UiTheme.Surface);
            Paint(root, "Edge", UiSpriteBaker.Stroke, UiTheme.BrassEdge);

            TMP_Text heading = EnsureText(root, "Heading", "Твоё решение");
            SetBox((RectTransform)heading.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -30f), new Vector2(260f, 24f));
            UiTheme.Caption(heading, 16f);
            heading.alignment = TextAlignmentOptions.TopLeft;

            // «1» без подписи читалось непонятной цифрой. Это секунды до конца уговоров.
            TMP_Text countdown = Text(root, "Countdown");
            SetBox((RectTransform)countdown.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -16f), new Vector2(120f, 50f));
            // Обычные цифры, а не моноширинные: у короткого отсчёта «13» разъезжается
            // в «1 3», а дрожать по ширине ему не от чего — он прижат вправо.
            UiTheme.Text(countdown, UiFonts.SansBold, 42f, UiTheme.Cream);
            countdown.alignment = TextAlignmentOptions.TopRight;

            TMP_Text countdownCaption = EnsureText(root, "CountdownCaption", "сек. до конца уговоров");
            SetBox((RectTransform)countdownCaption.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -66f), new Vector2(260f, 22f));
            UiTheme.Text(countdownCaption, UiFonts.SansMedium, 15f, UiTheme.Muted);
            countdownCaption.alignment = TextAlignmentOptions.TopRight;

            ChoiceButton(root.Find("KeepButton"), new Vector2(-116f, -18f));
            ChoiceButton(root.Find("SwapButton"), new Vector2(116f, -18f));

            TMP_Text hint = Text(root, "Hint");
            SetBox((RectTransform)hint.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(440f, 24f));
            UiTheme.Text(hint, UiFonts.SansMedium, 16f, UiTheme.Muted);
            hint.alignment = TextAlignmentOptions.Center;

            // Последние секунды отсчёта — латунью. Над кадром висит матчевый
            // таймер вчетверо крупнее, и решают именно по этому числу.
            var decision = panel.GetComponentInParent<BelieveDecisionPanel>();
            if (decision != null)
            {
                var so = new SerializedObject(decision);
                so.FindProperty("countdownColor").colorValue = UiTheme.Cream;
                so.FindProperty("countdownUrgentColor").colorValue = UiTheme.BrassBright;
                so.FindProperty("urgentSeconds").intValue = 5;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void ChoiceButton(Transform button, Vector2 position)
        {
            if (button == null)
            {
                return;
            }

            SetBox((RectTransform)button, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(212f, 66f));
            UiTheme.Paint(button.GetComponent<Image>(), UiSpriteBaker.Chip, UiTheme.SurfaceRaised);
            UiTheme.Overlay(button, "ClubEdge", UiTheme.ChipStroke, UiTheme.BrassEdge);
            // Гротеск, а не засечки: стрелка у Playfair — завиток с оперением,
            // и «← ОСТАВИТЬ» на кнопке читалось как орнамент.
            UiTheme.Text(Text(button, "Label"), UiFonts.SansBold, 20f, UiTheme.Cream, 4f);
            UiTheme.ButtonStates(button.GetComponent<Button>());
        }

        /// <summary>Реплики: левая колонка, строки собирает Core в рантайме — стиль идёт через его поля.</summary>
        private static void Phrases(Transform panel)
        {
            Transform root = panel != null ? panel.Find("Root") : null;
            if (root == null)
            {
                return;
            }

            SetBox((RectTransform)root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(-ColumnX, ColumnTop), new Vector2(440f, 260f));
            Paint(root, null, UiSpriteBaker.Card, UiTheme.Surface);
            Paint(root, "Edge", UiSpriteBaker.Stroke, UiTheme.BrassEdge);

            TMP_Text hint = Text(root, "Hint");
            var hintRect = (RectTransform)hint.transform;
            hintRect.anchorMin = new Vector2(0f, 1f);
            hintRect.anchorMax = new Vector2(1f, 1f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.offsetMin = new Vector2(24f, -58f);
            hintRect.offsetMax = new Vector2(-24f, -16f);
            UiTheme.Title(hint, 25f);
            hint.alignment = TextAlignmentOptions.MidlineLeft;

            var rows = (RectTransform)root.Find("Rows");
            rows.anchorMin = new Vector2(0f, 1f);
            rows.anchorMax = new Vector2(1f, 1f);
            rows.pivot = new Vector2(0.5f, 1f);
            rows.anchoredPosition = new Vector2(0f, -66f);
            rows.sizeDelta = new Vector2(-36f, 210f);

            var so = new SerializedObject(panel.GetComponent<QuickPhrasePanel>());
            so.FindProperty("rowHeight").floatValue = 42f;
            so.FindProperty("rowSpacing").floatValue = 6f;
            so.FindProperty("fontSize").floatValue = 21f;
            so.FindProperty("rowColor").colorValue = UiTheme.SurfaceSunken;
            so.FindProperty("rowReadyColor").colorValue = UiTheme.SurfaceRaised;
            so.FindProperty("textColor").colorValue = UiTheme.Cream;
            so.FindProperty("rowFont").objectReferenceValue = UiFonts.SansMedium;
            so.FindProperty("rowSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(UiSpriteBaker.KeyCap);
            so.FindProperty("rowTextInset").floatValue = 16f;
            // Номер строки — латунью: это клавиша, а одним цветом с текстом
            // он читался частью реплики, а не подсказкой, что нажать.
            so.FindProperty("numberTint").colorValue = UiTheme.Brass;
            so.FindProperty("fitRootToRows").boolValue = true;
            so.FindProperty("rootBottomPadding").floatValue = 18f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Строка роли — внизу на плашке, реплика — под таймером на плашке.
        /// Плашка подгоняется под текст раскладчиком и прячется вместе с ним
        /// (<see cref="BelieveSeatHud"/> знает о ней).
        /// </summary>
        private static void SeatLines(Transform hud)
        {
            if (hud == null)
            {
                return;
            }

            GameObject rolePlate = LinePlate(hud, "RolePlate", Text(hud, "RoleLine") ?? Text(hud, "RolePlate/RoleLine"),
                new Vector2(0.5f, 0f), new Vector2(0f, RoleFromBottom), UiTheme.Surface, UiTheme.BrassHairline,
                UiFonts.SansMedium, 21f);
            GameObject talkPlate = LinePlate(hud, "TalkPlate", Text(hud, "TalkLine") ?? Text(hud, "TalkPlate/TalkLine"),
                new Vector2(0.5f, 1f), new Vector2(0f, -TalkFromTop), UiTheme.Surface, UiTheme.BrassEdge,
                UiFonts.SansBold, 27f);

            var so = new SerializedObject(hud.GetComponent<BelieveSeatHud>());
            so.FindProperty("knowerColor").colorValue = UiTheme.BrassBright;
            so.FindProperty("deciderColor").colorValue = DeciderColor;
            so.FindProperty("neutralColor").colorValue = UiTheme.Cream;
            so.FindProperty("roleDetailColor").colorValue = UiTheme.Parchment;
            so.FindProperty("goodColor").colorValue = UiTheme.Good;
            so.FindProperty("badColor").colorValue = UiTheme.Bad;
            so.FindProperty("rolePlate").objectReferenceValue = rolePlate;
            so.FindProperty("talkPlate").objectReferenceValue = talkPlate;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Плашки видны, только пока игра показывает строку.
            rolePlate.SetActive(false);
            talkPlate.SetActive(false);
        }

        private static GameObject LinePlate(Transform hud, string name, TMP_Text line, Vector2 anchor, Vector2 position,
            Color fill, Color edge, TMP_FontAsset font, float size)
        {
            Transform existing = hud.Find(name);
            GameObject plate = existing != null
                ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));

            var rect = (RectTransform)plate.transform;
            rect.SetParent(hud, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;

            Image image = UiTheme.Paint(plate.GetComponent<Image>(), UiSpriteBaker.Chip, fill);
            image.raycastTarget = false;

            var layout = plate.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 10, 10);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var fitter = plate.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (line != null)
            {
                line.transform.SetParent(rect, false);
                line.transform.SetAsFirstSibling();
                UiTheme.Text(line, font, size, UiTheme.Cream);
                line.alignment = TextAlignmentOptions.Center;
                line.textWrappingMode = TextWrappingModes.NoWrap;
                line.overflowMode = TextOverflowModes.Overflow;
                line.raycastTarget = false;
                line.enabled = true;
            }

            UiTheme.Overlay(rect, "Edge", UiTheme.ChipStroke, edge);
            return plate;
        }

        private static void Bubble(Transform bubble)
        {
            Transform root = bubble.Find("Root");
            if (root == null)
            {
                return;
            }

            UiTheme.Paint(root.GetComponent<Image>(), UiSpriteBaker.Card, UiTheme.SurfaceRaised);
            UiTheme.Overlay(root, "Edge", UiSpriteBaker.Stroke, UiTheme.BrassEdge);
            TMP_Text label = Text(root, "Label");
            UiTheme.Text(label, UiFonts.SansBold, 26f, UiTheme.Cream);
            if (label != null)
            {
                label.transform.SetAsLastSibling();
            }
        }

        // ================= Приёмы =================

        /// <summary>Разделитель — волосяная латунная линия с ромбом, выше прежней полоски.</summary>
        private static void Ornament(Transform divider)
        {
            if (divider == null)
            {
                return;
            }

            Image image = UiTheme.Paint(divider.GetComponent<Image>(), UiTheme.Ornament, UiTheme.Brass, Image.Type.Simple);
            image.preserveAspect = false;
            var element = divider.GetComponent<LayoutElement>();
            if (element != null)
            {
                element.minHeight = 16f;
                element.preferredHeight = 16f;
            }
        }

        private static void Paint(Transform parent, string path, string spritePath, Color color)
        {
            if (parent == null)
            {
                return;
            }

            Transform target = string.IsNullOrEmpty(path) ? parent : parent.Find(path);
            if (target != null)
            {
                UiTheme.Paint(target.GetComponent<Image>(), spritePath, color);
            }
        }

        private static TMP_Text Text(Transform parent, string path)
        {
            if (parent == null)
            {
                return null;
            }

            Transform target = parent.Find(path);
            return target != null ? target.GetComponent<TMP_Text>() : null;
        }

        private static TMP_Text EnsureText(Transform parent, string name, string value)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null
                ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TMP_Text>();
            text.text = value;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        private static void SetBox(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>Экранный канвас шаблона: верхний, поверх экрана, с именем «Canvas». Он не обязан лежать в корне сцены.</summary>
        private static Transform FindScreenCanvas()
        {
            Scene scene = SceneManager.GetActiveScene();
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.gameObject.scene == scene && canvas.isRootCanvas &&
                    canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.name == "Canvas")
                {
                    return canvas.transform;
                }
            }

            return null;
        }
    }
}
