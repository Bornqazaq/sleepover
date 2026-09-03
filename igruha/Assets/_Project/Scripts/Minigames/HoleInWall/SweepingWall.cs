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
    /// <b>У стены нет коллайдеров, и это решение, а не экономия.</b> Проверка
    /// в игре дискретная: сравниваются номер позы и одно число по горизонтали,
    /// а габариты капсулы не участвуют вовсе (спека 5.3). Сплошная панель
    /// вернула бы их обратно — краем выреза она толкала бы вбок того, кто стоит
    /// верно, но чуть шире прочих, и толстый персонаж перестал бы проходить
    /// там, где проходит тонкий. Непроходимость обеспечивают правила: прыжок
    /// и промах мимо выреза — это провал, а перелезть стену высотой 5 ШП
    /// прыжком на 2.3 ШП всё равно нельзя.
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

        private Collider[] panelColliders = System.Array.Empty<Collider>();
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
            BuildPanelColliders();
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
        /// Навесить и настроить коллайдеры плит.
        ///
        /// Делается кодом, а не в сцене: плит пять на каждой из четырёх стен,
        /// и настройка у них одна. Руками её пришлось бы повторить двадцать
        /// раз и держать в синхроне при каждой правке толщины.
        /// </summary>
        private void BuildPanelColliders()
        {
            Transform[] panels = { panelLeft, panelMiddle, panelRight, lintelFirst, lintelSecond };
            var found = new System.Collections.Generic.List<Collider>(panels.Length);

            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] == null)
                {
                    continue;
                }

                var box = panels[i].GetComponent<BoxCollider>();
                if (box == null)
                {
                    box = panels[i].gameObject.AddComponent<BoxCollider>();
                }

                // Плита — единичный куб, растянутый масштабом, поэтому размер
                // коллайдера единичный, а смещение считается в долях толщины.
                box.size = Vector3.one;
                float thickness = Mathf.Max(0.001f, panels[i].localScale.z);
                box.center = new Vector3(0f, 0f, ColliderRecess / thickness);

                found.Add(box);
            }

            panelColliders = found.ToArray();
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
        public void DisableCollision()
        {
            for (int i = 0; i < panelColliders.Length; i++)
            {
                if (panelColliders[i] != null)
                {
                    panelColliders[i].enabled = false;
                }
            }
        }

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
            for (int i = 0; i < panelColliders.Length; i++)
            {
                if (panelColliders[i] != null)
                {
                    panelColliders[i].enabled = true;
                }
            }

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
        /// Пересобрать панели и контуры под действующий рисунок.
        ///
        /// Стена — это не дырявый меш, а пять плит вокруг дырок: две-три стойки
        /// во всю высоту и перемычки над вырезами. На каркасе этого достаточно,
        /// а в фазе 4 на их место приезжает готовая панель с отверстиями.
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
            float leftOffset = swap ? secondOffset : firstOffset;
            Vector2 leftSize = swap ? secondSize : firstSize;
            float rightOffset = swap ? firstOffset : secondOffset;
            Vector2 rightSize = swap ? firstSize : secondSize;

            float halfWall = config.WallWidth * 0.5f;
            float height = config.WallHeight;
            float thickness = config.WallThickness;

            if (hasSecond)
            {
                SetPanel(panelLeft, -halfWall, leftOffset - leftSize.x * 0.5f, 0f, height, thickness);
                SetPanel(panelMiddle, leftOffset + leftSize.x * 0.5f, rightOffset - rightSize.x * 0.5f, 0f, height, thickness);
                SetPanel(panelRight, rightOffset + rightSize.x * 0.5f, halfWall, 0f, height, thickness);
                SetPanel(lintelFirst, leftOffset - leftSize.x * 0.5f, leftOffset + leftSize.x * 0.5f, leftSize.y, height, thickness);
                SetPanel(lintelSecond, rightOffset - rightSize.x * 0.5f, rightOffset + rightSize.x * 0.5f, rightSize.y, height, thickness);
            }
            else
            {
                SetPanel(panelLeft, -halfWall, firstOffset - firstSize.x * 0.5f, 0f, height, thickness);
                SetPanel(panelMiddle, 0f, 0f, 0f, 0f, thickness);
                SetPanel(panelRight, firstOffset + firstSize.x * 0.5f, halfWall, 0f, height, thickness);
                SetPanel(lintelFirst, firstOffset - firstSize.x * 0.5f, firstOffset + firstSize.x * 0.5f, firstSize.y, height, thickness);
                SetPanel(lintelSecond, 0f, 0f, 0f, 0f, thickness);
            }

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
