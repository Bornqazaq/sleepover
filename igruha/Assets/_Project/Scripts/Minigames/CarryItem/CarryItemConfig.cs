using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Числа баланса «Переноски предмета» (спека, раздел 8). Один ассет на игру,
    /// крутится на плейтесте без пересборки.
    ///
    /// Здесь лежит только то, у чего нет дома в другом конфиге, — иначе вышло бы
    /// два источника правды. Остальное правится по месту:
    /// — длительность раунда 200 с, мин/макс игроков, тексты обучалки, режим
    ///   камеры — <see cref="Igruha.Core.Minigame.MinigameDefinition"/> (CarryItem.asset);
    /// — скорость бега, высота прыжка, сила удара — CharacterConfig (заморожен);
    /// — сколько болванок в соло-тесте — PlayerSpawner.debugPlayerCount на _Spawns.
    ///
    /// Размеры арены живут здесь же, а не в сцене: по ним арену собирает
    /// <c>CarryItemArenaBuilder</c>, и правка числа означает пересборку пунктом
    /// меню, а не двигание кубов руками.
    /// </summary>
    [CreateAssetMenu(fileName = "CarryItemConfig", menuName = "Igruha/Minigames/Carry Item Config")]
    public sealed class CarryItemConfig : ScriptableObject
    {
        [Header("Арена, ШИ (8.1)")]
        [Tooltip("Ширина игрока, м. Единица измерения всего проекта: 1 ШИ = 0.72 м")]
        [SerializeField] private float unitMeters = 0.72f;
        [Tooltip("Длина арены вдоль маршрута, ШИ")]
        [SerializeField] private float arenaLength = 90f;
        [Tooltip("Ширина арены поперёк маршрута, ШИ")]
        [SerializeField] private float arenaDepth = 50f;
        [Tooltip("Сторона стартовой зоны, ШИ. Пересчитано с 10 под правило камеры 4.5 м (спека 3.1) — уменьшать нельзя")]
        [SerializeField] private float startZoneSize = 14f;
        [Tooltip("Сторона зоны бака, ШИ. Пересчитано с 8 под то же правило камеры")]
        [SerializeField] private float tankZoneSize = 10f;
        [Tooltip("Длина общей площадки вдоль маршрута, ШИ")]
        [SerializeField] private float commonAreaLength = 20f;
        [Tooltip("Ширина общей площадки поперёк маршрута, ШИ")]
        [SerializeField] private float commonAreaWidth = 40f;
        [Tooltip("Ширина свободного прохода по центру площадки, ШИ. Пересчитано с 6 под правило камеры")]
        [SerializeField] private float neckWidth = 8f;
        [Tooltip("Высота завалов, ШИ. Выше прыжка (1.63 м) с запасом — не перескочить")]
        [SerializeField] private float rubbleHeight = 3f;
        [Tooltip("Ширина первой пропасти поперёк маршрута, ШИ")]
        [SerializeField] private float firstChasmWidth = 12f;
        [Tooltip("Ширина второй пропасти поперёк маршрута, ШИ")]
        [SerializeField] private float secondChasmWidth = 10f;
        [Tooltip("Глубина пропастей, ШИ. На дне стоит KillZone: пролёт 0.77 с плюс 1.2 с лежания дают объявленные LDD 2 с до респавна")]
        [SerializeField] private float chasmDepth = 10f;
        [Tooltip("Ширина доски через пропасть, ШИ. По одной на команду на каждой пропасти")]
        [SerializeField] private float plankWidth = 2f;
        [Tooltip("Насколько кучка метательного отнесена от края маршрута, ШИ. Крюк туда-обратно ≈ 4 с")]
        [SerializeField] private float stashOffset = 6f;
        [Tooltip("Высота стен арены, ШИ")]
        [SerializeField] private float wallHeight = 6f;

        [Header("Вода и раунд (8.2)")]
        [Tooltip("Обратный отсчёт перед стартом раунда, с")]
        [SerializeField] private float countdownSeconds = 3f;
        [Tooltip("Вместимость бака, единиц. Шесть полных бутылей")]
        [SerializeField] private int tankCapacity = 600;
        [Tooltip("Сколько воды в полной бутыли, единиц. 1/6 бака")]
        [SerializeField] private int bottleCapacity = 100;
        [Tooltip("Ступень видимого уровня воды, единиц. 20 ступеней на бутыль, кратна всем видам потерь")]
        [SerializeField] private int waterStep = 5;
        [Tooltip("За сколько секунд полная бутыль перетекает в бак. Бутыль должна оставаться в зоне весь слив")]
        [SerializeField] private float pourSeconds = 1.5f;
        [Tooltip("Сколько держать E на штабеле, чтобы взять бутыль, с")]
        [SerializeField] private float takeSeconds = 1.5f;
        [Tooltip("Через сколько секунд после того, как её отпустили все, исчезает брошенная пустая бутыль (5.1)")]
        [SerializeField] private float emptyBottleDespawnSeconds = 3f;

        [Header("Потери воды (8.3)")]
        [Tooltip("Удар по бутыли: игрок, предмет, ловушка. Единиц")]
        [SerializeField] private int hitLoss = 20;
        [Tooltip("Общий кулдаун на бутыль: одно событие — один штраф, с")]
        [SerializeField] private float hitCooldown = 2f;
        [Tooltip("Падение бутыли на землю, когда отпустили все. Единиц")]
        [SerializeField] private int dropLoss = 20;
        [Tooltip("Утечка при наклоне за порог, единиц в секунду")]
        [SerializeField] private float tiltLossPerSecond = 5f;
        [Tooltip("Порог наклона, °. За ним бутыль начинает лить")]
        [SerializeField] private float tiltAngleThreshold = 45f;
        [Tooltip("Сколько секунд наклон должен держаться, прежде чем польётся. Мгновенный клевок не наказывается")]
        [SerializeField] private float tiltGraceSeconds = 1f;
        [Tooltip("Таран: потери жертвы, единиц")]
        [SerializeField] private int ramVictimLoss = 20;
        [Tooltip("Таран: потери атакующего, единиц. КРИТИЧЕСКИЙ параметр плейтеста: поровну — таранить не будут, бесплатно — бросят носить")]
        [SerializeField] private int ramAttackerLoss = 10;
        [Tooltip("Доля остатка, теряемая при броске бутыли")]
        [Range(0f, 1f)]
        [SerializeField] private float throwLossFraction = 0.5f;
        [Tooltip("С какой скорости столкновения брошенный предмет считается попаданием по бутыли, м/с. Ниже — предмет просто лежит или катится под ногами")]
        [SerializeField] private float itemHitMinSpeed = 3f;

        [Header("Переноска (8.4)")]
        [Tooltip("Потолок скорости бутыли, м/с. Не зависит от числа несущих — треть от бега 6.5")]
        [SerializeField] private float maxObjectSpeed = 2.2f;
        [Tooltip("Потолок скорости несущего, м/с. Запас над бутылью — чтобы рывок был возможен и наказуем")]
        [SerializeField] private float maxCarrierSpeed = 2.9f;
        [Tooltip("Радиус ручек от оси бутыли, м. Плечо, на котором считается момент")]
        [SerializeField] private float handleRadius = 0.5f;
        [Tooltip("Высота ручек, м")]
        [SerializeField] private float handleHeight = 1.2f;
        [Tooltip("Предел растяжения связи, м. Отошёл дальше — руку сорвало")]
        [SerializeField] private float breakDistance = 1.8f;

        [Header("Таран (8.4)")]
        [Tooltip("Минимальная относительная скорость тарана, м/с. Ниже — притирка, потерь нет")]
        [SerializeField] private float minRamSpeed = 2f;
        [Tooltip("Кулдаун тарана на пару команд, с. Без него трение бутылями сливает баки за секунды")]
        [SerializeField] private float ramCooldown = 2f;
        [Tooltip("Разница проекций скоростей, ниже которой столкновение считается лобовым и атакующего нет")]
        [Range(0f, 1f)]
        [SerializeField] private float headOnTolerance = 0.2f;
        [Tooltip("Зазор между телами, при котором несущие считаются столкнувшимися, м")]
        [SerializeField] private float ramContactDistance = 1.2f;

        [Header("Ловушки (8.5)")]
        [Tooltip("Период балки на кране, с")]
        [SerializeField] private float beamPeriod = 6f;
        [Tooltip("Какую долю ширины горлышка перекрывает балка. Больше половины запирает проход наглухо")]
        [Range(0.1f, 0.5f)]
        [SerializeField] private float beamNeckCoverage = 0.5f;
        [Tooltip("Минимальное свободное окно на проход горлышка, с. Проверяется приёмкой 17.14")]
        [SerializeField] private float beamMinWindowSeconds = 2.5f;
        [Tooltip("Период опрокидывающейся тачки, с")]
        [SerializeField] private float cartPeriod = 8f;
        [Tooltip("Импульс подброса тачки")]
        [SerializeField] private float cartLaunchForce = 12f;
        [Tooltip("Доля от CharacterConfig.pushForce, с которой толкает струя прорванной трубы. Не роняет, но сбивает курс")]
        [SerializeField] private float pushZoneForceFactor = 0.6f;

        [Header("Модель переноски: тюнинг (9.1)")]
        [Tooltip("Во что превращается метр натяжения ручки: скорость бутыли, м/с на метр растяжения. Потолок скорости он не отменяет")]
        [SerializeField] private float pullToSpeed = 12f;
        [Tooltip("Во что превращается момент натяжений: угловое ускорение, рад/с² на метр·метр")]
        [SerializeField] private float tiltFromTorque = 10f;
        [Tooltip("Во что превращается смещение центра опоры при нехватке рук: угловое ускорение, рад/с² на метр. Это он валит бутыль, когда из четверых ушёл один")]
        [SerializeField] private float tiltFromSupportLoss = 19f;
        [Tooltip("Демпфер наклона, 1/с. Гасит раскачку от каждого шага")]
        [SerializeField] private float tiltDamping = 3.2f;
        [Tooltip("Возврат к вертикали, рад/с² на радиан наклона. Он и делает несущего одиночку устойчивым")]
        [SerializeField] private float tiltRestoring = 9f;
        [Tooltip("Предельный наклон бутыли в руках, °. Дальше её уже не удержать, и модель не должна её перевернуть")]
        [SerializeField] private float maxTiltAngle = 75f;
        [Tooltip("Натяжение ниже этого не считается вовсе, м. Иначе бутыль дёргается от каждого шага несущего")]
        [SerializeField] private float tensionDeadzone = 0.12f;
        [Tooltip("Сколько метров в секунду несущий волен уходить наружу на каждый метр оставшегося запаса связи. Меньше — связь держит крепче и рывок наказывается раньше")]
        [SerializeField] private float tetherFreeSpeedPerMeter = 1.3f;
        [Tooltip("Какую долю превышения связь отбирает, 0…1. Меньше единицы — упрямый бегун всё-таки дотягивает связь до срыва")]
        [Range(0f, 1f)]
        [SerializeField] private float tetherGrip = 0.85f;
        [Tooltip("Насколько несущий стоит дальше своей ручки, м. Столько места занимает его собственное тело")]
        [SerializeField] private float carrierStandoff = 0.45f;
        [Tooltip("На сколько дно бутыли поднято над ступнями несущих, м")]
        [SerializeField] private float carryClearance = 0.15f;
        [Tooltip("Импульс совместного броска бутыли на одного бросающего")]
        [SerializeField] private float throwImpulsePerCarrier = 7f;
        [Tooltip("Доля броска вверх")]
        [Range(0f, 1f)]
        [SerializeField] private float throwUpward = 0.45f;

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

        public float CountdownSeconds => countdownSeconds;
        public int TankCapacity => tankCapacity;
        public int BottleCapacity => bottleCapacity;
        public int WaterStep => waterStep;
        public float PourSeconds => pourSeconds;
        public float TakeSeconds => takeSeconds;
        public float EmptyBottleDespawnSeconds => emptyBottleDespawnSeconds;

        public int HitLoss => hitLoss;
        public float HitCooldown => hitCooldown;
        public int DropLoss => dropLoss;
        public float TiltLossPerSecond => tiltLossPerSecond;
        public float TiltAngleThreshold => tiltAngleThreshold;
        public float TiltGraceSeconds => tiltGraceSeconds;
        public int RamVictimLoss => ramVictimLoss;
        public int RamAttackerLoss => ramAttackerLoss;
        public float ThrowLossFraction => throwLossFraction;
        public float ItemHitMinSpeed => itemHitMinSpeed;

        public float MaxObjectSpeed => maxObjectSpeed;
        public float MaxCarrierSpeed => maxCarrierSpeed;
        public float HandleRadius => handleRadius;
        public float HandleHeight => handleHeight;
        public float BreakDistance => breakDistance;

        public float MinRamSpeed => minRamSpeed;
        public float RamCooldown => ramCooldown;
        public float HeadOnTolerance => headOnTolerance;
        public float RamContactDistance => ramContactDistance;

        public float BeamPeriod => beamPeriod;
        public float BeamNeckCoverage => beamNeckCoverage;
        public float BeamMinWindowSeconds => beamMinWindowSeconds;
        public float CartPeriod => cartPeriod;
        public float CartLaunchForce => cartLaunchForce;
        public float PushZoneForceFactor => pushZoneForceFactor;

        public float PullToSpeed => pullToSpeed;
        public float TiltFromTorque => tiltFromTorque;
        public float TiltFromSupportLoss => tiltFromSupportLoss;
        public float TiltDamping => tiltDamping;
        public float TiltRestoring => tiltRestoring;
        public float MaxTiltAngle => maxTiltAngle;
        public float TensionDeadzone => tensionDeadzone;
        public float TetherFreeSpeedPerMeter => tetherFreeSpeedPerMeter;
        public float TetherGrip => tetherGrip;
        public float CarrierStandoff => carrierStandoff;
        public float CarryClearance => carryClearance;
        public float ThrowImpulsePerCarrier => throwImpulsePerCarrier;
        public float ThrowUpward => throwUpward;

        /// <summary>Перевести размер из ШИ в метры. Единственное место, где живёт это умножение.</summary>
        public float ToMeters(float unitsOfShoulder) => unitsOfShoulder * unitMeters;
    }
}
