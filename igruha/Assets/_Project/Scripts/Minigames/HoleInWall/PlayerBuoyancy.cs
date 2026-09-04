using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Вода бассейна: упавший всплывает и качается на поверхности, а не ходит
    /// по дну.
    ///
    /// Вешается на аватар в начале раунда рядом с <see cref="PlayerPoseAbility"/>
    /// и <see cref="WallFunnel"/> и снимается в конце — префабы персонажей
    /// заморожены (igruha/CLAUDE.md, раздел 🔒 0).
    /// </summary>
    /// <remarks>
    /// <b>Что было.</b> Дно бассейна — коллайдер на слое <c>Ground</c> в 2.16 м
    /// под водой. Сметённый долетал до него, вставал и <b>ходил по дну обычным
    /// шагом</b>, полностью под водой. На прогоне 04.09 это названо главным,
    /// что портит вид провала.
    ///
    /// <b>Почему нельзя было починить одной анимацией.</b> Клипа плавания
    /// в проекте нет и заводить его нельзя — аниматоры восьмерых заморожены.
    /// Зато драйвер анимации (<c>CharacterAnimatorDriver</c>) знает только
    /// <c>Speed</c> и разовый триггер прыжка: у стоящего на месте в воде
    /// играет <c>Idle</c>. Значит достаточно поднять человека на поверхность
    /// и погасить ему скорость — и он читается стоящим в воде по грудь,
    /// без единого нового клипа.
    ///
    /// <b>Выталкивание считается по Архимеду, а не пружиной к заданной высоте.</b>
    /// Сила вверх пропорциональна доле тела под водой, и равновесие получается
    /// само там, где вытесненный объём уравновешивает вес —
    /// <see cref="FloatingShare"/>. Пружина к абсолютной высоте дала бы всем
    /// восьмерым одинаковую ватерлинию, а Шланга на 35 см выше Карлана.
    ///
    /// <b>Гашение здесь же, и оно важнее выталкивания.</b> Сметённый входит
    /// в воду на 8–10 м/с; без гашения он пробил бы 2.16 м бассейна до дна
    /// и проехал по нему до края — а за краем бассейна пусто. С гашением
    /// он тормозит за треть секунды и метр пути, то есть не достаёт ни до дна,
    /// ни до борта.
    ///
    /// <b>Сеть: каждая машина ведёт своего.</b> Вода — непрерывная сила, как
    /// воронка выреза и трос: применяет её владелец персонажа напрямую, иначе
    /// это полсотни пакетов в секунду на игрока. Исход по-прежнему считает
    /// сервер и по своим числам — высоту тела он видит и так.
    /// </remarks>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerBuoyancy : MonoBehaviour
    {
        /// <summary>
        /// Какая доля тела остаётся под водой в покое. 0.75 — человек по грудь:
        /// голова и плечи над водой, остальное под ней.
        ///
        /// Это же число задаёт силу выталкивания: при полном погружении она
        /// в <c>1 / FloatingShare</c> раза больше веса, при этой доле — ровно
        /// вес, и глубже тело не тонет.
        /// </summary>
        private const float FloatingShare = 0.75f;

        /// <summary>
        /// Гашение вертикали в воде, 1/с. Подобрано под вход сметённого,
        /// а он приходит быстро: импульс кидает его вверх на 3.4 м/с, дальше
        /// 3.15 м падения при тройной гравитации — <b>13.6 м/с</b> на входе
        /// в воду. Десятка даёт на них 136 м/с², то есть потолок
        /// <see cref="MaxAcceleration"/>; остановка занимает 0.22 с и около
        /// 1.5 м. Бассейн глубиной 2.16 м — дна он не достаёт.
        /// </summary>
        private const float VerticalDamping = 10f;

        /// <summary>
        /// Гашение горизонтали, 1/с. Ниже вертикального: вода тормозит,
        /// но барахтаться в сторону лестницы должно быть можно.
        ///
        /// Оно же держит человека внутри бассейна. Сметённый летит назад
        /// на 9.7 м/с и падает в воду в 6 м от платформы, а борт всего
        /// в 7.56 м: без гашения он проезжал бы по дну до края и уходил
        /// за него в пустоту.
        /// </summary>
        private const float HorizontalDamping = 6f;

        /// <summary>
        /// Потолок ускорения от воды, м/с². Ниже — и сметённый на 13.6 м/с
        /// пробивает бассейн до дна; выше — вход в воду выглядит ударом
        /// о батут, а не всплеском.
        ///
        /// Порог нокдауна им не пробить: за такт это 1.8 м/с против
        /// <c>knockdownVelocityThreshold = 5</c>, иначе вода валила бы
        /// с ног сама.
        /// </summary>
        private const float MaxAcceleration = 90f;

        /// <summary>Потолок скорости в воде, м/с. Плыть можно, убежать — нет.</summary>
        private const float SwimSpeed = 1.6f;

        /// <summary>Разгон в воде — доля обычного. Вода не даёт стартовать рывком.</summary>
        private const float SwimAcceleration = 0.35f;

        /// <summary>Торможение в воде — доля обычного. Ниже единицы: в воде не встают как вкопанные.</summary>
        private const float SwimDeceleration = 0.5f;

        /// <summary>Рост, которым считается погружение, если капсулы почему-то нет, м.</summary>
        private const float FallbackHeight = 1.8f;

        private PlayerController motor;
        private NetworkObject body;
        private Rigidbody physics;
        private CapsuleCollider capsule;
        private HoleInWallConfig config;

        /// <summary>Человек сейчас в воде. По ней же ставится и снимается вязкость.</summary>
        private bool submerged;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            body = GetComponent<NetworkObject>();
            physics = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
        }

        private void OnDisable() => Release();

        /// <summary>Указать воде её уровень. Зовётся сразу после навешивания компонента.</summary>
        public void Configure(HoleInWallConfig gameConfig)
        {
            config = gameConfig;
        }

        /// <summary>
        /// Отпустить персонажа: раунд кончился. Вязкость снимается обязательно —
        /// персонаж переезжает в хаб живым, и незакрытый потолок скорости уехал
        /// бы вместе с ним.
        /// </summary>
        public void Release()
        {
            config = null;
            SetSubmerged(false);
        }

        private void FixedUpdate()
        {
            if (config == null || physics == null || motor == null)
            {
                return;
            }

            // 🔴 Своего персонажа ведёт только его машина. Чужую копию двигает
            // сетевой транспорт, и вторая сила по ней — это дрожь на всех
            // экранах, кроме одного. Тот же приём, что у воронки и троса.
            if (!WorldAuthority.DrivenHere(body))
            {
                return;
            }

            float height = BodyHeight;
            float feetY = motor.Position.y;

            // Доля тела под водой. Ноги выше уровня — воды нет вовсе.
            float share = Mathf.Clamp01((config.WaterSurfaceY - feetY) / height);
            SetSubmerged(share > 0f);

            if (share <= 0f)
            {
                return;
            }

            Vector3 velocity = physics.linearVelocity;

            // Гравитация, которую надо перебить, — та же, что накидывает
            // PlayerController поверх физической: на взлёте одна, на падении
            // другая. Считать по общей означало бы недодавать выталкивания
            // ровно там, где человек падает в воду.
            float gravity = -Physics.gravity.y * (velocity.y > 0f
                ? motor.Config.RiseGravityMultiplier
                : motor.Config.FallGravityMultiplier);

            float vertical = gravity * (share / FloatingShare) - velocity.y * VerticalDamping;
            vertical = Mathf.Clamp(vertical, -MaxAcceleration, MaxAcceleration);

            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 drag = Vector3.ClampMagnitude(-horizontal * HorizontalDamping, MaxAcceleration);

            // Долей погружения умножается и торможение: по колено в воде она
            // мешать не должна.
            Vector3 acceleration = Vector3.up * vertical + drag * share;

            motor.ApplyImpulse(acceleration * (Time.fixedDeltaTime * physics.mass));
        }

        /// <summary>
        /// Вязкость воды: потолок скорости и ленивый разгон. Ставится и
        /// снимается по переходу, а не каждый такт — источник у обоих
        /// множителей общий, и лишние присвоения затирали бы чужие.
        /// </summary>
        private void SetSubmerged(bool value)
        {
            if (submerged == value)
            {
                return;
            }

            submerged = value;

            if (value)
            {
                motor.ApplySpeedCap(this, SwimSpeed);
                motor.ApplySurface(this, SwimAcceleration, SwimDeceleration);
                return;
            }

            motor.ClearSpeedCap(this);
            motor.ClearSurface(this);
        }

        /// <summary>
        /// Рост тела, м. Берётся у капсулы, а не из конфига: капсулы персонажей
        /// заморожены и разные — у Карлана 1.61 м, у Шланги 1.96, — и ватерлиния
        /// обязана считаться каждому своя.
        /// </summary>
        private float BodyHeight
        {
            get
            {
                if (capsule == null)
                {
                    return FallbackHeight;
                }

                float scale = Mathf.Abs(transform.lossyScale.y);
                return Mathf.Max(0.1f, capsule.height * scale);
            }
        }
    }
}
