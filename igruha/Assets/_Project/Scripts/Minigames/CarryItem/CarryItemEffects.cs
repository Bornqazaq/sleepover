using UnityEngine;
using Igruha.Core.Traps;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Эффекты «Переноски предмета» — подфаза 4.4.
    ///
    /// <b>Своего состояния и своих сообщений у него нет.</b> Компонент висит на
    /// событиях, которые игра уже поднимает, и ничего не решает: он не знает ни
    /// про очки, ни про раунд, ни про сеть. Убери его — игра не изменится ни на
    /// правило.
    ///
    /// <b>Почему всё держится на одном событии.</b> Единственная точка
    /// изменения воды — <see cref="WaterCart.WaterChanged"/>, и дискретные
    /// потери с неё поднимаются <b>на каждой машине</b>: у авторитета в
    /// <c>ChangeWater</c>, у остальных сообщением о потере. Наполнение и слив
    /// длятся секундами и событиями не ходят — их струи живут на самой
    /// тележке и включаются по её флагам. Остальные события переноски — крен,
    /// толчок, срыв поручня — считает только сервер, и эффект на них был бы
    /// виден одному хосту.
    ///
    /// <b>Пулы живут в сцене, а не на тележке.</b> Тележка постоянна, но
    /// падает в пропасть и возвращается телепортом — вложенный в неё всплеск
    /// улетел бы вместе с ней. Пул лежит на арене, эффект переносится в точку
    /// события и играется там.
    /// </summary>
    public sealed class CarryItemEffects : MonoBehaviour
    {
        [Header("За чьими тележками следим")]
        [Tooltip("Краны команд. Cart у них выставляется на каждой машине, и это единственный способ найти тележку")]
        [SerializeField] private WaterTap[] taps = System.Array.Empty<WaterTap>();

        [Header("Пулы эффектов")]
        [Tooltip("Всплеск воды: таран, пропасть, утечка")]
        [SerializeField] private ParticleSystem[] splashes = System.Array.Empty<ParticleSystem>();

        [Tooltip("Брызги вбок: удар по тележке")]
        [SerializeField] private ParticleSystem[] sprays = System.Array.Empty<ParticleSystem>();

        [Tooltip("Пыль удара о бетон")]
        [SerializeField] private ParticleSystem[] dusts = System.Array.Empty<ParticleSystem>();

        [Tooltip("Свуш толчка с разгона")]
        [SerializeField] private ParticleSystem[] swooshes = System.Array.Empty<ParticleSystem>();

        [Header("Ловушки")]
        [Tooltip("Ловушки с событием срабатывания: тачка. Балка события не имеет и телеграфирует шлейфом")]
        [SerializeField] private TrapBase[] traps = System.Array.Empty<TrapBase>();

        [Tooltip("Облако пыли, которое играет тачка-ловушка")]
        [SerializeField] private ParticleSystem barrowBurst;

        [Header("Пороги")]
        [Tooltip("Насколько выше точки тележки играется эффект, м. Ноль — под самое дно")]
        [SerializeField] private float effectLift = 0.35f;

        [Tooltip("Как часто утечка от крена даёт всплеск под ногами, сек")]
        [SerializeField] private float leakSplashInterval = 0.9f;

        private readonly WaterCart[] watched = new WaterCart[2];

        private int splashIndex;
        private int sprayIndex;
        private int dustIndex;
        private int swooshIndex;
        private float nextLeakSplash;

        private void OnEnable()
        {
            for (int i = 0; i < traps.Length; i++)
            {
                if (traps[i] != null)
                {
                    traps[i].Fired += OnTrapFired;
                }
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < traps.Length; i++)
            {
                if (traps[i] != null)
                {
                    traps[i].Fired -= OnTrapFired;
                }
            }

            for (int i = 0; i < watched.Length; i++)
            {
                Unwatch(i);
            }
        }

        /// <summary>
        /// Следим за тележкой каждого крана. Опросом, а не событием: тележку
        /// спавнит сервер, а <c>Cart</c> у крана выставляется на каждой машине
        /// — клиент узнаёт о ней, когда та приезжает и разбирается по крану.
        /// </summary>
        private void Update()
        {
            int count = Mathf.Min(taps.Length, watched.Length);
            for (int i = 0; i < count; i++)
            {
                WaterCart live = taps[i] != null ? taps[i].Cart : null;
                if (live == watched[i])
                {
                    continue;
                }

                Unwatch(i);

                if (live != null)
                {
                    watched[i] = live;
                    live.WaterChanged += OnWaterChanged;
                }
            }
        }

        private void Unwatch(int index)
        {
            if (watched[index] == null)
            {
                return;
            }

            watched[index].WaterChanged -= OnWaterChanged;
            watched[index] = null;
        }

        /// <summary>
        /// Вода ушла — показать, куда именно. Точку берём у тележки, которая
        /// это событие подняла: у каждой команды своя тележка и своя подписка,
        /// а событие приходит без ссылки — берём первую живую из двух.
        /// </summary>
        private void OnWaterChanged(int amount, WaterLossReason reason)
        {
            if (!TryFindSource(out Vector3 point))
            {
                return;
            }

            switch (reason)
            {
                // Слив и наполнение длятся секундами: их струи живут на самой
                // тележке и включаются по флагам. Здесь им делать нечего.
                case WaterLossReason.Poured:
                case WaterLossReason.Filled:
                    break;

                // Удар: брызги вбок и пыль от того, что по тележке прилетело.
                case WaterLossReason.Hit:
                    Play(sprays, ref sprayIndex, point);
                    Play(dusts, ref dustIndex, point);
                    break;

                case WaterLossReason.RamVictim:
                case WaterLossReason.RamAttacker:
                    Play(splashes, ref splashIndex, point);
                    Play(dusts, ref dustIndex, point);
                    break;

                // Пропасть: всплеск играется там, где тележка в этот момент, то
                // есть уже внизу. Смотрящий сверху видит, чем кончилось.
                case WaterLossReason.Void:
                    Play(splashes, ref splashIndex, point);
                    break;

                case WaterLossReason.Shove:
                    Play(swooshes, ref swooshIndex, point);
                    break;

                // Утечка от крена капает каждую секунду, и всплеск на каждый
                // тик слился бы в сплошную стену брызг. Реже — и лужа под
                // ногами читается лужей, а не фонтаном.
                case WaterLossReason.Tilt:
                    if (Time.time >= nextLeakSplash)
                    {
                        nextLeakSplash = Time.time + leakSplashInterval;
                        Play(splashes, ref splashIndex, GroundUnder(point));
                    }

                    break;
            }
        }

        private void OnTrapFired()
        {
            if (barrowBurst == null)
            {
                return;
            }

            barrowBurst.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            barrowBurst.Play(true);
        }

        /// <summary>Точка тележки, приподнятая от дна: у неё начало координат в основании между колёс.</summary>
        private bool TryFindSource(out Vector3 point)
        {
            for (int i = 0; i < watched.Length; i++)
            {
                if (watched[i] != null)
                {
                    point = watched[i].transform.position + Vector3.up * effectLift;
                    return true;
                }
            }

            point = Vector3.zero;
            return false;
        }

        /// <summary>Пол под точкой: всплеск от утечки бьёт по бетону, а не по борту.</summary>
        private static Vector3 GroundUnder(Vector3 point)
        {
            return new Vector3(point.x, 0.05f, point.z);
        }

        /// <summary>
        /// Сыграть следующий эффект пула в точке. По кругу, а не поиском
        /// свободного: восемь игроков теряют воду пачками, и перебор пула на
        /// каждую потерю — это работа в кадре ради того, что и так не видно.
        /// </summary>
        private static void Play(ParticleSystem[] pool, ref int index, Vector3 point)
        {
            if (pool == null || pool.Length == 0)
            {
                return;
            }

            ParticleSystem effect = pool[index % pool.Length];
            index = (index + 1) % pool.Length;

            if (effect == null)
            {
                return;
            }

            effect.transform.position = point;
            effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            effect.Play(true);
        }
    }
}
