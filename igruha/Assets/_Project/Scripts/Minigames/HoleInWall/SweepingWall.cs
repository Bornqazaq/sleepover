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
    ///
    /// <b>Видит игрок меш, а упирается в коробки.</b> Полотно с вырезом по
    /// контуру позы собирает <see cref="WallSurface"/> одним мешем, а плиты
    /// из сцены остались коробками столкновений с погашенными рендерерами.
    /// Так сделано намеренно: точность физики здесь ничего не решает —
    /// коллайдер и так утоплен, и прошедшим он снимается, — а невыпуклый
    /// <c>MeshCollider</c> на кинематическом теле стоил бы куда дороже
    /// любой выгоды.
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
        /// На сколько горизонтальных полос режется <b>коллизия</b> стены.
        ///
        /// Картинки это больше не касается: полотно режется по настоящей
        /// ломаной. Полосы остались только под коробки столкновений, где
        /// ступенька невидима, и ошибаться ими разрешено лишь в большую
        /// сторону — лишний сантиметр дырки не заметен, недостающий это
        /// застрявший в стене игрок.
        ///
        /// 24 полосы на 3.6 м — это 15 см на полосу. Число проверено прогоном
        /// и оставлено как есть: с погашенными рендерерами плит их количество
        /// перестало стоить кадра.
        /// </summary>
        private const int ShapeBands = 24;

        /// <summary>Полотно стены: меш с вырезами по контуру позы.</summary>
        private readonly WallSurface surface = new WallSurface();

        private Mesh surfaceMesh;

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

        /// <summary>Формы вырезов этой дорожки: контур на каждую позу под её состав.</summary>
        private CutoutShapes shapes;

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
            BuildSurface();
            BuildBody();
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (surfaceMesh != null)
            {
                Destroy(surfaceMesh);
            }
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
        /// Плиты остались от прямоугольных вырезов, и в сцене их пять.
        /// Теперь это <b>коробки столкновений</b>, а не картинка: полос
        /// коллизии до двух десятков, недостающие плиты клонируются от первой.
        /// Клонируем в рантайме, а не в сцене, чтобы не пересобирать одетую
        /// и запечённую арену ради геометрии.
        /// </summary>
        private void SeedPanelPool()
        {
            Transform[] fromScene = { panelLeft, panelMiddle, panelRight, lintelFirst, lintelSecond };
            for (int i = 0; i < fromScene.Length; i++)
            {
                if (fromScene[i] != null)
                {
                    panelPool.Add(fromScene[i]);
                    PreparePanel(fromScene[i]);
                }
            }

            if (panelPool.Count == 0)
            {
                Debug.LogError($"{name}: у стены нет ни одной плиты — построить арену заново пунктом меню", this);
            }
        }

        /// <summary>
        /// Завести полотно стены — меш, в котором и живут вырезы по контуру.
        ///
        /// Материал, тени и слой снимаются с плиты из сцены, а не задаются
        /// здесь: полотно обязано выглядеть ровно так же, как выглядела стена
        /// после арт-фазы, а сцена одета и запечена подфазами 4.1–4.6.
        /// </summary>
        private void BuildSurface()
        {
            var holder = new GameObject("Surface");
            holder.transform.SetParent(transform, false);

            surfaceMesh = new Mesh { name = "WallSurface" };
            surfaceMesh.MarkDynamic();
            holder.AddComponent<MeshFilter>().sharedMesh = surfaceMesh;
            var surfaceRenderer = holder.AddComponent<MeshRenderer>();

            Renderer sample = panelPool.Count > 0 && panelPool[0] != null
                ? panelPool[0].GetComponent<Renderer>()
                : null;

            if (sample == null)
            {
                Debug.LogError($"{name}: у плиты стены нет рендерера — полотну неоткуда взять материал", this);
                return;
            }

            holder.layer = panelPool[0].gameObject.layer;
            surfaceRenderer.sharedMaterial = sample.sharedMaterial;
            surfaceRenderer.shadowCastingMode = sample.shadowCastingMode;
            surfaceRenderer.receiveShadows = sample.receiveShadows;
        }

        /// <summary>
        /// Настроить плиту: коллайдер по месту, рендерер погашен.
        ///
        /// Рендерер гасится, а не удаляется вместе с плитой: плиты пришли
        /// из одетой сцены, и вернуть их обратно должно быть можно снятием
        /// одной строки. Показывать их нельзя — прямоугольные плиты и есть
        /// та самая лесенка вместо контура.
        /// </summary>
        private static void PreparePanel(Transform panel)
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

            var panelRenderer = panel.GetComponent<Renderer>();
            if (panelRenderer != null)
            {
                panelRenderer.enabled = false;
            }
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
            PreparePanel(clone);
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

        /// <summary>
        /// Привязать к дорожке. Зовётся один раз при старте раунда.
        ///
        /// Формы вырезов приходят вместе с конфигом, потому что они у каждой
        /// дорожки свои: вырез режется под состав, который на ней стоит.
        /// </summary>
        public void Configure(HoleInWallConfig gameConfig, CutoutShapes cutoutShapes)
        {
            config = gameConfig;
            shapes = cutoutShapes;
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
        /// Пересобрать полотно, коробки столкновений и контуры под действующий
        /// рисунок.
        ///
        /// Картинка и физика строятся по одному и тому же контуру, но разными
        /// способами. Полотно — меш: прямоугольник стены с вырезами по
        /// настоящей ломаной позы. Столкновения — коробки: стена режется
        /// на <see cref="ShapeBands"/> полос, на каждой известен самый широкий
        /// пролёт выреза, и между дырками ставятся плиты. Соседние полосы
        /// с одинаковым пролётом склеиваются.
        ///
        /// Ошибаемся коробками всегда в большую сторону: пролёт полосы берётся
        /// самый широкий из попавших в неё, а полоса, зацепившая верх выреза,
        /// считается дырявой целиком. Лишний сантиметр дырки не виден вовсе —
        /// её рисует меш, — а недостающий это застрявший в стене игрок.
        /// </summary>
        private void RebuildShape(bool trickActive)
        {
            trickShown = trickActive;

            ResolveCutout(0, trickActive, out HoleInWallPose firstPose, out float firstOffset);

            bool hasSecond = pattern.CutoutCount > 1;
            HoleInWallPose secondPose = HoleInWallPose.None;
            float secondOffset = 0f;

            if (hasSecond)
            {
                ResolveCutout(1, trickActive, out secondPose, out secondOffset);
            }

            // Зеркальный переворот меняет вырезы местами по X, поэтому «левый»
            // и «правый» пересортировываются каждый раз, а не берутся по номеру.
            bool swap = hasSecond && secondOffset < firstOffset;
            HoleInWallPose leftPose = swap ? secondPose : firstPose;
            float leftOffset = swap ? secondOffset : firstOffset;
            HoleInWallPose rightPose = swap ? firstPose : secondPose;
            float rightOffset = swap ? firstOffset : secondOffset;

            float thickness = config.WallThickness;
            BuildSurfaceMesh(leftPose, leftOffset, hasSecond ? rightPose : HoleInWallPose.None, rightOffset, thickness);
            BuildColliders(leftPose, leftOffset, hasSecond ? rightPose : HoleInWallPose.None, rightOffset, thickness);

            firstCutout.Apply(config, shapes, firstPose, firstOffset, thickness);

            if (hasSecond)
            {
                secondCutout.Apply(config, shapes, secondPose, secondOffset, thickness);
            }
            else
            {
                secondCutout.Hide();
            }
        }

        /// <summary>Сложить полотно из прямоугольника стены и одной-двух ломаных вырезов.</summary>
        private void BuildSurfaceMesh(HoleInWallPose leftPose, float leftOffset,
            HoleInWallPose rightPose, float rightOffset, float thickness)
        {
            if (surfaceMesh == null)
            {
                return;
            }

            surface.Build(surfaceMesh, config.WallWidth, config.WallHeight, thickness,
                new WallSurface.Cutout(OutlineOf(leftPose), leftOffset),
                new WallSurface.Cutout(OutlineOf(rightPose), rightOffset));
        }

        /// <summary>Ломаная выреза под эту позу. Пусто — выреза нет: у одиночки второго не бывает.</summary>
        private Vector2[] OutlineOf(HoleInWallPose pose) =>
            pose == HoleInWallPose.None || shapes == null ? null : shapes.Outline(pose);

        /// <summary>
        /// Расставить коробки столкновений по полосам высоты. Плиты невидимы:
        /// стену рисует полотно, а они только держат тех, кто не влез.
        /// </summary>
        private void BuildColliders(HoleInWallPose leftPose, float leftOffset,
            HoleInWallPose rightPose, float rightOffset, float thickness)
        {
            usedPanels = 0;
            float bandHeight = config.WallHeight / ShapeBands;
            int bandStart = 0;

            for (int band = 0; band <= ShapeBands; band++)
            {
                if (band < ShapeBands)
                {
                    ResolveBand(band, bandHeight, leftPose, leftOffset, 0);
                    ResolveBand(band, bandHeight, rightPose, rightOffset, 1);
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
        }

        /// <summary>
        /// Границы дырки на полосе. Пустая дырка — <c>to</c> не больше
        /// <c>from</c>: так же читается и полоса выше выреза, и второй вырез
        /// у одиночки.
        /// </summary>
        private void ResolveBand(int band, float bandHeight, HoleInWallPose pose, float offset, int slot)
        {
            bandFrom[slot] = 0f;
            bandTo[slot] = 0f;

            if (pose == HoleInWallPose.None || shapes == null)
            {
                return;
            }

            if (!shapes.TrySpan(pose, band * bandHeight, (band + 1) * bandHeight,
                    out float spanLeft, out float spanRight))
            {
                return;
            }

            bandFrom[slot] = offset + spanLeft;
            bandTo[slot] = offset + spanRight;
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
        /// Поставить коробки столкновений на полосе высот: слева от первой
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

        /// <summary>Погасить коробки, не занятые действующим рисунком.</summary>
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

        /// <summary>Поставить коробку от одной границы по X до другой и от одной высоты до другой.</summary>
        private static void SetPanel(Transform panel, float fromX, float toX, float fromY, float toY, float thickness)
        {
            if (panel == null)
            {
                return;
            }

            float width = toX - fromX;
            float height = toY - fromY;

            // Вырез, упёршийся в край стены, оставляет коробку нулевой
            // ширины — такую просто гасим: нулевой масштаб на коллайдере
            // PhysX не любит.
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
