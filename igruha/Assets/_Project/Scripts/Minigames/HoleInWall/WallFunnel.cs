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
    ///
    /// <b>Скорость, а не сила (IGR-594).</b> До 22.09 край толкал импульсом
    /// с потолком 25 м/с², а мотор персонажа без ввода каждый шаг гасит
    /// горизонталь с замедлением 20 м/с² — и съедал почти весь толчок.
    /// На стенде воронка работала все полсекунды, а сдвигала на 0.10–0.14 м:
    /// с 0.53 до 0.41, с 0.27 до 0.17. Досягаемость в 0.58 м не работала
    /// никогда; проходил только тот, кто сам встал ближе 0.22 м к центру,
    /// и это было «встал в позу у самой дырки, а всё равно упал».
    /// Теперь край задаёт поперечную скорость к центру, а считается это
    /// после мотора (<see cref="DefaultExecutionOrderAttribute"/>), по уже
    /// заторможенной скорости. Трос этим не перебит: когда оба стоят в своих
    /// вырезах, он провисает (разнос вырезов не больше 5 ШП при длине 6 ШП),
    /// а убежавший напарник идёт без позы — и воронка ему не помогает.
    /// </remarks>
    [DefaultExecutionOrder(100)]
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
        /// С какой скоростью край ведёт к центру на каждый метр промаха, 1/с.
        /// С <see cref="Lead"/> и нарастанием силы это сводит 0.58 м к 0.08,
        /// а 0.35 — к 0.03: заведомо внутрь допуска 0.15 м. Без перелёта —
        /// сводится по экспоненте, а не качается пружиной.
        /// </summary>
        private const float Gain = 8f;

        /// <summary>
        /// Потолок скорости сведения, м/с. Шаг вбок, а не рывок — и вдвое
        /// ниже порога нокдауна персонажа (5 м/с), чтобы доводка не роняла.
        /// </summary>
        private const float MaxSpeed = 2f;

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

        /// <summary>Сколько шагов физики воронка доводила на текущей стене. Для строки «у себя».</summary>
        public int EngagedSteps { get; private set; }

        /// <summary>Смещение от центра выреза в первый шаг доводки на текущей стене, м.</summary>
        public float EngagedFrom { get; private set; }

        /// <summary>Новая стена — счёт доводки с нуля.</summary>
        public void ResetStats()
        {
            EngagedSteps = 0;
            EngagedFrom = 0f;
        }

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
            if (EngagedSteps++ == 0)
            {
                EngagedFrom = -offset;
            }

            // Мотор в этом шаге уже отработал: скорость тут — после его торможения.
            float lateral = physics != null ? physics.linearVelocity.x : 0f;
            float desired = Mathf.Clamp(Gain * offset, -MaxSpeed, MaxSpeed);
            float next = Mathf.Lerp(lateral, desired, strength);

            float mass = physics != null ? physics.mass : 1f;
            motor.ApplyImpulse(Vector3.right * ((next - lateral) * mass));
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

            // Отсчёт — до того, что наступит раньше: грань у линии проверки
            // или грань у самого тела. Стоящего ближе к стене, чем линия,
            // плиты достают раньше вердикта, и довести его надо к их подходу,
            // а держать — до самой линии, где его разберут.
            float target = Mathf.Max(config.CheckLineZ, motor.Position.z);
            float remaining = (wall.FrontZ - target) / Mathf.Max(0.01f, wall.Speed);
            if (remaining > Lead || wall.FrontZ < config.CheckLineZ)
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
            strength = 1f - Mathf.Clamp01(Mathf.Max(0f, remaining) / Lead);
            return true;
        }
    }
}
