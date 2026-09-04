using UnityEngine;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Все числа «Верю / не верю» одним ассетом: геймдизайнер крутит их без
    /// программиста. Спека прямо предупреждает, что таймер уговоров — первое,
    /// что придётся резать на плейтесте, и делаться это должно в инспекторе,
    /// а не правкой кода.
    ///
    /// Размеры арены живут здесь же, потому что по ним строится сцена
    /// (см. <c>BelieveOrNotArenaBuilder</c>): один источник истины и для
    /// геометрии, и для правил.
    /// </summary>
    [CreateAssetMenu(menuName = "Igruha/Believe Or Not Config", fileName = "BelieveOrNotConfig")]
    public sealed class BelieveOrNotConfig : ScriptableObject
    {
        [Header("Длительности стадий, секунды")]
        [Tooltip("Рассадка: перенос за стол, разворот, камера")]
        [SerializeField] private float seatingSeconds = 3f;
        [Tooltip("Показ карточки Знающему")]
        [SerializeField] private float peekSeconds = 3f;
        [Tooltip("Уговоры. ПЕРВОЕ, что резать при затянутости: 40→30 вероятно понадобится на плейтесте")]
        [SerializeField] private float persuasionSeconds = 40f;
        [Tooltip("Раскрытие: обмен коробок и подъём обеих крышек")]
        [SerializeField] private float revealSeconds = 4f;
        [Tooltip("Пауза на реакцию: звук, гэг, обновление счёта")]
        [SerializeField] private float reactionSeconds = 3f;

        [Header("Коробки")]
        [Tooltip("Обмен коробок местами по дуге. Наглядность здесь важнее скорости")]
        [SerializeField] private float boxSwapSeconds = 1.2f;
        [Tooltip("Поворот крышки на 90°")]
        [SerializeField] private float lidOpenSeconds = 0.4f;
        [Tooltip("На сколько градусов крышка приподнимается «на щёлку» в фазе показа")]
        [SerializeField] private float lidPeekAngle = 12f;
        [Tooltip("Высота дуги при обмене коробок, доля от расстояния между ними")]
        [SerializeField] private float boxSwapArcHeight = 0.35f;

        [Header("Реплики")]
        [Tooltip("Пауза между репликами одного игрока — антиспам")]
        [SerializeField] private float phraseCooldown = 1.5f;
        [Tooltip("Сколько секунд висит пузырь над головой")]
        [SerializeField] private float phraseBubbleSeconds = 3f;

        [Header("Число конов по составу")]
        [Tooltip("Индекс 0 — лобби из 2, дальше по возрастанию до 8. Таблица из LDD: она же даёт почти идеальную ротацию")]
        [SerializeField] private int[] roundsByPlayerCount = { 4, 3, 2, 3, 3, 4, 4 };

        [Header("Страховки")]
        [Tooltip("Жёсткий предел матча. Штатный матч на 4 конах — 212 с, так что срабатывать не должен никогда")]
        [SerializeField] private float matchTimeoutSeconds = 360f;

        [Header("Арена, в ширинах персонажа (1 ШП = 0.72 м)")]
        [SerializeField] private float unitsPerWidth = 0.72f;
        [Tooltip("34, а не 30: при 30 зона зрителей упирается в стену и камере не хватает 4.5 м позади")]
        [SerializeField] private float hallWidth = 34f;
        [SerializeField] private float hallDepth = 34f;
        [SerializeField] private float ceilingHeight = 7f;
        [SerializeField] private float tableDiameter = 4f;
        [Tooltip("Высота столешницы над полом")]
        [SerializeField] private float tableHeight = 1f;
        [Tooltip("Точка посадки: расстояние от центра стола")]
        [SerializeField] private float seatDistance = 2.8f;
        [Tooltip("Невидимая крышка НАД столом: закрывает объём, в который иначе можно запрыгнуть. " +
                 "Меньше стола, чтобы не задевать сидящих. Держит тело, пропускает камеру — слой Ignore Raycast")]
        [SerializeField] private float barrierRadius = 1.6f;
        [SerializeField] private float barrierHeight = 1.5f;
        [Tooltip("Свободная зона зрителей: радиус от центра стола, внутри него ничего не стоит")]
        [SerializeField] private float spectatorZoneRadius = 10f;
        [Tooltip("Радиус окружности стартовых точек")]
        [SerializeField] private float spawnRingRadius = 6f;
        [SerializeField] private float boxSize = 0.8f;
        [Tooltip("Смещение коробки от центра стола к своему владельцу. Держать близко к центру: " +
                 "разнесённые по краям коробки не влезают в один кадр вместе с лицом оппонента")]
        [SerializeField] private float boxOffset = 0.55f;
        [SerializeField] private float lampHeight = 3.5f;

        [Header("Камера сидящего")]
        [Tooltip("Насколько риг отнесён за точку посадки вдоль оси стола, ШП. Работает только " +
                 "в паре с боковым выносом и высотой: сам по себе отход назад ставит камеру " +
                 "в затылок себе")]
        [SerializeField] private float seatCameraBack = 0.19f;
        [Tooltip("Боковой вынос рига от оси стола, ШП. Ноль означает взгляд СКВОЗЬ собственную " +
                 "голову: до плейтеста 28.08 было именно так, и своё же тело закрывало " +
                 "обе коробки и половину лица оппонента. Кадр обязан быть через плечо")]
        [SerializeField] private float seatCameraSide = 0.83f;
        [Tooltip("Высота рига над полом, метры. Выше головы сидящего: сверху видно крышки " +
                 "коробок и что с ними делают, с уровня глаз — только их бока")]
        [SerializeField] private float seatCameraHeight = 2f;
        [Tooltip("Куда смотрит камера: точка над центром стола, метры. По ней в кадр попадают " +
                 "и обе коробки, и лицо оппонента, и пузырь с его репликой")]
        [SerializeField] private float seatLookHeight = 1.2f;
        [Tooltip("Угол обзора. Подобран так, чтобы пузырь оппонента не срезало верхней " +
                 "границей, а своя коробка не ушла за нижнюю")]
        [SerializeField] private float seatCameraFov = 50f;

        [Header("Свет")]
        [Tooltip("Угол конуса лампы над столом")]
        [SerializeField] private float lampSpotAngle = 110f;
        [Tooltip("Яркость лампы в канделах. Точечный свет падает как интенсивность делить на квадрат " +
                 "расстояния, а от лампы до сукна 1.8 м: 6 даёт на столешнице 1.85 — тёплое пятно, " +
                 "в котором материалы читаются своим цветом, а не выгорают. На прежних 25 выходило " +
                 "семь с половиной, и тёмное дерево борта читалось лососёвым. " +
                 "Найдено на арте, подфаза 4.1")]
        [SerializeField] private float lampIntensity = 6f;
        [Tooltip("Цвет лампы: тёплый, ~3000 K")]
        [SerializeField] private Color lampColor = new Color(1f, 0.85f, 0.65f);
        [Tooltip("Общий свет зала. Тёмный — темнота здесь механика фокуса, а не украшение, — " +
                 "но не чёрный: на 0.02 зритель за пределами круга лампы не видел ни пола, " +
                 "ни соседей и терял стол из виду вовсе")]
        [SerializeField] private Color ambientColor = new Color(0.07f, 0.07f, 0.09f);

        public float SeatingSeconds => seatingSeconds;
        public float PeekSeconds => peekSeconds;
        public float PersuasionSeconds => persuasionSeconds;
        public float RevealSeconds => revealSeconds;
        public float ReactionSeconds => reactionSeconds;

        /// <summary>Полный кон целиком — для проверки тайминга и для HUD.</summary>
        public float RoundSeconds =>
            seatingSeconds + peekSeconds + persuasionSeconds + revealSeconds + reactionSeconds;

        public float BoxSwapSeconds => boxSwapSeconds;
        public float LidOpenSeconds => lidOpenSeconds;
        public float LidPeekAngle => lidPeekAngle;
        public float BoxSwapArcHeight => boxSwapArcHeight;

        public float PhraseCooldown => phraseCooldown;
        public float PhraseBubbleSeconds => phraseBubbleSeconds;

        public float MatchTimeoutSeconds => matchTimeoutSeconds;

        public float UnitsPerWidth => unitsPerWidth;
        public float HallWidth => hallWidth * unitsPerWidth;
        public float HallDepth => hallDepth * unitsPerWidth;
        public float CeilingHeight => ceilingHeight * unitsPerWidth;
        public float TableDiameter => tableDiameter * unitsPerWidth;
        public float TableHeight => tableHeight * unitsPerWidth;
        public float SeatDistance => seatDistance * unitsPerWidth;
        public float BarrierRadius => barrierRadius * unitsPerWidth;
        public float BarrierHeight => barrierHeight * unitsPerWidth;
        public float SpectatorZoneRadius => spectatorZoneRadius * unitsPerWidth;
        public float SpawnRingRadius => spawnRingRadius * unitsPerWidth;
        public float BoxSize => boxSize * unitsPerWidth;
        public float BoxOffset => boxOffset * unitsPerWidth;
        public float LampHeight => lampHeight * unitsPerWidth;

        public float SeatCameraBack => seatCameraBack * unitsPerWidth;

        /// <summary>Боковой вынос камеры сидящего — кадр «через плечо».</summary>
        public float SeatCameraSide => seatCameraSide * unitsPerWidth;

        /// <summary>Высота рига задана в метрах: она привязана к росту персонажа, а не к планировке.</summary>
        public float SeatCameraHeight => seatCameraHeight;

        /// <summary>Высота точки, в которую смотрит камера сидящего. Тоже в метрах и по той же причине.</summary>
        public float SeatLookHeight => seatLookHeight;
        public float SeatCameraFov => seatCameraFov;

        public float LampSpotAngle => lampSpotAngle;
        public float LampIntensity => lampIntensity;
        public Color LampColor => lampColor;
        public Color AmbientColor => ambientColor;

        /// <summary>
        /// Сколько конов будет в матче. Считается ОДИН раз, на старте: от числа
        /// конов зависит ротация посадки, и если оно поплывёт при выходе игрока,
        /// станет непредсказуемо, кто ещё успеет сесть.
        /// </summary>
        public int GetRoundCount(int playerCount)
        {
            int clamped = Mathf.Clamp(playerCount, 2, 8);
            int index = Mathf.Clamp(clamped - 2, 0, roundsByPlayerCount.Length - 1);
            return Mathf.Max(1, roundsByPlayerCount[index]);
        }

        /// <summary>Длительность стадии по её номеру. Ноль — стадии нет.</summary>
        public float GetStageSeconds(byte stage)
        {
            switch (stage)
            {
                case BelieveStage.Seating: return seatingSeconds;
                case BelieveStage.Peek: return peekSeconds;
                case BelieveStage.Persuasion: return persuasionSeconds;
                case BelieveStage.Reveal: return revealSeconds;
                case BelieveStage.Reaction: return reactionSeconds;
                default: return 0f;
            }
        }
    }
}
