using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Все числа «Переноски предмета» v2 — кран и тележка — в одном ассете.
    /// Спека `docs/minigames/carry-item.md`, раздел 8. Ни одно значение не
    /// живёт в коде: геймдизайнер крутит их на плейтесте без программиста.
    ///
    /// Раскладка арены задаётся в ШИ и переводится в метры одним
    /// коэффициентом (<see cref="ToMeters"/>); всё, что касается воды,
    /// тележки и переноски, — сразу в единицах воды, метрах и секундах.
    /// </summary>
    [CreateAssetMenu(fileName = "CarryItemConfig", menuName = "Igruha/Minigames/Carry Item Config")]
    public sealed class CarryItemConfig : ScriptableObject
    {
        [Header("Респавн")]
        [Tooltip("Сколько секунд упавший в проём ждёт возврата на стартовую зону")]
        [SerializeField, Min(0f)] private float respawnDelaySeconds = 5f;
        public float RespawnDelaySeconds => Mathf.Max(0f, respawnDelaySeconds);

        [Header("Единицы и арена (ШИ)")]
        [Tooltip("Ширина персонажа в метрах: 1 ШИ")]
        [SerializeField] private float unitMeters = 0.72f;
        [SerializeField] private float arenaLength = 90f;
        [SerializeField] private float arenaDepth = 50f;
        [SerializeField] private float startZoneSize = 14f;
        [SerializeField] private float tankZoneSize = 10f;
        [SerializeField] private float commonAreaLength = 20f;
        [SerializeField] private float commonAreaWidth = 40f;
        [SerializeField] private float neckWidth = 8f;
        [SerializeField] private float rubbleHeight = 3f;
        [SerializeField] private float firstChasmWidth = 12f;
        [SerializeField] private float secondChasmWidth = 10f;
        [SerializeField] private float chasmDepth = 10f;
        [SerializeField] private float plankWidth = 2f;
        [SerializeField] private float stashOffset = 6f;
        [SerializeField] private float wallHeight = 6f;

        [Header("Кран и стоянка (метры)")]
        [Tooltip("Сторона зоны наполнения под изливом, м")]
        [SerializeField] private float tapZoneSize = 2.5f;
        [Tooltip("Высота излива над полом, м. Струя падает в открытый бак тележки")]
        [SerializeField] private float tapSpoutHeight = 1.6f;

        [Header("Вода и раунд")]
        [SerializeField] private float countdownSeconds = 3f;
        [Tooltip("Вместимость бака команды, единиц. Это и есть табло")]
        [SerializeField] private int tankCapacity = 600;
        [Tooltip("Вместимость тележки, единиц")]
        [SerializeField] private int cartCapacity = 150;
        [Tooltip("Ступень уровня воды, единиц. Кратна всем потерям; уровень едет по сети ступенями")]
        [SerializeField] private int waterStep = 5;
        [Tooltip("Темп наполнения под краном, единиц в секунду")]
        [SerializeField] private float fillRate = 25f;
        [Tooltip("Темп откачки насосом, единиц в секунду: 150 единиц за 6 секунд")]
        [SerializeField] private float pourRate = 25f;
        [Tooltip("Через сколько секунд улетевшая в пропасть тележка появляется на стоянке у крана")]
        [SerializeField] private float cartRespawnSeconds = 5f;

        [Header("Потери воды")]
        [SerializeField] private int hitLoss = 20;
        [SerializeField] private float hitCooldown = 2f;
        [Tooltip("Утечка от крена, единиц в секунду при половине заполнения (см. множитель ниже)")]
        [SerializeField] private float tiltLossPerSecond = 5f;
        [Tooltip("Множитель утечки = этот порог + заполнение (0…1): полная плещет вдвое сильнее полупустой")]
        [SerializeField] private float tiltLossLoadFloor = 0.5f;
        [SerializeField] private float tiltAngleThreshold = 45f;
        [SerializeField] private float tiltGraceSeconds = 1f;
        [SerializeField] private int ramVictimLoss = 20;
        [SerializeField] private int ramAttackerLoss = 10;
        [Tooltip("Какая доля остатка расплёскивается при толчке тележки с разгона")]
        [SerializeField, Range(0f, 1f)] private float shoveLossFraction = 0.25f;
        [Tooltip("С какой относительной скорости прилетевший предмет считается ударом, м/с")]
        [SerializeField] private float itemHitMinSpeed = 3f;

        [Header("Тележка: кузов и поручни (метры)")]
        [Tooltip("Длина кузова вдоль хода")]
        [SerializeField] private float cartLength = 1.2f;
        [Tooltip("Ширина кузова")]
        [SerializeField] private float cartWidth = 0.9f;
        [Tooltip("Высота бака тележки от пола")]
        [SerializeField] private float cartHeight = 1f;
        [Tooltip("Масса тела тележки для физики столкновений. Инерцию хода задают разгон и торможение, не масса")]
        [SerializeField] private float cartMass = 8f;
        [Tooltip("Высота поручней над полом")]
        [SerializeField] private float handleHeight = 1f;
        [Tooltip("Задние поручни: на сколько позади центра кузова")]
        [SerializeField] private float cartHandleBack = 0.65f;
        [Tooltip("Передние поручни: на сколько впереди центра кузова")]
        [SerializeField] private float cartHandleFront = 0.65f;
        [Tooltip("Парные поручни: разнос вбок от оси")]
        [SerializeField] private float cartHandleSide = 0.45f;
        [Tooltip("Насколько несущий стоит дальше своего поручня")]
        [SerializeField] private float carrierStandoff = 0.45f;

        [Header("Тележка: ход")]
        [SerializeField] private Vector4 speedByHandsEmpty = new Vector4(2.2f, 2.8f, 2.9f, 2.9f);
        [SerializeField] private Vector4 speedByHandsFull = new Vector4(1.6f, 2f, 2.2f, 2.3f);
        [SerializeField] private Vector4 accelerationByHandsEmpty = new Vector4(2.4f, 4f, 5.2f, 6.4f);
        [SerializeField] private Vector4 accelerationByHandsFull = new Vector4(1.2f, 2f, 2.6f, 3.2f);
        [SerializeField, Min(0.01f)] private float rollingTetherGain = 4f;
        [SerializeField, Min(0.01f)] private float rollingLateralGain = 8f;
        [Tooltip("Потолок скорости пустой тележки, м/с")]
        [SerializeField] private float maxSpeedEmpty = 2.8f;
        [Tooltip("Потолок скорости полной тележки, м/с")]
        [SerializeField] private float maxSpeedFull = 2f;
        [Tooltip("Потолок скорости несущего, м/с. Запас над тележкой — чтобы рывок был возможен и наказуем")]
        [SerializeField] private float maxCarrierSpeed = 2.9f;
        [Tooltip("Разгон пустой тележки, м/с²")]
        [SerializeField] private float accelerationEmpty = 4f;
        [Tooltip("Разгон полной тележки, м/с²")]
        [SerializeField] private float accelerationFull = 2f;
        [Tooltip("Торможение без рук, м/с²")]
        [SerializeField] private float rollingDeceleration = 1.5f;
        [Tooltip("Скорость доворота кузова к ходу, °/с")]
        [SerializeField] private float turnRate = 180f;
        [Tooltip("Во что превращается метр натяжения: целевая скорость, м/с на метр")]
        [SerializeField] private float pullToSpeed = 12f;
        [Tooltip("Натяжение ниже этого не считается, м")]
        [SerializeField] private float tensionDeadzone = 0.12f;
        [Tooltip("Растяжение связи, за которым поручень срывает, м")]
        [SerializeField] private float breakDistance = 1.8f;
        [SerializeField] private float tetherFreeSpeedPerMeter = 1.3f;
        [SerializeField, Range(0f, 1f)] private float tetherGrip = 0.85f;
        [Tooltip("Импульс толчка с разгона на одного толкающего")]
        [SerializeField] private float shoveImpulsePerCarrier = 7f;

        [Header("Тележка: крен")]
        [SerializeField] private float disagreementStart = 0.15f;
        [SerializeField] private float disagreementFull = 0.5f;
        [Tooltip("Чуть выше границы утечки, чтобы устойчивый перекос не колебался ровно на 45°")]
        [SerializeField] private float disagreementTilt = 48f;
        [SerializeField] private float turnTilt = 45f;
        [SerializeField] private float turnReferenceDegrees = 90f;
        [SerializeField] private float turnReferenceSpeed = 2f;
        [SerializeField] private float releaseTilt = 20f;
        [SerializeField] private float emptyTiltFraction = 0.5f;
        [SerializeField] private float unattendedPushSpeed = 0.3f;
        [SerializeField] private float warningTilt = 30f;
        [Tooltip("Во что превращается рывок кузова — изменение его скорости — у полной тележки: крен, рад/с на м/с. Ровная тяга на колёсах крена не даёт; разгон, стена, толчок и ловушка — дают")]
        [SerializeField] private float sloshPerDeltaSpeed = 1.3f;
        [SerializeField] private float tiltDamping = 3.2f;
        [SerializeField] private float tiltRestoring = 9f;
        [Tooltip("Предельный крен в руках, °")]
        [SerializeField] private float maxTiltAngle = 60f;

        [Header("Таран")]
        [SerializeField] private float minRamSpeed = 2f;
        [SerializeField] private float ramCooldown = 2f;
        [SerializeField] private float headOnTolerance = 0.2f;
        [SerializeField] private float ramContactDistance = 1.2f;

        [Header("Ловушки")]
        [SerializeField] private float beamPeriod = 6f;
        [SerializeField] private float beamNeckCoverage = 0.5f;
        [SerializeField] private float beamMinWindowSeconds = 2.5f;
        [Tooltip("Тачка-ловушка в горлышке: период опрокидывания, с. Не путать с тележкой-тарой")]
        [SerializeField] private float barrowPeriod = 8f;
        [Tooltip("Тачка-ловушка: сила выброса")]
        [SerializeField] private float barrowLaunchForce = 12f;
        [SerializeField] private float pushZoneForceFactor = 0.6f;

        public float UnitMeters => unitMeters;
        public float ArenaLength => arenaLength;
        public float ArenaDepth => arenaDepth;
        public float StartZoneSize => startZoneSize;
        public float TankZoneSize => tankZoneSize;
        public float CommonAreaLength => commonAreaLength;
        public float CommonAreaWidth => commonAreaWidth;
        public float NeckWidth => neckWidth;
        public float RubbleHeight => rubbleHeight;
        public float FirstChasmWidth => firstChasmWidth;
        public float SecondChasmWidth => secondChasmWidth;
        public float ChasmDepth => chasmDepth;
        public float PlankWidth => plankWidth;
        public float StashOffset => stashOffset;
        public float WallHeight => wallHeight;

        public float TapZoneSize => tapZoneSize;
        public float TapSpoutHeight => tapSpoutHeight;

        public float CountdownSeconds => countdownSeconds;
        public int TankCapacity => tankCapacity;
        public int CartCapacity => cartCapacity;
        public int WaterStep => waterStep;
        public float FillRate => fillRate;
        public float PourRate => pourRate;
        public float CartRespawnSeconds => cartRespawnSeconds;

        public int HitLoss => hitLoss;
        public float HitCooldown => hitCooldown;
        public float TiltLossPerSecond => tiltLossPerSecond;
        public float TiltLossLoadFloor => tiltLossLoadFloor;
        public float TiltAngleThreshold => tiltAngleThreshold;
        public float TiltGraceSeconds => tiltGraceSeconds;
        public int RamVictimLoss => ramVictimLoss;
        public int RamAttackerLoss => ramAttackerLoss;
        public float ShoveLossFraction => shoveLossFraction;
        public float ItemHitMinSpeed => itemHitMinSpeed;

        public float CartLength => cartLength;
        public float CartWidth => cartWidth;
        public float CartHeight => cartHeight;
        public float CartMass => cartMass;
        public float HandleHeight => handleHeight;
        public float CartHandleBack => cartHandleBack;
        public float CartHandleFront => cartHandleFront;
        public float CartHandleSide => cartHandleSide;
        public float CarrierStandoff => carrierStandoff;

        public float MaxSpeedEmpty => maxSpeedEmpty;
        public Vector4 SpeedByHandsEmpty => speedByHandsEmpty;
        public Vector4 SpeedByHandsFull => speedByHandsFull;
        public Vector4 AccelerationByHandsEmpty => accelerationByHandsEmpty;
        public Vector4 AccelerationByHandsFull => accelerationByHandsFull;
        public float RollingTetherGain => rollingTetherGain;
        public float RollingLateralGain => rollingLateralGain;
        public float DisagreementStart => disagreementStart;
        public float DisagreementFull => disagreementFull;
        public float DisagreementTilt => disagreementTilt;
        public float TurnTilt => turnTilt;
        public float TurnReferenceDegrees => turnReferenceDegrees;
        public float TurnReferenceSpeed => turnReferenceSpeed;
        public float ReleaseTilt => releaseTilt;
        public float EmptyTiltFraction => emptyTiltFraction;
        public float UnattendedPushSpeed => unattendedPushSpeed;
        public float WarningTilt => warningTilt;
        public float MaxSpeedFull => maxSpeedFull;
        public float MaxCarrierSpeed => maxCarrierSpeed;
        public float AccelerationEmpty => accelerationEmpty;
        public float AccelerationFull => accelerationFull;
        public float RollingDeceleration => rollingDeceleration;
        public float TurnRate => turnRate;
        public float PullToSpeed => pullToSpeed;
        public float TensionDeadzone => tensionDeadzone;
        public float BreakDistance => breakDistance;
        public float TetherFreeSpeedPerMeter => tetherFreeSpeedPerMeter;
        public float TetherGrip => tetherGrip;
        public float ShoveImpulsePerCarrier => shoveImpulsePerCarrier;

        public float SloshPerDeltaSpeed => sloshPerDeltaSpeed;
        public float TiltDamping => tiltDamping;
        public float TiltRestoring => tiltRestoring;
        public float MaxTiltAngle => maxTiltAngle;

        public float MinRamSpeed => minRamSpeed;
        public float RamCooldown => ramCooldown;
        public float HeadOnTolerance => headOnTolerance;
        public float RamContactDistance => ramContactDistance;

        public float BeamPeriod => beamPeriod;
        public float BeamNeckCoverage => beamNeckCoverage;
        public float BeamMinWindowSeconds => beamMinWindowSeconds;
        public float BarrowPeriod => barrowPeriod;
        public float BarrowLaunchForce => barrowLaunchForce;
        public float PushZoneForceFactor => pushZoneForceFactor;

        /// <summary>ШИ → метры.</summary>
        public float ToMeters(float unitsOfShoulder) => unitsOfShoulder * unitMeters;
    }
}
