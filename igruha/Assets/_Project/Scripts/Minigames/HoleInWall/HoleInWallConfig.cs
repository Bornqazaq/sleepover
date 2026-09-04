using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Все числа «Дырки в стене» одним ассетом (спека, раздел 8): геймдизайнер
    /// крутит их без программиста. По этим же числам строится арена
    /// (<c>HoleInWallArenaBuilder</c>) — один источник истины и для геометрии,
    /// и для правил.
    ///
    /// Размеры задаются в <b>ШП — ширинах персонажа</b> (1 ШП = 0.72 юнита,
    /// диаметр капсулы Player.prefab), а наружу отдаются уже в метрах.
    /// Тот же пересчёт, что в «Плачущих ангелах» и «Рейсе на память».
    /// </summary>
    /// <remarks>
    /// <b>Глубина арены — вычисляемое свойство, а не поле</b> (см. <see cref="ArenaDepth"/>).
    /// В спеке стоит 40 ШП, и это число не сходится с собственным содержимым:
    /// путь стены 30 ШП плюс платформа 5 ШП плюс запас камере 8 ШП дают 42.5.
    /// Записанное руками, оно разъедется с ареной молча при первой же правке
    /// пути стены — ровно то, на чём сорвался LDD «Рейса на память».
    ///
    /// <b>Расписание тоже считается, а не хранится дважды.</b> В спеке три
    /// таблицы — подъезды, старты и удары, — но независимы в них только
    /// подъезды и пауза: остальное выводится. Хранить все три значит завести
    /// три источника правды на одно расписание.
    /// </remarks>
    [CreateAssetMenu(menuName = "Igruha/Hole In Wall Config", fileName = "HoleInWallConfig")]
    public sealed class HoleInWallConfig : ScriptableObject
    {
        /// <summary>Сколько поз в игре. Четыре — это сама игра, а не настройка.</summary>
        public const int PoseCount = 4;

        /// <summary>Габариты одного силуэта выреза, ШП. Таблица 8.3 спеки.</summary>
        [System.Serializable]
        public struct PoseSilhouette
        {
            [Tooltip("Ширина выреза, ШП")]
            public float Width;

            [Tooltip("Высота выреза от пола платформы, ШП")]
            public float Height;
        }

        [Header("Масштаб")]
        [Tooltip("Сколько юнитов в одной ширине персонажа. 0.72 = диаметр капсулы Player.prefab")]
        [SerializeField] private float unitsPerWidth = 0.72f;

        [Header("Арена, ШП")]
        [Tooltip("Сколько дорожек строит арена. Играют не все: лишние выключает контроллер по составу")]
        [SerializeField] private int trackCount = 4;
        [Tooltip("Ширина платформы = ширина дорожки = ширина стены")]
        [SerializeField] private float platformWidth = 12f;
        [Tooltip("Глубина платформы. Узкая намеренно: отступать некуда, отойти от стены нельзя")]
        [SerializeField] private float platformDepth = 5f;
        [Tooltip("Толщина плиты платформы — только на вид, игрок ходит по верхней грани")]
        [SerializeField] private float platformThickness = 1f;
        [Tooltip("Промежуток между дорожками. Через него соседа не достать — в этом и смысл")]
        [SerializeField] private float trackGap = 3f;
        [Tooltip("Запас по краям арены за крайними дорожками")]
        [SerializeField] private float sideMargin = 1.5f;
        [Tooltip("Высота платформы над водой")]
        [SerializeField] private float platformHeightOverWater = 4f;
        [Tooltip("Глубина бассейна ниже уровня воды")]
        [SerializeField] private float poolDepth = 3f;
        [Tooltip("Сколько бассейна оставить ЗА платформой под отход камеры. Радиус орбиты PartyCameraRig 4.20 м = 5.8 ШП, меньше ставить нельзя (igruha/CLAUDE.md, 2a)")]
        [SerializeField] private float cameraClearance = 8f;
        [Tooltip("Запас арены за стартовой позицией стены")]
        [SerializeField] private float farMargin = 2f;

        [Header("Стена, ШП")]
        [Tooltip("Путь стены от старта до линии проверки. Он же задаёт скорость: путь делится на подъезд")]
        [SerializeField] private float wallTravel = 30f;
        [Tooltip("Высота стены. 5 ШП расчётные: прыжок при jumpSpeed 8.4 поднимает на 1.64 м, верх стены на 3.6 — двукратный запас")]
        [SerializeField] private float wallHeight = 5f;
        [Tooltip("Толщина стены")]
        [SerializeField] private float wallThickness = 1f;
        [Tooltip("Сколько стена проезжает ЗА линию проверки, прежде чем исчезнуть")]
        [SerializeField] private float wallExitDistance = 8f;
        [Tooltip("Минимальная перемычка между двумя вырезами. Ниже неё вырезы сливаются в одну дыру и два силуэта перестают читаться")]
        [SerializeField] private float cutoutBridge = 0.4f;

        [Header("Проверка попадания, ШП")]
        [Tooltip("КРИТИЧЕСКИЙ ДЛЯ ПЛЕЙТЕСТА. На сколько можно промахнуться мимо центра выреза по горизонтали")]
        [SerializeField] private float hitTolerance = 0.8f;
        [Tooltip("На сколько игрок может быть выше пола платформы и всё ещё считаться стоящим. Прыжок поднимает на 2.3 ШП, так что в прыжке проверка провалится")]
        [SerializeField] private float groundedTolerance = 0.4f;
        [Tooltip("На сколько разводит пару от центра платформы на старте")]
        [SerializeField] private float pairSpread = 2f;

        [Header("Разнос вырезов, ШП")]
        [Tooltip("Минимальный разнос центров вырезов")]
        [SerializeField] private float spreadMin = 2f;
        [Tooltip("Максимальный разнос. 5, а не 8 как в LDD: при 8 пара с тросом 6 ШП не дотягивается до своих вырезов даже идеально")]
        [SerializeField] private float spreadMax = 5f;
        [Tooltip("Потолок разноса на простых стенах: вырезы стоят рядом")]
        [SerializeField] private float earlySpreadMax = 3f;
        [Tooltip("Пол разноса на стенах сразу после простых: вырезы разнесены")]
        [SerializeField] private float wideSpreadMin = 4f;
        [Tooltip("Сколько первых стен считаются простыми: только позы 1–3 и узкий разнос")]
        [SerializeField] private int easyWallCount = 2;
        [Tooltip("Сколько стен подряд после простых идут с широким разносом")]
        [SerializeField] private int wideWallCount = 2;

        [Header("Силуэты вырезов, ШП — порядок соответствует позам 1…4")]
        [Tooltip("ЗАПАСНОЙ ВАРИАНТ. Настоящий размер выреза берётся из ассета силуэтов и считается под состав дорожки. Эти числа работают, только если ассета нет или персонаж не опознан: замерено по всем восьми плюс запас 0.3 ШП")]
        [SerializeField]
        private PoseSilhouette[] poseSilhouettes =
        {
            new PoseSilhouette { Width = 1.85f, Height = 3.6f },
            new PoseSilhouette { Width = 3.4f, Height = 2.9f },
            new PoseSilhouette { Width = 1.9f, Height = 2.25f },
            new PoseSilhouette { Width = 2.85f, Height = 3.15f }
        };

        [Tooltip("Контуры вырезов: состав дорожки × поза, в метрах. Генерируется пунктом меню Igruha/Дырка в стене/Испечь силуэты вырезов")]
        [SerializeField] private HoleInWallSilhouettes silhouettes;

        [Header("Трос")]
        [Tooltip("ВЫСОКИЙ ПРИОРИТЕТ ПЛЕЙТЕСТА. Максимальная длина троса, ШП")]
        [SerializeField] private float tetherLength = 6f;
        [Tooltip("На каком перетяге (ШП) притяжение выходит на полную силу")]
        [SerializeField] private float tetherRamp = 1f;
        [Tooltip("Максимальное ускорение притяжения, м/с²")]
        [SerializeField] private float tetherPullAcceleration = 25f;
        [Tooltip("Жёсткий предел сверх длины, ШП. Дальше позиция стопорится")]
        [SerializeField] private float tetherHardLimit = 1.5f;

        [Header("Расписание стен, с")]
        [Tooltip("Подъезд каждой стены. КРИТИЧЕСКИЙ ДЛЯ ПЛЕЙТЕСТА. Длина массива = число стен в раунде")]
        [SerializeField] private float[] wallApproachSeconds = { 6f, 6f, 5f, 5f, 4f, 4f, 3.5f, 3f };
        [Tooltip("Пауза между ударом одной стены и стартом следующей")]
        [SerializeField] private float pauseAfterHit = 2f;
        [Tooltip("КРИТИЧЕСКИЙ ДЛЯ ПЛЕЙТЕСТА. Сколько секунд игрок обязан простоять на платформе перед ударом. По этому числу возвращают из воды: раньше — можно, позже — нельзя")]
        [SerializeField] private float poseWindowSeconds = 2.5f;
        [Tooltip("Насколько быстрее едет стена на дорожке одиночки. Момент удара при этом общий: стена просто стартует позже")]
        [SerializeField] private float soloSpeedBonus = 0.25f;
        [Tooltip("Сколько секунд после последнего удара держать раунд, чтобы провалившиеся успели вылезти из воды")]
        [SerializeField] private float roundEndDelay = 5f;

        [Header("Подвохи")]
        [Tooltip("Номер стены с зеркальным переворотом, с 1. Ноль — переворота нет вовсе")]
        [SerializeField] private int mirrorWall = 7;
        [Tooltip("Номер стены со сменой формы, с 1. Ноль — смены нет вовсе")]
        [SerializeField] private int morphWall = 8;
        [Tooltip("За сколько секунд до удара срабатывает зеркальный переворот")]
        [SerializeField] private float mirrorLead = 1f;
        [Tooltip("За сколько секунд до удара срабатывает смена формы")]
        [SerializeField] private float morphLead = 1.5f;
        [Tooltip("За сколько секунд до удара звучит сигнал. Единственная подсказка в игре")]
        [SerializeField] private float warningLead = 1f;

        [Header("Провал")]
        [Tooltip("Импульс, которым стена сметает с платформы. Должен превышать порог нокдауна (5 м/с при массе 2, то есть 10)")]
        [SerializeField] private float sweepImpulse = 16f;
        [Tooltip("Доля импульса вверх — чтобы сметённый улетал, а не проезжал по платформе")]
        [Range(0f, 1f)]
        [SerializeField] private float sweepUpward = 0.35f;
        [Tooltip("Полёт и падение в воду")]
        [SerializeField] private float fallSeconds = 1.5f;
        [Tooltip("Насколько быстрее стены обязан лететь сметённый, м/с. Меньше — стена догоняет его и волочёт перед собой, и он оказывается внутри плиты")]
        [SerializeField] private float sweepClearanceSpeed = 2.5f;
        [Tooltip("Барахтанье в воде до автовозврата на платформу")]
        [SerializeField] private float splashSeconds = 3f;
        [Tooltip("Минимум барахтанья, когда расписание требует вернуть раньше срока. Ниже него провал перестаёт читаться: игрок вылетает из воды тем же кадром, каким в неё вошёл")]
        [SerializeField] private float minSplashSeconds = 1f;

        // ========== МАСШТАБ ==========

        /// <summary>Сколько юнитов в одной ширине персонажа.</summary>
        public float UnitsPerWidth => unitsPerWidth;

        // ========== АРЕНА, МЕТРЫ ==========

        public int TrackCount => Mathf.Max(1, trackCount);
        public float PlatformWidth => platformWidth * unitsPerWidth;
        public float PlatformDepth => platformDepth * unitsPerWidth;
        public float PlatformThickness => platformThickness * unitsPerWidth;
        public float TrackGap => trackGap * unitsPerWidth;
        public float SideMargin => sideMargin * unitsPerWidth;
        public float PlatformHeightOverWater => platformHeightOverWater * unitsPerWidth;
        public float PoolDepth => poolDepth * unitsPerWidth;
        public float CameraClearance => cameraClearance * unitsPerWidth;
        public float FarMargin => farMargin * unitsPerWidth;

        /// <summary>Шаг между центрами соседних дорожек: платформа плюс промежуток.</summary>
        public float TrackPitch => PlatformWidth + TrackGap;

        /// <summary>Ширина арены целиком: дорожки, промежутки и запас по краям.</summary>
        public float ArenaWidth =>
            TrackCount * PlatformWidth + (TrackCount - 1) * TrackGap + 2f * SideMargin;

        /// <summary>
        /// Глубина арены целиком. Считается, а не задаётся: запас за стартом
        /// стены, путь стены, сама платформа и отход камеры за ней.
        /// </summary>
        public float ArenaDepth => ArenaFarZ - ArenaNearZ;

        /// <summary>Центр дорожки по X. Начало координат — центр арены.</summary>
        public float TrackCenterX(int track) => (track - (TrackCount - 1) * 0.5f) * TrackPitch;

        /// <summary>
        /// Линия проверки и центр платформы по Z. Ноль — и это не совпадение:
        /// игрок стоит здесь, стена доходит сюда, отсюда же отмеряется её путь.
        /// </summary>
        public float CheckLineZ => 0f;

        /// <summary>Верхняя грань платформы: пол, по которому ходят. Ноль по той же причине.</summary>
        public float PlatformSurfaceY => 0f;

        /// <summary>Уровень воды.</summary>
        public float WaterSurfaceY => PlatformSurfaceY - PlatformHeightOverWater;

        /// <summary>Дно бассейна.</summary>
        public float PoolBottomY => WaterSurfaceY - PoolDepth;

        /// <summary>Дальний край платформы — тот, с которого приходит стена.</summary>
        public float PlatformFrontZ => CheckLineZ + PlatformDepth * 0.5f;

        /// <summary>Ближний край платформы — за ним только вода и камера.</summary>
        public float PlatformBackZ => CheckLineZ - PlatformDepth * 0.5f;

        public float ArenaFarZ => WallStartZ + FarMargin;
        public float ArenaNearZ => PlatformBackZ - CameraClearance;

        // ========== СТЕНА, МЕТРЫ ==========

        public float WallTravel => wallTravel * unitsPerWidth;
        public float WallWidth => PlatformWidth;
        public float WallHeight => wallHeight * unitsPerWidth;
        public float WallThickness => wallThickness * unitsPerWidth;
        public float CutoutBridge => cutoutBridge * unitsPerWidth;

        /// <summary>Где стоит передняя грань стены в момент старта.</summary>
        public float WallStartZ => CheckLineZ + WallTravel;

        /// <summary>Где стена исчезает, уехав за спину игрокам.</summary>
        public float WallExitZ => CheckLineZ - wallExitDistance * unitsPerWidth;

        // ========== ПРОВЕРКА ==========

        /// <summary>Допуск по горизонтали от центра выреза, м.</summary>
        public float HitTolerance => hitTolerance * unitsPerWidth;

        /// <summary>На сколько игрок может подняться над полом и всё ещё считаться стоящим, м.</summary>
        public float GroundedTolerance => groundedTolerance * unitsPerWidth;

        /// <summary>На сколько разводит пару от центра платформы на старте, м.</summary>
        public float PairSpread => pairSpread * unitsPerWidth;

        // ========== ВЫРЕЗЫ ==========

        public float SpreadMin => spreadMin * unitsPerWidth;
        public float SpreadMax => spreadMax * unitsPerWidth;
        public float EarlySpreadMax => earlySpreadMax * unitsPerWidth;
        public float WideSpreadMin => wideSpreadMin * unitsPerWidth;
        public int EasyWallCount => Mathf.Max(0, easyWallCount);
        public int WideWallCount => Mathf.Max(0, wideWallCount);

        /// <summary>
        /// Контуры вырезов: настоящий силуэт позы в метрах, свой у каждого
        /// состава дорожки. <c>null</c> — ассет не собран, и вырезы остаются
        /// прямоугольными по <see cref="SilhouetteSize"/>.
        /// Собрать: <c>Igruha/Дырка в стене/Испечь силуэты вырезов</c>.
        /// </summary>
        public HoleInWallSilhouettes Silhouettes => silhouettes;

        /// <summary>
        /// Запасной габарит силуэта позы, м. Для <see cref="HoleInWallPose.None"/> — ноль.
        ///
        /// ⚠️ Это <b>не</b> размер выреза в игре. Настоящий берётся из
        /// <see cref="Silhouettes"/> под состав дорожки: у Карлана ростом
        /// 1.61 м и Шланги ростом 1.96 м вырезы разные. Эти числа — объединение
        /// всего ростера, то есть вырез «на самого большого»; они остаются
        /// на случай, когда ассета нет или персонаж на дорожке не опознан.
        /// </summary>
        public Vector2 SilhouetteSize(HoleInWallPose pose)
        {
            int index = (int)pose - 1;
            if (poseSilhouettes == null || index < 0 || index >= poseSilhouettes.Length)
            {
                return Vector2.zero;
            }

            PoseSilhouette silhouette = poseSilhouettes[index];
            return new Vector2(silhouette.Width * unitsPerWidth, silhouette.Height * unitsPerWidth);
        }

        // ========== ТРОС ==========

        public float TetherLength => tetherLength * unitsPerWidth;
        public float TetherRamp => Mathf.Max(0.01f, tetherRamp * unitsPerWidth);
        public float TetherPullAcceleration => tetherPullAcceleration;
        public float TetherHardLimit => tetherHardLimit * unitsPerWidth;

        // ========== РАСПИСАНИЕ ==========

        /// <summary>Сколько стен в раунде. Задаётся длиной таблицы подъездов.</summary>
        public int WallCount => wallApproachSeconds != null ? wallApproachSeconds.Length : 0;

        public float PauseAfterHit => pauseAfterHit;

        /// <summary>Гарантированное окно на выбор позы перед ударом, с.</summary>
        public float PoseWindowSeconds => Mathf.Max(0f, poseWindowSeconds);

        public float SoloSpeedBonus => Mathf.Max(0f, soloSpeedBonus);
        public float RoundEndDelay => roundEndDelay;
        public float MirrorLead => mirrorLead;
        public float MorphLead => morphLead;
        public float WarningLead => warningLead;

        /// <summary>Номер стены с зеркальным переворотом, с нуля. Минус один — переворота нет.</summary>
        public int MirrorWallIndex => mirrorWall - 1;

        /// <summary>Номер стены со сменой формы, с нуля. Минус один — смены нет.</summary>
        public int MorphWallIndex => morphWall - 1;

        /// <summary>Подъезд стены на дорожке пары, с.</summary>
        public float ApproachSeconds(int wall) =>
            wallApproachSeconds != null && wall >= 0 && wall < wallApproachSeconds.Length
                ? Mathf.Max(0.01f, wallApproachSeconds[wall])
                : 0.01f;

        /// <summary>
        /// Подъезд стены на дорожке одиночки: короче на ту же долю, на какую
        /// выше скорость. Момент удара от этого не меняется — стена просто
        /// стартует позже (спека 5.4).
        /// </summary>
        public float ApproachSeconds(int wall, bool solo) =>
            solo ? ApproachSeconds(wall) / (1f + SoloSpeedBonus) : ApproachSeconds(wall);

        /// <summary>Скорость стены, м/с: весь путь за время подъезда.</summary>
        public float WallSpeed(int wall, bool solo) => WallTravel / ApproachSeconds(wall, solo);

        /// <summary>
        /// Момент удара стены от начала раунда, с. Общий для всех дорожек —
        /// поэтому от состава не зависит.
        /// </summary>
        public float HitTime(int wall)
        {
            float time = 0f;
            for (int i = 0; i <= wall && i < WallCount; i++)
            {
                if (i > 0)
                {
                    time += pauseAfterHit;
                }

                time += ApproachSeconds(i);
            }

            return time;
        }

        /// <summary>
        /// Момент ближайшего удара, до которого остаётся больше
        /// <paramref name="minLead"/> секунд. Минус один — таких ударов
        /// в раунде уже нет.
        ///
        /// Считается по таблице, а не по номеру текущей стадии: стадия
        /// переключается через <see cref="PauseAfterHit"/> после удара, и в эту
        /// паузу «текущая» стена уже отыграла. Функция от одного времени
        /// такой дырки не знает.
        /// </summary>
        /// <param name="elapsed">Сколько прошло с начала раунда, с</param>
        /// <param name="minLead">Сколько до удара обязано остаться, с</param>
        public float NextHitTime(float elapsed, float minLead)
        {
            for (int wall = 0; wall < WallCount; wall++)
            {
                float hit = HitTime(wall);
                if (hit - elapsed > minLead)
                {
                    return hit;
                }
            }

            return -1f;
        }

        /// <summary>Момент старта стены от начала раунда, с. У одиночки позже — см. <see cref="ApproachSeconds(int,bool)"/>.</summary>
        public float StartTime(int wall, bool solo) => HitTime(wall) - ApproachSeconds(wall, solo);

        /// <summary>
        /// Сколько длится стадия этой стены, с. Стадия идёт от старта стены
        /// до старта следующей; последняя тянется до конца раунда.
        ///
        /// Границы стадий считаются по дорожке пары — она стартует раньше
        /// одиночки, поэтому старт одиночки всегда попадает внутрь своей
        /// стадии, а не в предыдущую.
        /// </summary>
        public float StageDuration(int wall)
        {
            if (wall < 0 || wall >= WallCount)
            {
                return 0f;
            }

            return wall < WallCount - 1
                ? StartTime(wall + 1, false) - StartTime(wall, false)
                : HitTime(wall) + roundEndDelay - StartTime(wall, false);
        }

        /// <summary>Вся игровая часть раунда, с: от старта первой стены до конца после последнего удара.</summary>
        public float RoundLength => WallCount > 0 ? HitTime(WallCount - 1) + roundEndDelay : 0f;

        // ========== ПРОВАЛ ==========

        public float SweepImpulse => sweepImpulse;
        public float SweepUpward => sweepUpward;
        public float SweepClearanceSpeed => Mathf.Max(0f, sweepClearanceSpeed);

        /// <summary>
        /// Импульс сметания под конкретную стену, Н·с.
        ///
        /// <b>Постоянного импульса тут мало, и это арифметика.</b> При массе 2
        /// импульс 16 даёт 8.00 м/с, из которых по горизонтали — 7.55: доля
        /// <see cref="SweepUpward"/> уходит вверх. Скорость же стены растёт
        /// вместе с укорочением подъезда и на последних стенах доходит до
        /// 7.20 м/с у пары и <b>9.00 м/с у одиночки</b>. То есть стена летит
        /// быстрее, чем отбрасывает: её передняя грань проходит сквозь
        /// сметённого, и тело остаётся внутри плиты весь ход за линию —
        /// отсюда жалоба «проваливаюсь в стену». На стенах 7 и 8 обгон был
        /// отрицательным: −0.16 и −1.45 м/с.
        ///
        /// <b>Почему это лечится импульсом, а не коллайдером.</b> Плиты стены
        /// намеренно без коллайдеров, и вернуть их нельзя: допуск попадания
        /// <see cref="HitTolerance"/> = 0.576 м равен половине самого узкого
        /// выреза, а радиус капсулы игрока — 0.36 м. Игрок на границе допуска,
        /// которого проверка считает <b>прошедшим</b>, перекрывал бы плиту на
        /// треть метра и получал бы толчок за успешный проход. Поэтому
        /// «не быть внутри стены» достигается тем, что сметённый всегда
        /// улетает быстрее неё.
        ///
        /// Здесь импульс поднимается ровно настолько, чтобы горизонтальная
        /// составляющая обгоняла стену на <see cref="SweepClearanceSpeed"/>.
        /// На ранних стенах ничего не меняется: там заданные 16 и так с запасом.
        /// </summary>
        /// <param name="wallSpeed">Скорость этой стены, м/с</param>
        /// <param name="mass">Масса тела, кг</param>
        public float SweepImpulseFor(float wallSpeed, float mass)
        {
            // Импульс уходит под углом, поэтому по горизонтали приходит
            // не весь: множитель — косинус того же наклона, что задаёт
            // sweepUpward. Без него «обогнать стену» считалось бы по модулю,
            // и по горизонтали отлёт всё равно отставал бы.
            float horizontalShare = 1f / Mathf.Sqrt(1f + sweepUpward * sweepUpward);
            float needed = mass * (wallSpeed + SweepClearanceSpeed) / horizontalShare;
            return Mathf.Max(sweepImpulse, needed);
        }
        public float FallSeconds => fallSeconds;
        public float SplashSeconds => splashSeconds;

        /// <summary>Минимум барахтанья, с. Меньше — и провала не видно.</summary>
        public float MinSplashSeconds => Mathf.Max(0f, minSplashSeconds);

        /// <summary>Сколько всего проходит от сметания до возвращения на платформу, с.</summary>
        public float SweptReturnSeconds => fallSeconds + splashSeconds;

        /// <summary>Тот же путь по нижней границе: полёт всё равно занимает своё время.</summary>
        public float SweptReturnMinSeconds => fallSeconds + MinSplashSeconds;

        /// <summary>
        /// Через сколько секунд вернуть упавшего на платформу.
        ///
        /// <b>Возврат привязан к расписанию, а не к фиксированной задержке.</b>
        /// Фиксированная задержка давала окно на позу, которое плавало вместе
        /// с подъездом: 3.5 с после первой стены и 0.5 с после седьмой, то есть
        /// к концу раунда игрок возвращался позже сигнала за
        /// <see cref="WarningLead"/> до удара и стена решала за него. Здесь
        /// возврат назначается за <see cref="PoseWindowSeconds"/> до удара,
        /// и окно перестаёт зависеть от того, какая стена следующая.
        ///
        /// <b>Целимся в первый удар, к которому вообще можно успеть</b> — тот,
        /// до которого осталось больше <paramref name="floor"/>. Иначе торопить
        /// возврат незачем: игрок вынырнет в кадр удара, потеряет стену всё
        /// равно и вдобавок не отбарахтается. Пропущенный так удар — не потеря:
        /// он был потерян в момент падения.
        ///
        /// Границы у каждого пути падения свои: <paramref name="ceiling"/> —
        /// полная задержка пути, дольше неё не держим никогда;
        /// <paramref name="floor"/> — минимум, ниже которого провал перестаёт
        /// читаться и на глаз, и на слух.
        /// </summary>
        /// <param name="elapsed">Сколько прошло с начала раунда, с</param>
        /// <param name="ceiling">Полная задержка этого пути падения, с</param>
        /// <param name="floor">Минимальная задержка этого пути падения, с</param>
        public float ReturnDelay(float elapsed, float ceiling, float floor)
        {
            float hit = NextHitTime(elapsed, floor);
            if (hit < 0f)
            {
                // Удары кончились: держит только RoundEndDelay, спешить некуда.
                return ceiling;
            }

            return Mathf.Clamp(hit - PoseWindowSeconds - elapsed, floor, ceiling);
        }
    }
}
