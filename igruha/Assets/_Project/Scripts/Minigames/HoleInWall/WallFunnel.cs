using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Воронка выреза: край дырки подправляет игрока, который встал в свою позу,
    /// но чуть мимо. Последняя доля секунды перед стеной.
    ///
    /// Вешается на аватар в начале раунда рядом с <see cref="PlayerPoseAbility"/>
    /// и снимается в конце — префабы персонажей заморожены
    /// (igruha/CLAUDE.md, раздел 🔒 0).
    /// </summary>
    /// <remarks>
    /// <b>Зачем она вообще понадобилась.</b> Прохождение засчитывается, только
    /// если игрок влез в дырку <b>по-настоящему</b>: допуск равен запасу
    /// контура, потому что дальше кожа уходит в сплошную плиту и проход
    /// выглядит как проход сквозь стену. Замер: при смещении 0.58 м — прежнем
    /// допуске — в плите оказывалось 46–100 % тела. Но требовать от игрока
    /// попадания в 15 см на бегу нельзя, и разрыв закрывает воронка: игра
    /// требует прежней точности, а последние сантиметры доводит сама.
    ///
    /// <b>Это не «игра играет за тебя».</b> Воронка не спасает от неверной
    /// позы, не поднимает из воды и не работает дальше <see cref="Reach"/> —
    /// ровно того расстояния, которое раньше засчитывалось молча. Она лишь
    /// делает засчитанное честным на вид: край дырки доводит, как довёл бы
    /// настоящий край настоящей дырки.
    ///
    /// <b>Сеть: каждая машина ведёт своего.</b> Воронка — не событие, а
    /// непрерывная сила, и уходит она в курс, а не в счёт. Поэтому применяет
    /// её сам владелец персонажа, тем же приёмом, что натяжение троса
    /// (<c>Core/Player/PlayerTether</c>) и струя <c>PushZone</c>: через сервер
    /// это были бы полсотни пакетов в секунду на игрока. Исход по-прежнему
    /// считает сервер и по своим числам — воронка на вердикт не влияет
    /// ничем, кроме того, что двигает тело, а тело он и так видит.
    /// </remarks>
    [RequireComponent(typeof(PlayerController))]
    public sealed class WallFunnel : MonoBehaviour
    {
        /// <summary>
        /// За сколько секунд до подхода стены край начинает доводить, с.
        ///
        /// Полсекунды — это примерно последние 4.5 м пути стены на средней
        /// скорости. Раньше — и доводка читается как автопилот: игрока
        /// подтягивает, когда он ещё выбирает, куда идти. Позже — и на
        /// доводку не остаётся хода: с 0.58 м за 0.2 с пришлось бы дёргать
        /// втрое сильнее, а это уже рывок, а не край дырки.
        /// </summary>
        private const float Lead = 0.5f;

        /// <summary>
        /// Максимальное ускорение доводки, м/с². Столько же, сколько у троса:
        /// сильнее — и воронка перетягивала бы натянутый трос, а он в этой игре
        /// главнее, он и есть комедия.
        /// </summary>
        private const float MaxAcceleration = 25f;

        /// <summary>
        /// Жёсткость доводки, 1/с². Подобрана под <see cref="Lead"/>: с 0.58 м
        /// за полсекунды доводка укладывается, не упираясь в потолок ускорения.
        /// </summary>
        private const float Stiffness = 90f;

        /// <summary>
        /// Гашение поперечной скорости, 1/с. Около критического для
        /// <see cref="Stiffness"/>: без него игрок проскакивает центр и
        /// начинает качаться в дырке.
        /// </summary>
        private const float Damping = 17f;

        private PlayerController motor;
        private PlayerPoseAbility ability;
        private NetworkObject body;
        private Rigidbody physics;

        private HoleInWallConfig config;
        private SweepingWall wall;
        private int cutoutIndex = -1;
        private float trackX;

        /// <summary>
        /// Насколько далеко от центра выреза край ещё дотягивается, м.
        ///
        /// Это прежний допуск попадания из конфига. Он не выброшен, а сменил
        /// роль: раньше на нём игрока молча засчитывали прошедшим сквозь плиту,
        /// теперь на нём его доводят до дырки по-настоящему.
        /// </summary>
        private float Reach => config != null ? config.HitTolerance : 0f;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            ability = GetComponent<PlayerPoseAbility>();
            body = GetComponent<NetworkObject>();
            TryGetComponent(out physics);
        }

        /// <summary>Привязать к вырезу: чья это воронка и на какой дорожке.</summary>
        public void Configure(HoleInWallConfig gameConfig, SweepingWall trackWall, int index, float trackCenterX)
        {
            config = gameConfig;
            wall = trackWall;
            cutoutIndex = index;
            trackX = trackCenterX;
        }

        /// <summary>Раунд кончился: больше никуда не доводим.</summary>
        public void Release() => wall = null;

        /// <summary>
        /// Доводка идёт в шаге физики: это сила, а не кадр отрисовки
        /// (igruha/CLAUDE.md, раздел 2).
        /// </summary>
        private void FixedUpdate()
        {
            if (!TryAim(out float targetX, out float strength))
            {
                return;
            }

            float offset = targetX - motor.Position.x;
            float lateral = physics != null ? physics.linearVelocity.x : 0f;
            float acceleration = Mathf.Clamp(
                Stiffness * offset - Damping * lateral, -MaxAcceleration, MaxAcceleration) * strength;

            float mass = physics != null ? physics.mass : 1f;
            motor.ApplyImpulse(Vector3.right * (acceleration * Time.fixedDeltaTime * mass));
        }

        /// <summary>
        /// Куда и насколько сильно доводить прямо сейчас.
        ///
        /// Условий четыре, и каждое отсекает случай, в котором помощь была бы
        /// нечестной: поза должна совпадать с вырезом (промах позой — это
        /// промах), игрок должен стоять на платформе (в прыжке и в воде
        /// не считается), стена должна быть на подходе, и он должен быть
        /// в пределах досягаемости края.
        /// </summary>
        private bool TryAim(out float targetX, out float strength)
        {
            targetX = 0f;
            strength = 0f;

            if (wall == null || !wall.Running || ability == null || !motor.IsGrounded)
            {
                return false;
            }

            // Чужое тело ведёт его машина, и всё, что мы ему напишем, тут же
            // перетрёт сетевой транспорт.
            if (!WorldAuthority.DrivenHere(body))
            {
                return false;
            }

            if (!wall.TryGetCutout(cutoutIndex, out HoleInWallPose pose, out float offset) ||
                ability.CurrentPose != pose)
            {
                return false;
            }

            float remaining = (wall.FrontZ - config.CheckLineZ) / Mathf.Max(0.01f, wall.Speed);
            if (remaining < 0f || remaining > Lead)
            {
                return false;
            }

            targetX = trackX + offset;
            if (Mathf.Abs(targetX - motor.Position.x) > Reach)
            {
                return false;
            }

            // Сила нарастает по мере подхода стены: издали край едва трогает,
            // у самой плиты доводит уверенно. Так это и читается краем дырки,
            // а не магнитом, включённым щелчком.
            strength = 1f - Mathf.Clamp01(remaining / Lead);
            return true;
        }
    }
}
