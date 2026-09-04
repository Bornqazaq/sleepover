using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Traps;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Звук «Переноски предмета» — подфаза 4.5.
    ///
    /// <b>Своего состояния и своих сообщений у него нет</b>, как и у эффектов
    /// 4.4: он висит на событиях, которые игра уже поднимает, и ничего не
    /// решает. Убери его — игра не изменится ни на правило.
    ///
    /// <b>Половина обратной связи этой игры — на слух.</b> Воду теряешь за
    /// спиной: бутыль несут вчетвером, смотришь ты вперёд, а льётся она из
    /// горлышка позади. Плеск говорит «сейчас начнёт», струя — «уже теряешь»,
    /// и это два разных звука, потому что означают они разное.
    ///
    /// Подписка идёт на то же <see cref="WaterBottle.WaterSpent"/>, что и у
    /// эффектов, и по той же причине: оно поднимается на каждой машине, а
    /// наклон и срыв ручки считает только сервер. Звук, слышный одному хосту,
    /// означает неверную привязку, а не проблему звука.
    /// </summary>
    public sealed class CarryItemAudio : MonoBehaviour
    {
        [Header("Кого слушаем")]
        [Tooltip("Штабели команд: LiveBottle у них выставляется на каждой машине")]
        [SerializeField] private BottleStack[] stacks = System.Array.Empty<BottleStack>();

        [Tooltip("Баки команд: по ним слышно слив")]
        [SerializeField] private WaterTank[] tanks = System.Array.Empty<WaterTank>();

        [Tooltip("Ловушки с событием срабатывания: тачка")]
        [SerializeField] private TrapBase[] traps = System.Array.Empty<TrapBase>();

        [Header("Чем играем")]
        [SerializeField] private MinigameAudioPlayer player;

        [Tooltip("Таймер раунда: по нему слышно старт, последние секунды и конец")]
        [SerializeField] private Igruha.Core.Minigame.RoundTimer roundTimer;

        [Tooltip("За сколько секунд до конца включается подгоняющий тик")]
        [SerializeField] private float rushSeconds = 15f;

        [Header("Точки постоянных звуков")]
        [Tooltip("Прорванная труба: её гул идёт весь раунд с этой точки")]
        [SerializeField] private Transform pipe;

        [Tooltip("Ось горлышка: оттуда слышен скрип троса балки")]
        [SerializeField] private Transform beam;

        /// <summary>Идентификаторы слотов. Строка в коде — это опечатка, которая молчит.</summary>
        private const string RoundStart = "round_start";
        private const string BottleTake = "bottle_take";
        private const string SloshLoop = "slosh_loop";
        private const string LeakLoop = "leak_loop";
        private const string HandleBreak = "handle_break";
        private const string BottleDrop = "bottle_drop";
        private const string PourLoop = "pour_loop";
        private const string PourDone = "pour_done";
        private const string BeamWarn = "beam_warn";
        private const string CartTip = "cart_tip";
        private const string PipeLoop = "pipe_loop";
        private const string BrickHit = "brick_hit";
        private const string LastSeconds = "last_15s";
        private const string RoundEnd = "round_end";

        /// <summary>Сколько секунд слив звучит после последней порции, прежде чем оборваться.</summary>
        private const float PourTail = 0.4f;

        /// <summary>Сколько секунд утечка звучит после последней потери от крена.</summary>
        private const float LeakTail = 1.4f;

        private readonly WaterBottle[] watched = new WaterBottle[2];

        private float pourUntil;
        private float leakUntil;
        private bool pourPlaying;
        private bool leakPlaying;
        private bool sloshPlaying;
        private bool roundRunning;
        private bool rushPlaying;

        private void OnEnable()
        {
            for (int i = 0; i < traps.Length; i++)
            {
                if (traps[i] != null)
                {
                    traps[i].Fired += OnTrapFired;
                }
            }

            for (int i = 0; i < tanks.Length; i++)
            {
                if (tanks[i] != null)
                {
                    tanks[i].BottleFinished += OnBottleFinished;
                }
            }

            if (roundTimer != null)
            {
                roundTimer.Finished += OnRoundFinished;
            }

            StartAmbience();
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

            for (int i = 0; i < tanks.Length; i++)
            {
                if (tanks[i] != null)
                {
                    tanks[i].BottleFinished -= OnBottleFinished;
                }
            }

            if (roundTimer != null)
            {
                roundTimer.Finished -= OnRoundFinished;
            }

            for (int i = 0; i < watched.Length; i++)
            {
                Unwatch(i);
            }

            if (player != null)
            {
                player.StopAll();
            }

            pourPlaying = false;
            leakPlaying = false;
            sloshPlaying = false;
            rushPlaying = false;
            roundRunning = false;
        }

        /// <summary>
        /// Постоянные звуки арены: труба течёт весь раунд, трос балки скрипит
        /// весь раунд. Оба привязаны к точке, а не к слушателю: слышно их тем
        /// сильнее, чем ближе подошёл, и это и есть телеграф.
        /// </summary>
        private void StartAmbience()
        {
            if (player == null)
            {
                return;
            }

            if (pipe != null)
            {
                player.StartLoop(PipeLoop, pipe.position);
            }

            if (beam != null)
            {
                player.StartLoop(BeamWarn, beam.position);
            }
        }

        private void Update()
        {
            WatchBottles();
            WatchRound();
            FollowBeam();
            UpdateSlosh();
            UpdateTails();
        }

        /// <summary>
        /// Старт и последние секунды ловятся по таймеру раунда, а не вызовом
        /// из правил игры: правила про звук знать не обязаны, а таймер и так
        /// живёт на каждой машине и идёт у всех одинаково.
        /// </summary>
        private void WatchRound()
        {
            if (player == null || roundTimer == null)
            {
                return;
            }

            if (roundTimer.IsRunning != roundRunning)
            {
                roundRunning = roundTimer.IsRunning;
                if (roundRunning)
                {
                    player.Play(RoundStart);
                }
            }

            bool rush = roundRunning && roundTimer.Remaining <= rushSeconds && roundTimer.Remaining > 0f;
            if (rush == rushPlaying)
            {
                return;
            }

            rushPlaying = rush;
            if (rush)
            {
                player.StartLoop(LastSeconds);
            }
            else
            {
                player.StopLoop(LastSeconds);
            }
        }

        /// <summary>
        /// Следим за живой тарой опросом: <c>BottleTaken</c> поднимает только
        /// сервер, а <c>LiveBottle</c> выставляется на каждой машине.
        /// </summary>
        private void WatchBottles()
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

                if (live == null)
                {
                    continue;
                }

                watched[i] = live;
                live.WaterSpent += OnWaterSpent;

                if (player != null)
                {
                    player.PlayAt(BottleTake, live.transform.position);
                }
            }
        }

        /// <summary>Скрип троса едет за балкой: она ходит над горлышком весь раунд.</summary>
        private void FollowBeam()
        {
            if (player != null && beam != null)
            {
                player.MoveLoop(BeamWarn, beam.position);
            }
        }

        /// <summary>
        /// Плеск ведётся за креном той бутыли, что сейчас жива: чем ближе к
        /// порогу, тем громче и выше. Это единственное предупреждение, которое
        /// несущий получает <b>до</b> того, как вода пошла.
        /// </summary>
        private void UpdateSlosh()
        {
            if (player == null)
            {
                return;
            }

            WaterBottle bottle = FirstAlive();
            if (bottle == null || bottle.Carry == null || !bottle.Carry.IsCarried)
            {
                if (sloshPlaying)
                {
                    player.StopLoop(SloshLoop);
                    sloshPlaying = false;
                }

                return;
            }

            float threshold = Mathf.Max(1f, bottle.Carry.Settings.tiltThreshold);
            float intensity = Mathf.Clamp01(bottle.Carry.TiltAngle / threshold);

            if (!sloshPlaying)
            {
                player.StartLoop(SloshLoop, bottle.transform.position);
                sloshPlaying = true;
            }

            player.MoveLoop(SloshLoop, bottle.transform.position);
            player.SetLoopLevel(SloshLoop, intensity, Mathf.Lerp(0.9f, 1.3f, intensity));
        }

        /// <summary>
        /// Слив и утечка приходят порциями раз в секунду, а звучать обязаны
        /// сплошняком. Луп держится хвостом после последней порции: иначе он
        /// дёргался бы вместе с расходом.
        /// </summary>
        private void UpdateTails()
        {
            if (player == null)
            {
                return;
            }

            if (pourPlaying && Time.time > pourUntil)
            {
                player.StopLoop(PourLoop);
                pourPlaying = false;
            }

            if (leakPlaying && Time.time > leakUntil)
            {
                player.StopLoop(LeakLoop);
                leakPlaying = false;
            }
        }

        private void OnWaterSpent(int amount, WaterLossReason reason)
        {
            WaterBottle bottle = FirstAlive();
            Vector3 point = bottle != null ? bottle.transform.position : transform.position;
            if (player == null)
            {
                return;
            }

            switch (reason)
            {
                case WaterLossReason.Poured:
                    pourUntil = Time.time + PourTail;
                    if (!pourPlaying)
                    {
                        player.StartLoop(PourLoop, point);
                        pourPlaying = true;
                    }

                    player.MoveLoop(PourLoop, point);
                    break;

                case WaterLossReason.Tilt:
                    leakUntil = Time.time + LeakTail;
                    if (!leakPlaying)
                    {
                        player.StartLoop(LeakLoop, point);
                        leakPlaying = true;
                    }

                    player.MoveLoop(LeakLoop, point);
                    break;

                // Удар по бутыли: игрок, кирпич или ловушка. Разделить их по
                // звуку нечем — причина у всех трёх одна, — и это честнее, чем
                // угадывать: удар звучит ударом.
                case WaterLossReason.Hit:
                    player.PlayAt(BrickHit, point);
                    break;

                case WaterLossReason.Drop:
                case WaterLossReason.Void:
                case WaterLossReason.RamVictim:
                case WaterLossReason.RamAttacker:
                    player.PlayAt(BottleDrop, point);
                    break;

                case WaterLossReason.Throw:
                    player.PlayAt(HandleBreak, point);
                    break;
            }
        }

        /// <summary>Ходка закрыта: бак отпустил бутыль, значит слив кончился наградой.</summary>
        private void OnBottleFinished()
        {
            if (player == null)
            {
                return;
            }

            player.Play(PourDone);
        }

        private void OnTrapFired()
        {
            if (player == null)
            {
                return;
            }

            player.Play(CartTip);
        }

        /// <summary>
        /// Конец раунда: гасим всё, включая постоянные лупы, и только потом
        /// гудок. Иначе он тонет в трубе, тике и плеске — а он тут последнее,
        /// что игрок обязан услышать.
        /// </summary>
        private void OnRoundFinished()
        {
            if (player == null)
            {
                return;
            }

            player.StopAll();
            pourPlaying = false;
            leakPlaying = false;
            sloshPlaying = false;
            rushPlaying = false;
            roundRunning = false;
            player.Play(RoundEnd);
        }

        private WaterBottle FirstAlive()
        {
            for (int i = 0; i < watched.Length; i++)
            {
                if (watched[i] != null)
                {
                    return watched[i];
                }
            }

            return null;
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
    }
}
