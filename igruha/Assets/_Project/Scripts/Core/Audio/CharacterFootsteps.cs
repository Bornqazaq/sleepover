using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Шаги персонажа — общий слой фазы 5, работает во всех пятнадцати играх.
    ///
    /// <b>Шаг отмеряется пройденным расстоянием, а не таймером и не анимацией.</b>
    /// Три пути было на выбор, и два отпали:
    ///
    /// <list type="bullet">
    /// <item><b>Animation event на касание стопы</b> — самый точный, но клипы бега
    /// у восьми персонажей заморожены (см. раздел 0 правил проекта), а правка
    /// клипа ради звука — ровно та молчаливая правка замороженного, которую
    /// проект запрещает.</item>
    /// <item><b>Таймер от скорости</b> — при разгоне и торможении шаг уезжает
    /// от ног, потому что частота меняется раньше, чем анимация.</item>
    /// <item><b>Расстояние</b> — шаг привязан к пройденному пути, поэтому сам
    /// учащается на бегу и редеет на приседе, без единого коэффициента на скорость.
    /// Персонаж, стоящий на месте и дёргающий стик, молчит: путь не растёт.</item>
    /// </list>
    ///
    /// Своей сетевой части нет и не нужно: позиция персонажа реплицирована, значит
    /// его путь растёт одинаково на всех машинах — чужие шаги слышны сами собой.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class CharacterFootsteps : MonoBehaviour
    {
        /// <summary>Длина шага бегущего, м. Подобрана под скорость бега: даёт около четырёх шагов в секунду.</summary>
        private const float DefaultStrideMeters = 1.25f;

        /// <summary>Во сколько раз шаг длиннее в приседе — крадущийся переставляет ноги реже.</summary>
        private const float CrouchStrideScale = 1.7f;

        /// <summary>Насколько тише шаг в приседе.</summary>
        private const float CrouchVolume = 0.4f;

        /// <summary>Ниже этой скорости шаг не звучит: это топтание на месте, а не ходьба.</summary>
        private const float MinSpeed = 0.6f;

        [Tooltip("Проигрыватель звука этого персонажа")]
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        [Tooltip("Контроллер персонажа. Не задан — берётся с этого же объекта")]
        [SerializeField] private PlayerController controller;

        [Tooltip("Чем звучит пол, на котором нет метки SurfaceAudio")]
        [SerializeField] private SurfaceKind defaultSurface = SurfaceKind.Concrete;

        [Tooltip("Длина шага бегущего, м")]
        [SerializeField] private float strideMeters = DefaultStrideMeters;

        /// <summary>Слот шага в обход поверхности — ставит мини-игра: шаг заражённого, шаг по воде.</summary>
        private string slotOverride;

        private Vector3 lastPosition;
        private float travelled;

        /// <summary>Разобранная поверхность прошлого шага. Пол под ногами меняется редко, а искать метку каждый шаг незачем.</summary>
        private Collider cachedGround;
        private string cachedSlot;

        /// <summary>
        /// Подменить слот шага на свой — или вернуть обычный, передав пустую строку.
        /// Нужно мини-играм, где шаг несёт смысл: заражённый слышен со спины,
        /// и это предупреждение, а не украшение.
        /// </summary>
        public void SetSlotOverride(string slotId) => slotOverride = slotId;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            lastPosition = transform.position;
        }

        private void OnEnable()
        {
            lastPosition = transform.position;
            travelled = 0f;
        }

        private void Update()
        {
            if (controller == null || audioPlayer == null) return;

            Vector3 position = controller.Position;
            Vector3 delta = position - lastPosition;
            delta.y = 0f;
            lastPosition = position;

            // В воздухе и в нокдауне путь не копится: прыжок озвучивает CharacterAudio,
            // а у лежащего шагов нет вовсе. Телепорт тоже гасится здесь — иначе
            // респаун на другом конце арены отсчитал бы себе сразу десяток шагов.
            if (!controller.IsGrounded || controller.IsKnockedDown)
            {
                travelled = 0f;
                return;
            }

            float distance = delta.magnitude;
            if (distance > strideMeters || distance < MinSpeed * Time.deltaTime)
            {
                if (distance > strideMeters) travelled = 0f;
                return;
            }

            travelled += distance;

            bool crouched = controller.IsCrouched;
            float stride = crouched ? strideMeters * CrouchStrideScale : strideMeters;
            if (travelled < stride) return;

            travelled -= stride;
            audioPlayer.PlayAt(ResolveSlot(), position, crouched ? CrouchVolume : 1f);
        }

        private string ResolveSlot()
        {
            if (!string.IsNullOrEmpty(slotOverride)) return slotOverride;

            Collider ground = controller.GroundCollider;
            if (ground != cachedGround)
            {
                cachedGround = ground;
                cachedSlot = SurfaceAudio.ResolveStepSlot(ground, defaultSurface);
            }

            return cachedSlot;
        }
    }
}
