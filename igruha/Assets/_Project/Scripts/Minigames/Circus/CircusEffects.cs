using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>
    /// Эффекты цирковой арены — подфаза 4.4, общие на «Секундомер» и «Порядок
    /// банок». Труха из-под опускающейся клетки, облако опилок под
    /// раскрывшимися створками, искры от лапы медведя по решётке, удар при
    /// поимке и конфетти на итогах.
    ///
    /// <b>Своего состояния здесь нет ни на копейку, и это главное правило
    /// подфазы.</b> Каждый эффект висит на том, что игра уже посчитала и уже
    /// показала всем:
    /// <list type="bullet">
    /// <item>спуск клетки — <see cref="CageStation.Descending"/>, то есть
    /// движение платформы, которую сервер уже синхронизировал;</item>
    /// <item>створки — событие <see cref="CageStation.DoorsOpened"/>;</item>
    /// <item>медведь — <see cref="PitBear.State"/> и <see cref="PitBear.Caught"/>,
    /// оба приезжают с сервера через <c>ApplyNetworkBearState</c>;</item>
    /// <item>итоги — <see cref="MinigameControllerBase.ResultsReported"/>,
    /// который поднимается и у хоста, и у клиента (у клиента через
    /// <c>ApplyResults</c>).</item>
    /// </list>
    /// Ни одного нового RPC, ни одной новой переменной, <c>Core/</c> не тронут
    /// вовсе. Отсюда же следует, что эффекты видны у всех и в один момент:
    /// они чистая функция от того, что и так одинаково на каждой машине.
    ///
    /// <b>Общие, а не «секундомерные».</b> Клетки опускаются, створки
    /// открываются и медведь ловит одинаково в обеих играх. Заведи это внутри
    /// «Секундомера» — и «Порядку банок» пришлось бы писать то же самое второй
    /// раз.
    /// </summary>
    /// <remarks>
    /// <b>Что где лежит и почему.</b> Труха спуска — единственное, что
    /// прицеплено к клетке: клетка никуда не девается, а труха обязана ехать
    /// вместе с ней. Всё остальное лежит отдельной группой и лишь
    /// переставляется в точку перед запуском. Облако опилок особенно: вложить
    /// его в створку было бы естественнее всего — и оно погасло бы ровно в тот
    /// момент, ради которого его ставили, потому что створка распахивается
    /// и уносит его с собой.
    ///
    /// <b>Партиклы пака запускаются вручную.</b> У всех FX HorrorCarnival
    /// в префабе стоит <c>playOnAwake</c>, а у половины ещё и зацикливание:
    /// без правки арена встретила бы игрока восемью облаками пыли и
    /// непрерывным конфетти. Автостарт снимает построитель
    /// <c>CircusVfx</c>; зацикленными остаются только туман ямы и пыль
    /// в лучах — это не события, а воздух шатра.
    /// </remarks>
    public sealed class CircusEffects : MonoBehaviour
    {
        [Tooltip("Труха из-под пола опускающейся клетки. По одной на клетку, прицеплена к ней")]
        [SerializeField] private ParticleSystem[] descentDust;

        [Tooltip("Облако опилок в яме под раскрывшимися створками. Переставляется, не вложено")]
        [SerializeField] private ParticleSystem landingBurst;

        [Tooltip("Искры от лапы медведя по решётчатому полу нижней клетки")]
        [SerializeField] private ParticleSystem tauntSparks;

        [Tooltip("Удар в момент поимки игрока медведем")]
        [SerializeField] private ParticleSystem catchImpact;

        [Tooltip("Конфетти на итогах мини-игры, над центром арены")]
        [SerializeField] private ParticleSystem confetti;

        [Tooltip("Клетки арены. Заполняет билдер")]
        [SerializeField] private CageStation[] cages;

        [SerializeField] private PitBear bear;

        [SerializeField] private MinigameControllerBase controller;

        /// <summary>Как часто пробуем искры, пока медведь дразнит. Реже — рвано, чаще — сплошная полоса.</summary>
        private const float TauntSparkPeriod = 0.55f;

        /// <summary>На сколько облако опилок отступает от дна ямы, м.</summary>
        private const float LandingLift = 0.15f;

        private readonly List<bool> wasDescending = new List<bool>(8);
        private float tauntTimer;

        private void Awake()
        {
            wasDescending.Clear();
            for (int i = 0; i < CageCount; i++)
            {
                wasDescending.Add(false);
            }
        }

        private void OnEnable()
        {
            for (int i = 0; i < CageCount; i++)
            {
                if (cages[i] != null)
                {
                    cages[i].DoorsOpened += OnDoorsOpened;
                }
            }

            if (bear != null)
            {
                bear.Caught += OnBearCaught;
            }

            if (controller != null)
            {
                controller.ResultsReported += OnResults;
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < CageCount; i++)
            {
                if (cages[i] != null)
                {
                    cages[i].DoorsOpened -= OnDoorsOpened;
                }
            }

            if (bear != null)
            {
                bear.Caught -= OnBearCaught;
            }

            if (controller != null)
            {
                controller.ResultsReported -= OnResults;
            }
        }

        private int CageCount => cages == null ? 0 : cages.Length;

        private void Update()
        {
            TickDescent();
            TickTaunt(Time.deltaTime);
        }

        /// <summary>
        /// Труха идёт, пока клетка едет. Читаем движение платформы, а не
        /// событие: событие о начале спуска игра не поднимает, а платформа
        /// синхронизирована и так — значит труха включится и выключится
        /// на каждой машине в один и тот же момент.
        /// </summary>
        private void TickDescent()
        {
            for (int i = 0; i < CageCount && i < wasDescending.Count; i++)
            {
                CageStation cage = cages[i];
                if (cage == null || descentDust == null || i >= descentDust.Length)
                {
                    continue;
                }

                ParticleSystem dust = descentDust[i];
                if (dust == null)
                {
                    continue;
                }

                bool moving = cage.Descending;
                if (moving == wasDescending[i])
                {
                    continue;
                }

                wasDescending[i] = moving;
                if (moving)
                {
                    dust.Play(true);
                }
                else
                {
                    // Stop с прекращением эмиссии, но без уборки уже живых
                    // частиц: клетка встала, а труха обязана досыпаться.
                    dust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        /// <summary>
        /// Пока медведь дразнит нижнюю клетку, он бьёт лапой по решётке.
        /// Искры ставим у пола самой низкой клетки — той, к которой он и
        /// тянется.
        /// </summary>
        private void TickTaunt(float deltaTime)
        {
            if (bear == null || tauntSparks == null || bear.State != PitBear.BearState.Taunt)
            {
                tauntTimer = 0f;
                return;
            }

            tauntTimer -= deltaTime;
            if (tauntTimer > 0f)
            {
                return;
            }

            tauntTimer = TauntSparkPeriod;
            if (!TryFindLowestCage(out Vector3 floor))
            {
                return;
            }

            tauntSparks.transform.position = floor;
            tauntSparks.Play(true);
        }

        private bool TryFindLowestCage(out Vector3 floor)
        {
            floor = Vector3.zero;
            float lowest = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < CageCount; i++)
            {
                if (cages[i] == null)
                {
                    continue;
                }

                float y = cages[i].transform.position.y;
                if (y >= lowest)
                {
                    continue;
                }

                lowest = y;
                floor = cages[i].transform.position;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Створки раскрылись — облако опилок внизу, там, где игрок приземлится.
        /// Не у створок: смотреть в этот момент будут в яму, а не под клетку.
        /// </summary>
        private void OnDoorsOpened(CageStation cage)
        {
            if (landingBurst == null || cage == null)
            {
                return;
            }

            Vector3 at = cage.transform.position;
            landingBurst.transform.position = new Vector3(at.x, LandingLift, at.z);
            landingBurst.Play(true);
        }

        private void OnBearCaught(PlayerController player, Vector3 hitPoint)
        {
            if (catchImpact == null)
            {
                return;
            }

            catchImpact.transform.position = hitPoint;
            catchImpact.Play(true);
        }

        private void OnResults(MinigameResults results)
        {
            if (confetti != null)
            {
                confetti.Play(true);
            }
        }
    }
}
