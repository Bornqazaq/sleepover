using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>
    /// Числа медведя в яме. Медведь один и тот же в «Секундомере» и в «Порядке
    /// банок», поэтому его числа живут здесь, в общей папке арены, а не в конфиге
    /// конкретной игры: вторая копия неизбежно разъедется с первой — подкрутили
    /// скорость погони в одной игре, и одна и та же яма стала вести себя
    /// по-разному.
    ///
    /// <b>«Секундомер» на этот ассет ещё не переехал.</b> У него свои копии тех же
    /// чисел в <c>StopwatchConfig</c>, и переезд идёт отдельным тикетом после его
    /// арт-фазы. До тех пор расхождение существует — и записано здесь именно
    /// затем, чтобы оно было видимым, а не тихим: правя числа тут, проверь, не
    /// разошлись ли они с <c>StopwatchConfig</c>.
    ///
    /// Чего здесь нет:
    /// — радиус ямы, в которой медведь бегает, — <see cref="CircusArenaConfig"/>:
    ///   это размер арены, а не свойство медведя;
    /// — скорость поворота корпуса и отступ от борта — поля самого
    ///   <see cref="PitBear"/>: они про походку заготовки и меняются вместе
    ///   с моделью на арт-фазе.
    /// </summary>
    [CreateAssetMenu(fileName = "CircusBearConfig", menuName = "Igruha/Minigames/Circus Bear Config")]
    public sealed class CircusBearConfig : ScriptableObject
    {
        [Header("Погоня")]
        [Tooltip("Скорость погони, м/с. Меньше, чем у игрока (6.5), и это принципиально: медведь догоняет срезанием по хорде, а не скоростью. Дай ему быстрее игрока — и забега нет, есть мгновенная смерть")]
        [SerializeField] private float chaseSpeed = 5.5f;
        [Tooltip("Скорость патрулирования пустой ямы, м/с")]
        [SerializeField] private float patrolSpeed = 2.5f;
        [Tooltip("Сколько секунд медведь разворачивается и разгоняется, прежде чем впервые ударить. Это фора выпавшему: без неё он умирает, не успев встать")]
        [SerializeField] private float firstAttackDelay = 3f;

        [Header("Удар")]
        [Tooltip("Радиус удара лапой, м")]
        [SerializeField] private float strikeRadius = 1.5f;
        [Tooltip("Скорость отлёта от удара, м/с")]
        [SerializeField] private float knockbackSpeed = 8f;

        [Header("Гибель")]
        [Tooltip("Сколько длится отлёт и падение, с. Сейчас это справочное значение: длительностью распоряжается PlayerElimination своим полем, и оно совпадает. Переезд на этот ассет — правка в Core, отдельным тикетом")]
        [SerializeField] private float knockdownDuration = 1.5f;
        [Tooltip("Через сколько секунд после падения тело исчезает, с. Коллизия снимается сразу. Та же оговорка, что и выше: значением распоряжается PlayerElimination")]
        [SerializeField] private float bodyDespawnDelay = 1.5f;

        public float ChaseSpeed => chaseSpeed;
        public float PatrolSpeed => patrolSpeed;
        public float FirstAttackDelay => firstAttackDelay;
        public float StrikeRadius => strikeRadius;
        public float KnockbackSpeed => knockbackSpeed;
        public float KnockdownDuration => knockdownDuration;
        public float BodyDespawnDelay => bodyDespawnDelay;

        /// <summary>
        /// Раздать числа медведю. Радиус ямы приходит отдельным аргументом:
        /// он свойство арены, а не медведя, и живёт в <see cref="CircusArenaConfig"/>.
        ///
        /// Метод существует, чтобы порядок шести аргументов
        /// <see cref="PitBear.Configure"/> перечислялся в одном месте, а не
        /// переписывался в каждой игре: перепутанные местами радиус удара
        /// и задержка компилируются молча.
        /// </summary>
        public void Apply(PitBear bear, float pitRadius)
        {
            if (bear == null)
            {
                return;
            }

            bear.Configure(chaseSpeed, patrolSpeed, strikeRadius, firstAttackDelay, knockbackSpeed, pitRadius);
        }
    }
}
