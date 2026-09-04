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
    /// <b>Почему всё держится на одном событии.</b> Единственная точка расхода
    /// воды — <see cref="WaterBottle.WaterSpent"/>, и она поднимается <b>на
    /// каждой машине</b>: у авторитета в <c>SpendWater</c>, у остальных
    /// сообщением о потере. Остальные события переноски — наклон, бросок,
    /// приземление, срыв ручки — считает только сервер (STATE 3.24.3), и эффект
    /// на них был бы виден одному хосту. Тот же класс дыр отдал «Рейсу на
    /// память» целую катку одному хосту, и повторять его незачем: причина
    /// потери приезжает вместе с самой потерей и покрывает все восемь случаев.
    ///
    /// <b>Эффекты живут в сцене, а не на бутыли.</b> Бутыль исчезает в тот же
    /// кадр, в который сливается, — вложенный в неё всплеск погас бы ровно в
    /// тот момент, ради которого его ставили. Пул лежит на арене, эффект
    /// переносится в точку события и играется там.
    /// </summary>
    public sealed class CarryItemEffects : MonoBehaviour
    {
        [Header("За чьими бутылями следим")]
        [Tooltip("Штабели команд. LiveBottle у них выставляется на каждой машине, и это единственный способ найти живую тару")]
        [SerializeField] private BottleStack[] stacks = System.Array.Empty<BottleStack>();

        [Header("Пулы эффектов")]
        [Tooltip("Всплеск воды: слив, падение, таран, пропасть")]
        [SerializeField] private ParticleSystem[] splashes = System.Array.Empty<ParticleSystem>();

        [Tooltip("Брызги вбок: удар по бутыли")]
        [SerializeField] private ParticleSystem[] sprays = System.Array.Empty<ParticleSystem>();

        [Tooltip("Пыль удара о бетон")]
        [SerializeField] private ParticleSystem[] dusts = System.Array.Empty<ParticleSystem>();

        [Tooltip("Свуш броска")]
        [SerializeField] private ParticleSystem[] swooshes = System.Array.Empty<ParticleSystem>();

        [Header("Ловушки")]
        [Tooltip("Ловушки с событием срабатывания: тачка. Балка события не имеет и телеграфирует шлейфом")]
        [SerializeField] private TrapBase[] traps = System.Array.Empty<TrapBase>();

        [Tooltip("Облако пыли, которое играет тачка")]
        [SerializeField] private ParticleSystem cartBurst;

        [Header("Пороги")]
        [Tooltip("Насколько выше точки бутыли играется эффект, м. Ноль — под самое дно")]
        [SerializeField] private float effectLift = 0.35f;

        [Tooltip("Как часто утечка от крена даёт всплеск под ногами, сек")]
        [SerializeField] private float leakSplashInterval = 0.9f;

        private readonly WaterBottle[] watched = new WaterBottle[2];

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
        /// Следим за живой тарой каждого штабеля. Опросом, а не событием:
        /// <c>BottleTaken</c> поднимает только тот, кто выдал бутыль, то есть
        /// сервер, а <c>LiveBottle</c> выставляется на каждой машине — клиент
        /// узнаёт о таре, когда та приезжает и разбирается по штабелю.
        /// </summary>
        private void Update()
        {
            int count = Mathf.Min(stacks.Length, watched.Length);
            for (int i = 0; i < count; i++)
            {
                WaterBottle live = stacks[i] != null ? stacks[i].LiveBottle : null;
                if (live == watched[i])
                {
                    continue;
                }

                Unwatch(i);

                if (live != null)
                {
                    watched[i] = live;
                    live.WaterSpent += OnWaterSpent;
                }
            }
        }

        private void Unwatch(int index)
        {
            if (watched[index] == null)
            {
                return;
            }

            watched[index].WaterSpent -= OnWaterSpent;
            watched[index] = null;
        }

        /// <summary>
        /// Вода ушла — показать, куда именно. Точку берём у той бутыли, которая
        /// сейчас жива: событие приходит без неё, а обеих сразу не бывает —
        /// у каждой команды своя тара, и своя же подписка.
        /// </summary>
        private void OnWaterSpent(int amount, WaterLossReason reason)
        {
            if (!TryFindSource(out Vector3 point))
            {
                return;
            }

            switch (reason)
            {
                // Слив в бак — единственная «потеря», которая идёт в счёт, и
                // единственная, которую игрок хочет видеть как награду.
                case WaterLossReason.Poured:
                    Play(splashes, ref splashIndex, point);
                    break;

                // Удар: брызги вбок и пыль от того, что по бутыли прилетело.
                case WaterLossReason.Hit:
                    Play(sprays, ref sprayIndex, point);
                    Play(dusts, ref dustIndex, point);
                    break;

                case WaterLossReason.Drop:
                case WaterLossReason.RamVictim:
                case WaterLossReason.RamAttacker:
                    Play(splashes, ref splashIndex, point);
                    Play(dusts, ref dustIndex, point);
                    break;

                // Пропасть: всплеск играется там, где бутыль в этот момент, то
                // есть уже внизу. Смотрящий сверху видит, чем кончилось.
                case WaterLossReason.Void:
                    Play(splashes, ref splashIndex, point);
                    break;

                case WaterLossReason.Throw:
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
            if (cartBurst == null)
            {
                return;
            }

            cartBurst.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            cartBurst.Play(true);
        }

        /// <summary>Точка живой бутыли, приподнятая от дна: у тары начало координат в основании.</summary>
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

        /// <summary>Пол под точкой: всплеск от утечки бьёт по бетону, а не по горлышку.</summary>
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
