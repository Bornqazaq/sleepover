using System;
using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Стена с вырезами: едет на игроков, доезжает и уходит за спину. Сама
    /// угроза игры.
    ///
    /// Стена <b>только сообщает</b>, где её передняя грань. Кто прошёл, а кто
    /// полетел в воду, решает контроллер: вердикт парный, и считать его должен
    /// тот, кто знает состав дорожки.
    /// </summary>
    /// <remarks>
    /// <b>Положение — чистая функция от общих часов, а не хранимое состояние.</b>
    /// Прямой образец — <c>Core/Traps/SwingingBeamTrap</c>: каждая машина берёт
    /// положение по одной формуле от одного объявленного момента, поэтому
    /// в фазе 3 стена сойдётся у всех без единого пакета на её движение,
    /// а подключившийся в середине подъезда сразу увидит её там же.
    /// Трансформ — лишь отрисовка этой формулы, и никто её не спрашивает
    /// у трансформа.
    ///
    /// <b>Подвох тоже чистая функция.</b> Момент срабатывания объявлен заранее
    /// вместе с рисунком, поэтому «какой вырез действует прямо сейчас»
    /// отвечается одинаково и в кадре отрисовки, и в физическом такте, где
    /// контроллер считает вердикт. Бросать подвох в рантайме нельзя: в фазе 3
    /// это пришлось бы переписывать.
    ///
    /// <b>Коллайдеры у стены есть, но утоплены.</b> Полгода их не было вовсе:
    /// проверка в игре дискретная (спека 5.3), а сплошная плита вернула бы
    /// в неё габариты капсулы — краем выреза она толкала бы вбок того, кто
    /// стоит верно, но чуть шире прочих. Конфликт снят геометрией, а не отказом
    /// от физики: коллайдер утоплен на <see cref="ColliderRecess"/> и касается
    /// только того, кто уже провалился, а прошедшим плиты снимает вердикт
    /// (<see cref="DisableCollision"/>). Разбор — STATE.md, раздел 3.52.
    /// </remarks>
    public sealed class SweepingWall : MonoBehaviour
    {
        /// <summary>Куда едет стена. Ею же задаётся направление, в которое сметает провалившихся.</summary>
        public static readonly Vector3 TravelDirection = Vector3.back;

        [Header("Панели — собираются построителем арены")]
        [SerializeField] private Transform panelLeft;
        [SerializeField] private Transform panelMiddle;
        [SerializeField] private Transform panelRight;
        [SerializeField] private Transform lintelFirst;
        [SerializeField] private Transform lintelSecond;

        [Header("Вырезы")]
        /// <summary>
        /// Насколько коллайдер плиты утоплен назад от её видимой передней
        /// грани, м.
        ///
        /// ⚠️ Не косметика, а разрешение конфликта, из-за которого коллайдеров
        /// у стены не было вовсе. Допуск попадания 0.576 м равен половине
        /// самого узкого выреза, а радиус капсулы игрока — 0.36 м: игрок
        /// на границе допуска, которого проверка считает <b>прошедшим</b>,
        /// касается плиты боком. Передняя грань доходит до его капсулы
        /// на 0.36/скорость раньше вердикта — при 9 м/с это 40 мс, три шага
        /// физики, и стена успевала бы вытолкнуть его из допуска ДО проверки.
        ///
        /// Утопленный коллайдер отдаёт эти сорок миллисекунд вердикту: он
        /// касается только того, кто уже провалился. При этом прыжок в стену
        /// снаружи упирается в неё, а не проходит сквозь — ради чего
        /// коллайдеры и появились.
        /// </summary>
        private const float ColliderRecess = 0.45f;

        /// <summary>
        /// На сколько горизонтальных полос режется стена, когда вырез
        /// повторяет силуэт позы.
        ///
        /// 24 полосы на 3.6 м — это 15 см на ступеньку. С максимальной
        /// дистанции подъезда (30 ШП) она занимает около десяти пикселей:
        /// контур читается позой, а не лесенкой. Полосы с одинаковым пролётом
        /// склеиваются, поэтому плит выходит вдвое-втрое меньше числа полос.
        /// </summary>
        private const int ShapeBands = 24;

        /// <summary>Плиты стены. Первые пять пришли из сцены, остальные клонируются от них по мере надобности.</summary>
        private readonly System.Collections.Generic.List<Transform> panelPool =
            new System.Collections.Generic.List<Transform>(ShapeBands);

        /// <summary>Сколько плит занято действующим рисунком. Остальные погашены.</summary>
        private int usedPanels;

        /// <summary>Границы дырок на разбираемой полосе, м от центра стены. Поля, а не локальные: перестройка идёт в кадре подвоха.</summary>
        private readonly float[] bandFrom = new float[2];
        private readonly float[] bandTo = new float[2];
        private readonly float[] previousFrom = new float[2];
        private readonly float[] previousTo = new float[2];

        private Rigidbody body;

        [SerializeField] private WallCutout firstCutout;
        [SerializeField] private WallCutout secondCutout;

        private HoleInWallConfig config;
        private WallPattern pattern;
        private double startTime;
        private double trickTime;
        private float speed;
        private bool running;
        private bool trickShown;

        /// <summary>
        /// Подвох этой стены только что сработал: вырезы уже поменялись.
        ///
        /// Поднимается <b>на каждой машине сама</b>, без единого пакета: момент
        /// срабатывания объявлен вместе с рисунком, и до него каждая машина
        /// доходит по общим часам. Отсюда же и единственность — ровно один раз
        /// за проезд, там же, где перестраивается форма и моргает контур.
        ///
        /// Точка привязки арта: след подвоха в подфазе 4.4, свуш переворота
        /// в 4.5. Стену передаём первым доводом, чтобы подписчик знал, чья
        /// она, не заводя по обработчику на дорожку.
        /// </summary>
        public event Action<SweepingWall, WallTrick> TrickTriggered;

        /// <summary>Стена едет прямо сейчас.</summary>
        public bool Running => running;

        /// <summary>
        /// Скорость едущей стены, м/с. Ноль — стена стоит.
        ///
        /// Не состояние, а объявленное число: его задаёт расписание подъездов
        /// на старте (<see cref="HoleInWallConfig.WallSpeed"/>), и оно одно
        /// на всех машинах. Звук подфазы 4.5 гонит по нему тон и громкость
        /// гула — стены раунда разгоняются от первой к восьмой.
        /// </summary>
        public float Speed => running ? speed : 0f;

        /// <summary>Сколько вырезов на этой стене: два у пары, один у одиночки.</summary>
        public int CutoutCount => running ? pattern.CutoutCount : 0;

        /// <summary>
        /// Где передняя грань стены — та, что первой доходит до игрока.
        /// Считается от общих часов, а не читается из трансформа.
        /// </summary>
        public float FrontZ
        {
            get
            {
                if (config == null)
                {
                    return 0f;
                }

                if (!running)
                {
                    return config.WallStartZ;
                }

                float travelled = speed * (float)(NetworkClock.Now - startTime);
                return Mathf.Clamp(config.WallStartZ - travelled, config.WallExitZ, config.WallStartZ);
            }
        }

        /// <summary>Стена доехала до конца пути и больше никого не касается.</summary>
        public bool Finished => running && FrontZ <= config.WallExitZ + 0.001f;

        /// <summary>Подвох уже сработал: действуют новые вырезы.</summary>
        public bool TrickActive =>
            running && pattern.Trick != WallTrick.None && NetworkClock.Now >= trickTime;

        private void Awake()
        {
            SeedPanelPool();
            BuildBody();
            SetVisible(false);
        }

        /// <summary>
        /// Кинематическое тело на корне стены.
        ///
        /// ⚠️ Обязательно ровно с того момента, как у плит появились
        /// коллайдеры. Без <c>Rigidbody</c> PhysX считает стену статикой,
        /// а переставленный трансформ видит как телепорт: расталкивание
        /// получается рывками, игрока выбивает непредсказуемо, и каждый кадр
        /// перестраивается broadphase. Кинематическое тело двигают
        /// <c>MovePosition</c> — это перемещение с развёрткой, а интерполяция
        /// возвращает гладкость картинке при кадре чаще шага физики.
        /// </summary>
        private void BuildBody()
        {
            body = GetComponent<Rigidbody>();
            if (body == null)
            {
                body = gameObject.AddComponent<Rigidbody>();
            }

            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>
        /// Собрать пул плит из того, что построила арена.
        ///
        /// Плит в сцене пять — этого хватало прямоугольным вырезам. Вырез
        /// по силуэту режет стену полосами, и плит нужно больше: недостающие
        /// клонируются от первой, потому что материал, слой и настройки
        /// коллайдера у них одинаковые. Клонируем в рантайме, а не в сцене,
        /// чтобы не пересобирать одетую и запечённую арену ради геометрии.
        /// </summary>
        private void SeedPanelPool()
        {
            Transform[] fromScene = { panelLeft, panelMiddle, panelRight, lintelFirst, lintelSecond };
            for (int i = 0; i < fromScene.Length; i++)
            {
                if (fromScene[i] != null)
                {
                    panelPool.Add(fromScene[i]);
                    EnsureCollider(fromScene[i]);
                }
            }

            if (panelPool.Count == 0)
            {
                Debug.LogError($"{name}: у стены нет ни одной плиты — построить арену заново пунктом меню", this);
            }
        }

        /// <summary>
        /// Навесить и настроить коллайдер плиты.
        ///
        /// Делается кодом, а не в сцене: плит на каждой из четырёх стен теперь
        /// десятки, и настройка у них одна. Руками её пришлось бы повторять
        /// при каждой правке толщины стены.
        /// </summary>
        private static void EnsureCollider(Transform panel)
        {
            var box = panel.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = panel.gameObject.AddComponent<BoxCollider>();
            }

            // Плита — единичный куб, растянутый масштабом, поэтому размер
            // коллайдера единичный, а смещение считается в долях толщины.
            box.size = Vector3.one;
            float thickness = Mathf.Max(0.001f, panel.localScale.z);
            box.center = new Vector3(0f, 0f, ColliderRecess / thickness);
        }

        /// <summary>Взять следующую свободную плиту, доклонировав её, если пул кончился.</summary>
        private Transform TakePanel()
        {
            if (usedPanels < panelPool.Count)
            {
                return panelPool[usedPanels++];
            }

            Transform template = panelPool.Count > 0 ? panelPool[0] : null;
            if (template == null)
            {
                return null;
            }

            Transform clone = Instantiate(template.gameObject, template.parent).transform;
            clone.name = $"Panel_{panelPool.Count:00}";
            EnsureCollider(clone);
            panelPool.Add(clone);
            usedPanels++;
            return clone;
        }

        /// <summary>Зажечь или погасить столкновения у всех плит разом.</summary>
        private void SetCollidersEnabled(bool enabled)
        {
            for (int i = 0; i < panelPool.Count; i++)
            {
                if (panelPool[i] == null)
                {
                    continue;
                }

                var box = panelPool[i].GetComponent<BoxCollider>();
                if (box != null)
                {
                    box.enabled = enabled;
                }
            }
        }

        /// <summary>
        /// Снять с плит столкновения до конца прохода.
        ///
        /// Зовётся ровно в одном случае — когда вердикт по дорожке уже вынесен
        /// и он <b>«прошли»</b>. Пара стоит в вырезах, но у самого узкого
        /// выреза допуск шире физического зазора, и оставленный коллайдер
        /// толкнул бы прошедшего за успешный проход. Провалившимся плиты
        /// остаются: их стена и обязана ударить.
        /// </summary>
        public void DisableCollision() => SetCollidersEnabled(false);

        /// <summary>Привязать к дорожке. Зовётся один раз при старте раунда.</summary>
        public void Configure(HoleInWallConfig gameConfig)
        {
            config = gameConfig;
        }

        /// <summary>
        /// Пустить стену. Все три момента приходят в общих часах: их объявляет
        /// сервер один раз, и дальше каждая машина считает от них сама.
        /// </summary>
        /// <param name="wallPattern">Рисунок вместе с подвохом</param>
        /// <param name="wallStartTime">Момент старта в общих часах</param>
        /// <param name="wallSpeed">Скорость, м/с</param>
        /// <param name="wallTrickTime">Момент срабатывания подвоха в общих часах</param>
        public void Launch(WallPattern wallPattern, double wallStartTime, float wallSpeed, double wallTrickTime)
        {
            if (config == null)
            {
                Debug.LogError($"{name}: стена пущена без конфига — ехать не по чему", this);
                return;
            }

            pattern = wallPattern;
            startTime = wallStartTime;
            speed = Mathf.Max(0.01f, wallSpeed);
            trickTime = wallTrickTime;
            running = true;
            trickShown = false;

            // Сначала показываем всё, потом форма гасит лишние плиты: обратный
            // порядок зажёг бы обратно и те, что RebuildShape только что убрал.
            SetVisible(true);
            RebuildShape(false);

            // Новая стена — снова твёрдая: прошлая могла снять плиты вердиктом.
            SetCollidersEnabled(true);

            UpdateTransform(snap: true);
        }

        /// <summary>Убрать стену: доехала или раунд кончился.</summary>
        public void Retire()
        {
            running = false;
            trickShown = false;
            SetVisible(false);
        }

        /// <summary>
        /// Действующий вырез под этим номером. Учитывает подвох, если тот уже
        /// сработал: контроллер обязан сравнивать позу игрока с тем вырезом,
        /// который стоит на стене прямо сейчас.
        /// </summary>
        public bool TryGetCutout(int index, out HoleInWallPose pose, out float offset)
        {
            pose = HoleInWallPose.None;
            offset = 0f;

            if (!running || index < 0 || index >= pattern.CutoutCount)
            {
                return false;
            }

            ResolveCutout(index, TrickActive, out pose, out offset);
            return pose != HoleInWallPose.None;
        }

        /// <summary>
        /// Ход стены. В <c>FixedUpdate</c>, а не в <c>Update</c>: стена несёт
        /// коллайдеры, а физику проекта разрешено двигать только шагом физики
        /// (igruha/CLAUDE.md, раздел 2). Гладкость кадра даёт интерполяция
        /// тела, а не частота вызовов.
        ///
        /// Подвох переключается здесь же: он меняет размеры плит, то есть
        /// те же коллайдеры.
        /// </summary>
        private void FixedUpdate()
        {
            if (!running)
            {
                return;
            }

            bool trickActive = TrickActive;
            if (trickActive != trickShown)
            {
                RebuildShape(trickActive);
                BlinkCutouts();

                // Только на включении: обратно подвох не выключается, а сброс
                // отметки при Retire — это уже другая стена, и след ей не нужен.
                if (trickActive)
                {
                    TrickTriggered?.Invoke(this, pattern.Trick);
                }
            }

            UpdateTransform(snap: false);
        }

        /// <summary>
        /// Что за вырез действует под этим номером — единственная запись
        /// правила подвоха. Читают её и отрисовка, и вердикт, поэтому разойтись
        /// им негде.
        /// </summary>
        private void ResolveCutout(int index, bool trickActive, out HoleInWallPose pose, out float offset)
        {
            WallCutoutSpec spec = index == 0 ? pattern.First : pattern.Second;
            pose = spec.Pose;
            offset = spec.Offset;

            if (!trickActive)
            {
                return;
            }

            switch (pattern.Trick)
            {
                case WallTrick.Mirror:
                    // Зеркальный переворот: позы остаются, меняются места.
                    offset = -offset;
                    break;
                case WallTrick.Morph:
                    // Смена формы: места остаются, меняются позы.
                    pose = index == 0 ? pattern.MorphFirst : pattern.MorphSecond;
                    break;
            }
        }

        /// <summary>
        /// Поставить стену по расписанию. Позиция считается по формуле,
        /// а не накапливается: центр стены отстоит от передней грани
        /// на половину толщины.
        /// </summary>
        /// <param name="snap">
        /// Телепорт вместо хода. Нужен только на старте: <c>MovePosition</c>
        /// перемещает с развёрткой, и на старте оно протащило бы стену через
        /// всю арену, расталкивая всё по пути.
        /// </param>
        private void UpdateTransform(bool snap)
        {
            Vector3 local = transform.localPosition;
            local.z = FrontZ + config.WallThickness * 0.5f;

            if (snap || body == null)
            {
                transform.localPosition = local;
                return;
            }

            Transform parent = transform.parent;
            body.MovePosition(parent != null ? parent.TransformPoint(local) : local);
        }

        // ========== ГЕОМЕТРИЯ ==========

        /// <summary>
        /// Пересобрать плиты и контуры под действующий рисунок.
        ///
        /// Стена — это не дырявый меш, а набор плит вокруг дырок. Пока вырезы
        /// были прямоугольными, плит хватало пяти. Теперь дырка повторяет
        /// силуэт позы, поэтому стена режется горизонтальными полосами:
        /// на каждой полосе известно, докуда достаёт силуэт, и между дырками
        /// остаются куски сплошной стены. Соседние полосы с одинаковым
        /// пролётом склеиваются в одну плиту — иначе на ровном месте выходило
        /// бы под сотню кубов на стену.
        ///
        /// Ошибаемся всегда в большую сторону: пролёт полосы берётся самый
        /// широкий из попавших в неё (<c>TryWidestSpan</c>), а полоса,
        /// зацепившая верх выреза, считается дырявой целиком. Лишний сантиметр
        /// дырки не видно, а недостающий — это застрявший в стене игрок.
        /// </summary>
        private void RebuildShape(bool trickActive)
        {
            trickShown = trickActive;

            ResolveCutout(0, trickActive, out HoleInWallPose firstPose, out float firstOffset);
            Vector2 firstSize = config.SilhouetteSize(firstPose);

            bool hasSecond = pattern.CutoutCount > 1;
            HoleInWallPose secondPose = HoleInWallPose.None;
            float secondOffset = 0f;
            Vector2 secondSize = Vector2.zero;

            if (hasSecond)
            {
                ResolveCutout(1, trickActive, out secondPose, out secondOffset);
                secondSize = config.SilhouetteSize(secondPose);
            }

            // Зеркальный переворот меняет вырезы местами по X, поэтому «левый»
            // и «правый» пересортировываются каждый раз, а не берутся по номеру.
            bool swap = hasSecond && secondOffset < firstOffset;
            HoleInWallPose leftPose = swap ? secondPose : firstPose;
            float leftOffset = swap ? secondOffset : firstOffset;
            Vector2 leftSize = swap ? secondSize : firstSize;
            HoleInWallPose rightPose = swap ? firstPose : secondPose;
            float rightOffset = swap ? firstOffset : secondOffset;
            Vector2 rightSize = swap ? firstSize : secondSize;

            usedPanels = 0;
            float thickness = config.WallThickness;
            float bandHeight = config.WallHeight / ShapeBands;
            int bandStart = 0;

            for (int band = 0; band <= ShapeBands; band++)
            {
                if (band < ShapeBands)
                {
                    ResolveBand(band, bandHeight, leftPose, leftOffset, leftSize, 0);
                    ResolveBand(band, bandHeight, hasSecond ? rightPose : HoleInWallPose.None,
                        rightOffset, rightSize, 1);
                }

                bool last = band == ShapeBands;
                if (!last && band > bandStart && SameAsPrevious())
                {
                    continue;
                }

                if (band > bandStart)
                {
                    EmitBand(bandStart * bandHeight, band * bandHeight, thickness);
                }

                bandStart = band;
                previousFrom[0] = bandFrom[0];
                previousTo[0] = bandTo[0];
                previousFrom[1] = bandFrom[1];
                previousTo[1] = bandTo[1];
            }

            HidePanelsFrom(usedPanels);

            firstCutout.Apply(config, firstPose, firstOffset, thickness);

            if (hasSecond)
            {
                secondCutout.Apply(config, secondPose, secondOffset, thickness);
            }
            else
            {
                secondCutout.Hide();
            }
        }

        /// <summary>
        /// Границы дырки на полосе. Пустая дырка — <c>to</c> не больше
        /// <c>from</c>: так же читается и полоса выше выреза, и второй вырез
        /// у одиночки.
        /// </summary>
        private void ResolveBand(int band, float bandHeight, HoleInWallPose pose, float offset, Vector2 size, int slot)
        {
            bandFrom[slot] = 0f;
            bandTo[slot] = 0f;

            if (pose == HoleInWallPose.None || size.x <= 0f || size.y <= 0f)
            {
                return;
            }

            float from = band * bandHeight;
            if (from >= size.y)
            {
                return;
            }

            float to = (band + 1) * bandHeight;
            float halfWidth = size.x * 0.5f;
            HoleInWallPoseShapes shapes = config.PoseShapes;

            if (shapes == null || !shapes.TryWidestSpan(pose, from / size.y, to / size.y,
                    out float spanLeft, out float spanRight))
            {
                // Ассета формы нет — вырез остаётся прямоугольным, как до арта.
                bandFrom[slot] = offset - halfWidth;
                bandTo[slot] = offset + halfWidth;
                return;
            }

            bandFrom[slot] = offset + spanLeft * halfWidth;
            bandTo[slot] = offset + spanRight * halfWidth;
        }

        /// <summary>Пролёты на этой полосе совпали с предыдущей — плиту можно не резать.</summary>
        private bool SameAsPrevious()
        {
            const float Epsilon = 0.001f;
            for (int i = 0; i < bandFrom.Length; i++)
            {
                if (Mathf.Abs(bandFrom[i] - previousFrom[i]) > Epsilon ||
                    Mathf.Abs(bandTo[i] - previousTo[i]) > Epsilon)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Поставить сплошные куски стены на полосе высот: слева от первой
        /// дырки, между дырками и справа от второй.
        /// </summary>
        private void EmitBand(float fromY, float toY, float thickness)
        {
            float halfWall = config.WallWidth * 0.5f;
            float cursor = -halfWall;

            for (int i = 0; i < previousFrom.Length; i++)
            {
                if (previousTo[i] <= previousFrom[i])
                {
                    continue;
                }

                SetPanel(TakePanel(), cursor, previousFrom[i], fromY, toY, thickness);
                cursor = Mathf.Max(cursor, previousTo[i]);
            }

            SetPanel(TakePanel(), cursor, halfWall, fromY, toY, thickness);
        }

        /// <summary>Погасить плиты, не занятые действующим рисунком.</summary>
        private void HidePanelsFrom(int first)
        {
            for (int i = first; i < panelPool.Count; i++)
            {
                if (panelPool[i] != null)
                {
                    panelPool[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>Поставить плиту от одной границы по X до другой и от одной высоты до другой.</summary>
        private static void SetPanel(Transform panel, float fromX, float toX, float fromY, float toY, float thickness)
        {
            if (panel == null)
            {
                return;
            }

            float width = toX - fromX;
            float height = toY - fromY;

            // Вырез, упёршийся в край стены, оставляет плиту нулевой ширины —
            // такую просто гасим: масштаб в ноль даёт вывернутый меш.
            if (width <= 0.001f || height <= 0.001f)
            {
                panel.gameObject.SetActive(false);
                return;
            }

            panel.gameObject.SetActive(true);
            panel.localPosition = new Vector3((fromX + toX) * 0.5f, (fromY + toY) * 0.5f, 0f);
            panel.localScale = new Vector3(width, height, thickness);
        }

        private void BlinkCutouts()
        {
            float seconds = pattern.Trick == WallTrick.Mirror ? config.MirrorLead : config.MorphLead;
            firstCutout.Blink(seconds);

            if (pattern.CutoutCount > 1)
            {
                secondCutout.Blink(seconds);
            }
        }

        private void SetVisible(bool visible)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                transform.GetChild(i).gameObject.SetActive(visible);
            }
        }
    }
}
