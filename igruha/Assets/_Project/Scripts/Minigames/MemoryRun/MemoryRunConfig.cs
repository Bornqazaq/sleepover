using UnityEngine;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>
    /// Все числа «Рейса на память» одним ассетом: геймдизайнер крутит их без
    /// программиста. Спека прямо называет главный рычаг длительности — число
    /// шагов, — и резать его придётся в инспекторе, а не правкой кода.
    ///
    /// Размеры арены живут здесь же, потому что по ним строится сцена
    /// (см. <c>MemoryRunArenaBuilder</c>): один источник истины и для геометрии,
    /// и для правил.
    /// </summary>
    /// <remarks>
    /// <b>Длина цеха не поле, а вычисляемое свойство</b> — см. <see cref="HallDepth"/>.
    /// Это единственный способ не повторить ошибку LDD, где длина цеха (60 ШИ)
    /// оказалась меньше собственного содержимого (84 ШИ): одна цепочка плит при
    /// десяти шагах занимает 58 ШИ. Стоит числу шагов измениться, и любая
    /// записанная руками длина разъедется с ареной молча.
    /// </remarks>
    [CreateAssetMenu(menuName = "Igruha/Memory Run Config", fileName = "MemoryRunConfig")]
    public sealed class MemoryRunConfig : ScriptableObject
    {
        /// <summary>
        /// Плит в ряду. Константа, а не поле: «одна из трёх» — это сама игра,
        /// на тройке держатся и запрет перехода лево ↔ право, и оценка того,
        /// за сколько смертей раскрывается маршрут.
        /// </summary>
        public const int LaneCount = 3;

        [Header("Маршрут")]
        [Tooltip("ГЛАВНЫЙ РЫЧАГ ДЛИТЕЛЬНОСТИ. Затянулось на плейтесте — резать сюда, 10→8 срезает почти половину времени. Таймер трогать не надо")]
        [SerializeField] private int steps = 10;
        [Tooltip("Не более скольких одинаковых полос подряд. Без ограничения выпадает «центр, центр, центр, центр» — четыре шага запоминаются как один")]
        [SerializeField] private int maxSameLaneRun = 2;

        [Header("Ход")]
        [Tooltip("Сколько секунд у игрока на ход. Истёк — ход обрывается и засчитывается смерть, иначе один игрок морозит всю очередь")]
        [SerializeField] private float turnSeconds = 40f;
        [Tooltip("Пауза перед стартом таймера хода: игроку надо понять, что ход его")]
        [SerializeField] private float turnAnnounceSeconds = 2f;
        [Tooltip("Сколько смертей до выбывания из очереди. Предохранитель против тех, кто не запоминает вообще")]
        [SerializeField] private int deathLimit = 10;
        [Tooltip("Рагдолл-отлёт после взрыва — столько же, сколько в Duck Hunt и «Секундомере»")]
        [SerializeField] private float ragdollSeconds = 1.5f;
        [Tooltip("Сила подброса при детонации, импульс вверх-назад")]
        [SerializeField] private float mineImpulse = 16f;

        [Header("Арена, в ширинах персонажа (1 ШП = 0.72 м)")]
        [SerializeField] private float unitsPerWidth = 0.72f;
        [Tooltip("Сторона плиты")]
        [SerializeField] private float plateSize = 4f;
        [Tooltip("Толщина плиты — только на вид, игрок ходит по верхней грани")]
        [SerializeField] private float plateThickness = 0.5f;
        [Tooltip("Зазор между плитами внутри ряда. Он же определяет длину диагонального прыжка центр ↔ край")]
        [SerializeField] private float laneGap = 1f;
        [Tooltip("Пропасть между шагами. Подобран заведомо с запасом: прыжок берёт 4.69 м, здесь 1.44")]
        [SerializeField] private float stepGap = 2f;
        [Tooltip("28, а не 18: при 18 от крайней плиты до стены 1.44 м, а камере нужно 4.5 (igruha/CLAUDE.md, 2a)")]
        [SerializeField] private float hallWidth = 28f;
        [SerializeField] private float ceilingHeight = 10f;
        [Tooltip("Глубина пропасти. Дно не видно, теряется в темноте и дыму")]
        [SerializeField] private float pitDepth = 12f;

        [Header("Стартовая зона и выход")]
        [Tooltip("Сторона площадки ожидания. Здесь ждут очереди, бегают и дерутся")]
        [SerializeField] private float startZoneSize = 14f;
        [Tooltip("На сколько зона приподнята над уровнем плит. Не украшение: при восьмерых стоящие сзади должны видеть плиты поверх голов")]
        [SerializeField] private float startZoneLift = 1f;
        [Tooltip("Запас пола позади стартовой зоны на отход камеры. При нуле стоящий у задней стенки получает камеру в затылок — грабли «Верю / не верю»")]
        [SerializeField] private float cameraClearance = 6.5f;
        [Tooltip("Глубина безопасной площадки перед дверью")]
        [SerializeField] private float exitPadDepth = 8f;
        [Tooltip("Высота барьера очереди. Прыжок берёт 2.3 ШИ, так что 3 — с запасом")]
        [SerializeField] private float gateHeight = 3f;

        public int Steps => Mathf.Max(1, steps);
        public int MaxSameLaneRun => Mathf.Clamp(maxSameLaneRun, 1, Mathf.Max(1, steps));

        public float TurnSeconds => turnSeconds;
        public float TurnAnnounceSeconds => turnAnnounceSeconds;
        public int DeathLimit => Mathf.Max(1, deathLimit);
        public float RagdollSeconds => ragdollSeconds;
        public float MineImpulse => mineImpulse;

        public float UnitsPerWidth => unitsPerWidth;
        public float PlateSize => plateSize * unitsPerWidth;
        public float PlateThickness => plateThickness * unitsPerWidth;
        public float LaneGap => laneGap * unitsPerWidth;
        public float StepGap => stepGap * unitsPerWidth;
        public float HallWidth => hallWidth * unitsPerWidth;
        public float CeilingHeight => ceilingHeight * unitsPerWidth;
        public float PitDepth => pitDepth * unitsPerWidth;

        public float StartZoneSize => startZoneSize * unitsPerWidth;
        public float StartZoneLift => startZoneLift * unitsPerWidth;
        public float CameraClearance => cameraClearance * unitsPerWidth;
        public float ExitPadDepth => exitPadDepth * unitsPerWidth;
        public float GateHeight => gateHeight * unitsPerWidth;

        /// <summary>Шаг между центрами соседних полос: плита плюс зазор.</summary>
        public float LanePitch => PlateSize + LaneGap;

        /// <summary>Шаг между центрами соседних рядов: плита плюс пропасть.</summary>
        public float StepPitch => PlateSize + StepGap;

        /// <summary>Ширина ряда целиком — три плиты и два зазора.</summary>
        public float RowWidth => LaneCount * PlateSize + (LaneCount - 1) * LaneGap;

        /// <summary>
        /// Длина всей цепочки плит: десять рядов и девять пропастей между ними.
        /// Ровно то число, которое не сошлось в LDD.
        /// </summary>
        public float ChainDepth => Steps * PlateSize + (Steps - 1) * StepGap;

        /// <summary>
        /// Длина цеха целиком. Считается, а не задаётся: запас на камеру,
        /// стартовая зона, пропасть до первого ряда, цепочка, пропасть после
        /// последнего ряда и площадка перед дверью.
        /// </summary>
        public float HallDepth =>
            CameraClearance + StartZoneSize + StepGap + ChainDepth + StepGap + ExitPadDepth;

        /// <summary>
        /// Z ближнего края стартовой зоны. Начало координат — центр цеха, чтобы
        /// арена жила симметрично относительно нуля, как во всех остальных играх.
        /// </summary>
        public float NearEdgeZ => -HallDepth * 0.5f;

        /// <summary>Z дальнего края стартовой зоны, он же место барьера очереди.</summary>
        public float GateZ => NearEdgeZ + CameraClearance + StartZoneSize;

        /// <summary>Z ближнего края первого ряда плит.</summary>
        public float ChainStartZ => GateZ + StepGap;

        /// <summary>Z ближнего края выходной площадки.</summary>
        public float ExitPadZ => ChainStartZ + ChainDepth + StepGap;

        /// <summary>Центр плиты по X для полосы 0 (левая), 1 (центр), 2 (правая).</summary>
        public float LaneX(int lane) => (lane - (LaneCount - 1) * 0.5f) * LanePitch;

        /// <summary>Центр плиты по Z для шага с индексом 0…<see cref="Steps"/>−1.</summary>
        public float StepZ(int step) => ChainStartZ + step * StepPitch + PlateSize * 0.5f;

        /// <summary>
        /// На какой плите стоит точка. Возвращает false, если точка не над
        /// плитой вовсе — над пропастью, стартовой зоной или площадкой выхода.
        ///
        /// Существует затем, чтобы <b>сервер определял приземление сам</b>, по
        /// позиции, а не со слов клиента. Плиты стоят регулярной сеткой из этих
        /// же чисел, поэтому обратный пересчёт точен и не требует ни триггеров,
        /// ни тридцати лишних коллайдеров в сцене.
        /// </summary>
        public bool TryGetCell(Vector3 worldPosition, out int step, out int lane)
        {
            step = -1;
            lane = -1;

            float half = PlateSize * 0.5f;

            float laneRaw = worldPosition.x / LanePitch + (LaneCount - 1) * 0.5f;
            int laneIndex = Mathf.RoundToInt(laneRaw);
            if (laneIndex < 0 || laneIndex >= LaneCount)
            {
                return false;
            }

            if (Mathf.Abs(worldPosition.x - LaneX(laneIndex)) > half)
            {
                return false;
            }

            float stepRaw = (worldPosition.z - ChainStartZ - PlateSize * 0.5f) / StepPitch;
            int stepIndex = Mathf.RoundToInt(stepRaw);
            if (stepIndex < 0 || stepIndex >= Steps)
            {
                return false;
            }

            if (Mathf.Abs(worldPosition.z - StepZ(stepIndex)) > half)
            {
                return false;
            }

            step = stepIndex;
            lane = laneIndex;
            return true;
        }

        /// <summary>Точка над центром плиты — куда ставить персонажа и что показывать маркеру.</summary>
        public Vector3 CellCenter(int step, int lane) => new Vector3(LaneX(lane), 0f, StepZ(step));

        /// <summary>
        /// Самый длинный прыжок, который маршрут вообще может потребовать, —
        /// диагональ центр ↔ край от края плиты до края следующей.
        ///
        /// Существует затем, чтобы построитель арены мог сверить это число
        /// с дальностью прыжка персонажа и заорать, если планировка уехала.
        /// Именно на этом сорвался LDD: переход лево ↔ право требовал 4.55 м
        /// при дальности 4.69, то есть 3% запаса и только с идеального угла.
        /// </summary>
        public float LongestRequiredJump => Mathf.Sqrt(LaneGap * LaneGap + StepGap * StepGap);
    }
}
