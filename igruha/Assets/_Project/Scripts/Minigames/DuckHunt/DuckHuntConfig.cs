using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Числа Duck Hunt (спека, раздел 8). Крутятся без пересборки.
    ///
    /// Вся геометрия задана в ШП — ширинах персонажа (диаметр капсулы, вид сверху),
    /// а в юниты переводится вычисляемыми свойствами. Смысл в том, что арена
    /// построена под персонажа, а не под абстрактные метры: поменяется радиус
    /// капсулы — поменяется <see cref="CharacterWidth"/>, и пропорции всей башни
    /// поедут целиком, а не разъедутся по отдельным числам.
    ///
    /// Длительность раунда, число игроков и тексты обучалки живут в
    /// MinigameDefinition — общем для всех мини-игр. Здесь их намеренно нет:
    /// два места для одного числа рано или поздно разъезжаются.
    /// </summary>
    [CreateAssetMenu(fileName = "DuckHuntConfig", menuName = "Igruha/Duck Hunt Config")]
    public sealed class DuckHuntConfig : ScriptableObject
    {
        [Header("Единица измерения")]
        [Tooltip("ШП — ширина персонажа в юнитах: диаметр капсулы, вид сверху. Радиус капсулы 0.36 даёт 0.72. Если радиус изменится, менять здесь — пересчитается вся таблица размеров")]
        [SerializeField] private float characterWidth = 0.72f;

        [Header("Раунд")]
        [Tooltip("Обратный отсчёт до старта, с. Движение заблокировано у всех")]
        [SerializeField] private float startCountdown = 3f;
        [Tooltip("Этажей в башне")]
        [SerializeField] private int floorCount = 5;

        [Header("Охотник — лифт")]
        [Tooltip("Скорость лифта, ШП/с. ГЛАВНЫЙ рычаг баланса всей мини-игры: ослаблять Охотника надо им, а не боезапасом. 4.5 ШП/с = половина скорости бега")]
        [SerializeField] private float elevatorSpeed = 4.5f;
        [Tooltip("Нижняя граница хода лифта, ШП, от уровня пола первого этажа")]
        [SerializeField] private float elevatorMinHeight = -2f;
        [Tooltip("Верхняя граница хода лифта, ШП. Выше крыши — чтобы можно было караулить выход на неё")]
        [SerializeField] private float elevatorMaxHeight = 46f;

        [Header("Охотник — оружие")]
        [Tooltip("Задержка между выстрелами, с. Второй по важности параметр баланса после скорости лифта")]
        [SerializeField] private float fireDelay = 2f;
        [Tooltip("Патронов в обойме")]
        [SerializeField] private int magazineSize = 5;
        [Tooltip("Перезарядка обоймы, с")]
        [SerializeField] private float reloadDuration = 4f;
        [Tooltip("Разброс, когда лифт стоит, °")]
        [SerializeField] private float spreadStanding = 1.5f;
        [Tooltip("Разброс, когда лифт едет, °. По спеке вдвое больше базового")]
        [SerializeField] private float spreadMoving = 3f;
        [Tooltip("Дальность выстрела, ШП")]
        [SerializeField] private float shotRange = 60f;
        [Tooltip("Во что попадает луч. Слой PlayerBarrier (прозрачная стена вдоль открытой грани) обязан быть ИСКЛЮЧЁН — иначе стена ловит все выстрелы и стрелять по этажам нельзя вовсе")]
        [SerializeField] private LayerMask shotMask = ~0;
        [Tooltip("Импульс, которым сносит подстреленную Утку. Чисто визуальный: убивает само попадание, а это про то, насколько смешно тело улетает")]
        [SerializeField] private float deathImpulse = 18f;
        [Tooltip("Доля импульса, уходящая вверх. Без неё тело просто едет по полу, а не отлетает")]
        [Range(0f, 1f)]
        [SerializeField] private float deathImpulseLift = 0.4f;

        [Header("Охотник — камера")]
        [Tooltip("Обзор по ГОРИЗОНТАЛИ, °. Именно он задаёт, сколько этажа влезает в кадр: при 90° это 32 ШП из 48, то есть весь этаж разом Охотник не видит физически")]
        [SerializeField] private float horizontalFieldOfView = 90f;
        [Tooltip("Предел наклона взгляда, ° вверх и вниз. Хватает, чтобы с любой высоты достать и первый этаж, и крышу")]
        [SerializeField] private float cameraPitchLimit = 55f;
        [Tooltip("Потолок скорости поворота, °/с. У Охотника обзор свободный — в отличие от Водящего «Ангелов», здесь ограничение даёт сама перспектива, а не скорость мыши")]
        [SerializeField] private float cameraTurnSpeed = 720f;
        [Tooltip("Сдвиг камеры от глаза, м: X вбок, Y вверх, Z назад. Охотник смотрит от ТРЕТЬЕГО лица, из-за плеча: с отводом назад видно и его самого, и ружьё. Z меньше — камера ближе к затылку, Y больше — выше над плечом. Ноль по всем осям вернёт вид от первого лица (и тогда голова прячется, иначе она занимает весь кадр)")]
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 0.35f, 1.4f);

        [Header("Охотник — ружьё в руках")]
        [Tooltip("Модель ружья, которая выдаётся Охотнику на время роли. Пусто — Охотник целится пустыми руками, как было до арта")]
        [SerializeField] private GameObject rifleProp;
        [Tooltip("Положение ружья от точки на высоте плеча, м, в осях ПРИЦЕЛА: X вправо, Y вверх, Z вперёд по лучу. Ружьё стоит по лучу выстрела, а кисти к нему подтягивает IK — поэтому сдвиг общий на весь ростер, высота плеча замеряется у каждого своя")]
        [SerializeField] private Vector3 rifleGripOffset = new Vector3(0.14f, -0.02f, 0.12f);
        [Tooltip("Доворот ружья относительно луча выстрела, °. Ноль — ствол точно по лучу. Ненулевым его делают только ради читаемости позы со стороны")]
        [SerializeField] private Vector3 rifleTilt = Vector3.zero;
        [Tooltip("Где на ружье шейка приклада с курком — координата вдоль ствола в единицах модели. Сюда IK ставит ПРАВУЮ кисть")]
        [SerializeField] private float rifleGripPoint = 0.055f;
        [Tooltip("Где на ружье цевьё — координата вдоль ствола в единицах модели. Сюда IK ставит ЛЕВУЮ кисть. Дальше от курка — шире хват")]
        [SerializeField] private float rifleForePoint = 0.58f;
        [Tooltip("Масштаб модели ружья. Ружьё пака сделано под взрослого человека, персонажи проекта ниже и коренастее. Масштаб кисти в него не входит — он компенсируется отдельно, иначе ружьё разного размера у разных персонажей")]
        [SerializeField] private float rifleScale = 0.75f;

        [Header("Ловушки")]
        [Tooltip("Перезарядка кнопки, с. Общая для всех трёх ловушек")]
        [SerializeField] private float buttonCooldown = 10f;
        [Tooltip("Сколько дверь держится закрытой, с")]
        [SerializeField] private float doorClosedDuration = 5f;
        [Tooltip("Сколько участок пола остаётся провалившимся, с")]
        [SerializeField] private float collapseDuration = 3f;
        [Tooltip("Высота подброса гейзером, ШП. Время в воздухе — следствие высоты, а не отдельная настройка: 4.5 ШП это вдвое выше высокого укрытия и заметно ниже потолка")]
        [SerializeField] private float geyserLaunchHeight = 4.5f;

        [Header("Скользкий пол (зимние этажи)")]
        [Tooltip("Множитель разгона на льду")]
        [SerializeField] private float iceAccelerationMultiplier = 0.3f;
        [Tooltip("Множитель торможения на льду. Он и даёт скольжение")]
        [SerializeField] private float iceDecelerationMultiplier = 0.2f;

        [Header("Геометрия башни, ШП")]
        [Tooltip("Длина этажа")]
        [SerializeField] private float floorLength = 48f;
        [Tooltip("Ширина этажа")]
        [SerializeField] private float floorDepth = 14f;
        [Tooltip("Высота потолка. 7, а не 5: с паркурной платформы в 2 ШП полный прыжок требует 6.56, иначе персонаж бьётся макушкой и теряет часть прыжка")]
        [SerializeField] private float ceilingHeight = 7f;
        [Tooltip("Толщина перекрытия между этажами")]
        [SerializeField] private float slabThickness = 1f;
        [Tooltip("Расстояние от открытой грани до лифта. Отношение длины этажа к нему (48:16 = 3:1) и задаёт, сколько Охотник видит разом")]
        [SerializeField] private float distanceToElevator = 16f;
        [Tooltip("Сторона квадратной лестничной комнаты")]
        [SerializeField] private float stairRoomSize = 8f;
        [Tooltip("Сторона квадратной платформы лифта")]
        [SerializeField] private float elevatorPlatformSize = 4f;

        [Header("Объекты этажа, ШП")]
        [Tooltip("Высота высокого укрытия. Прячет стоящего (рост 2.29 ШП)")]
        [SerializeField] private float highCoverHeight = 2.5f;
        [Tooltip("Высота низкого укрытия. Прячет только присевшего (рост 1.15 ШП)")]
        [SerializeField] private float lowCoverHeight = 1.3f;
        [Tooltip("Высота бортика вдоль открытой грани. Ниже любого укрытия, выстрелам не мешает — нужен только чтобы край читался глазом")]
        [SerializeField] private float ledgeHeight = 1f;

        // ========== ЕДИНИЦА ==========

        /// <summary>ШП в юнитах. Основание всей геометрии.</summary>
        public float CharacterWidth => characterWidth;

        /// <summary>Перевести величину из ШП в юниты.</summary>
        public float ToUnits(float widths) => widths * characterWidth;

        // ========== РАУНД ==========

        public float StartCountdown => startCountdown;
        public int FloorCount => floorCount;

        // ========== ОХОТНИК ==========

        public float ElevatorSpeedUnits => ToUnits(elevatorSpeed);
        public float ElevatorMinHeightUnits => ToUnits(elevatorMinHeight);
        public float ElevatorMaxHeightUnits => ToUnits(elevatorMaxHeight);

        public float FireDelay => fireDelay;
        public int MagazineSize => magazineSize;
        public float ReloadDuration => reloadDuration;
        public float SpreadStanding => spreadStanding;
        public float SpreadMoving => spreadMoving;
        public float ShotRangeUnits => ToUnits(shotRange);
        public LayerMask ShotMask => shotMask;
        public float DeathImpulse => deathImpulse;
        public float DeathImpulseLift => deathImpulseLift;

        public float HorizontalFieldOfView => horizontalFieldOfView;
        public float CameraPitchLimit => cameraPitchLimit;
        public float CameraTurnSpeed => cameraTurnSpeed;
        public Vector3 CameraOffset => cameraOffset;

        // ========== РУЖЬЁ ==========

        public GameObject RifleProp => rifleProp;
        public Vector3 RifleGripOffset => rifleGripOffset;
        public Vector3 RifleTilt => rifleTilt;
        public float RifleScale => rifleScale;
        public float RifleGripPoint => rifleGripPoint;
        public float RifleForePoint => rifleForePoint;

        // ========== ЛОВУШКИ ==========

        public float ButtonCooldown => buttonCooldown;
        public float DoorClosedDuration => doorClosedDuration;
        public float CollapseDuration => collapseDuration;
        public float GeyserLaunchHeightUnits => ToUnits(geyserLaunchHeight);

        public float IceAccelerationMultiplier => iceAccelerationMultiplier;
        public float IceDecelerationMultiplier => iceDecelerationMultiplier;

        // ========== ГЕОМЕТРИЯ ==========

        public float FloorLengthUnits => ToUnits(floorLength);
        public float FloorDepthUnits => ToUnits(floorDepth);
        public float CeilingHeightUnits => ToUnits(ceilingHeight);
        public float SlabThicknessUnits => ToUnits(slabThickness);
        public float DistanceToElevatorUnits => ToUnits(distanceToElevator);
        public float StairRoomSizeUnits => ToUnits(stairRoomSize);
        public float ElevatorPlatformSizeUnits => ToUnits(elevatorPlatformSize);

        public float HighCoverHeightUnits => ToUnits(highCoverHeight);
        public float LowCoverHeightUnits => ToUnits(lowCoverHeight);
        public float LedgeHeightUnits => ToUnits(ledgeHeight);

        /// <summary>Длина этажа в ШП — по ней считается прогресс Утки вдоль трассы.</summary>
        public float FloorLengthWidths => floorLength;

        /// <summary>Шаг этажей по высоте, ШП: потолок плюс перекрытие.</summary>
        public float FloorStepWidths => ceilingHeight + slabThickness;

        /// <summary>Шаг этажей по высоте в юнитах.</summary>
        public float FloorStepUnits => ToUnits(FloorStepWidths);

        /// <summary>Высота башни в юнитах: пять этажей по своему шагу.</summary>
        public float TowerHeightUnits => FloorStepUnits * floorCount;

        /// <summary>
        /// Импульс, который подбросит персонажа на заданную высоту.
        /// Считается из гравитации и массы, а не задаётся числом: в спеке
        /// настраивается именно высота подброса, потому что время в воздухе —
        /// её следствие, а импульс зависит ещё и от настроек тела.
        /// </summary>
        public float GetGeyserImpulse(float mass, float riseGravityMultiplier)
        {
            float gravity = Mathf.Abs(Physics.gravity.y) * Mathf.Max(1f, riseGravityMultiplier);
            float speed = Mathf.Sqrt(2f * gravity * GeyserLaunchHeightUnits);
            return speed * mass;
        }
    }
}
