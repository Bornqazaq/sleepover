using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Traps;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Звук «Переноски предмета» — фаза 5.
    ///
    /// <b>Своего состояния и своих сообщений у него нет</b>, как и у эффектов
    /// 4.4: он висит на событиях, которые игра уже поднимает, и ничего не
    /// решает. Убери его — игра не изменится ни на правило.
    ///
    /// Раскачка, утечка и предупреждения показываются без звука.
    ///
    /// Подписка идёт на то же <see cref="WaterCart.WaterChanged"/>, что и у
    /// эффектов, и по той же причине: оно поднимается на каждой машине, а
    /// наклон и срыв ручки считает только сервер. Звук, слышный одному хосту,
    /// означает неверную привязку, а не проблему звука.
    /// </summary>
    public sealed class CarryItemAudio : MonoBehaviour
    {
        [Header("Кого слушаем")]
        [Tooltip("Краны команд: Cart у них выставляется на каждой машине")]
        [SerializeField] private WaterTap[] taps = System.Array.Empty<WaterTap>();

        [Tooltip("Баки команд: по ним слышно слив")]
        [SerializeField] private WaterTank[] tanks = System.Array.Empty<WaterTank>();

        [Tooltip("Ловушки с событием срабатывания: тачка-ловушка")]
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
        private const string CartGrab = "cart_grab";
        private const string HandleBreak = "handle_break";
        private const string CartSplash = "cart_splash";
        private const string PourLoop = "pour_loop";
        private const string PourDone = "pour_done";
        private const string BeamWarn = "beam_warn";
        private const string BarrowTip = "barrow_tip";
        private const string FillLoop = "fill_loop";
        private const string TapLoop = "tap_loop";
        private const string RollLoop = "roll_loop";
        private const string PipeLoop = "pipe_loop";
        private const string BrickHit = "brick_hit";
        private const string LastSeconds = "last_15s";
        private const string RoundEnd = "round_end";

        /// <summary>Сколько секунд слив звучит после последней порции, прежде чем оборваться.</summary>
        private const float PourTail = 0.4f;

        private readonly WaterCart[] watched = new WaterCart[2];
        private readonly bool[] fillPlaying = new bool[2];
        private readonly bool[] rollPlaying = new bool[2];

        private float pourUntil;
        private bool pourPlaying;
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
                    tanks[i].TripFinished += OnTripFinished;
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
                    tanks[i].TripFinished -= OnTripFinished;
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

            // Краны журчат весь раунд: это и ориентир «где мой кран», и
            // телеграф соперника, вернувшегося за водой.
            for (int i = 0; i < taps.Length; i++)
            {
                if (taps[i] != null)
                {
                    player.StartLoop(TapLoop, taps[i].transform.position);
                }
            }
        }

        private void Update()
        {
            WatchCarts();
            WatchRound();
            FollowBeam();
            UpdateCartLoops();
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
        /// Следим за тележками опросом: спавнит их сервер, а <c>Cart</c> у
        /// крана выставляется на каждой машине.
        /// </summary>
        private void WatchCarts()
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

                if (live == null)
                {
                    continue;
                }

                watched[i] = live;
                live.WaterChanged += OnWaterChanged;
                live.Carry.HandleTaken += OnHandleTaken;
            }
        }

        /// <summary>Взялись за поручень: короткий скрип. Событие поднимается на каждой машине из состояния ручек.</summary>
        private void OnHandleTaken(int slot, Igruha.Core.Player.PlayerController carrier)
        {
            if (player != null && carrier != null)
            {
                player.PlayAt(CartGrab, carrier.transform.position);
            }
        }

        /// <summary>
        /// Наполнение и качение — петли по состоянию тележки: флаг наполнения
        /// и скорость едут по сети сами, отдельных сообщений под звук нет.
        /// </summary>
        private void UpdateCartLoops()
        {
            if (player == null)
            {
                return;
            }

            for (int i = 0; i < watched.Length; i++)
            {
                WaterCart cart = watched[i];
                if (cart == null)
                {
                    StopCartLoops(i);
                    continue;
                }

                Vector3 point = cart.transform.position;
                bool filling = cart.IsFilling;
                if (filling != fillPlaying[i])
                {
                    fillPlaying[i] = filling;
                    if (filling) player.StartLoop(FillLoop, point); else player.StopLoop(FillLoop);
                }

                if (filling)
                {
                    player.MoveLoop(FillLoop, point);
                }

                bool rolling = cart.Carry.IsCarried && !cart.IsLost;
                if (rolling != rollPlaying[i])
                {
                    rollPlaying[i] = rolling;
                    if (rolling) player.StartLoop(RollLoop, point); else player.StopLoop(RollLoop);
                }

                if (rolling)
                {
                    player.MoveLoop(RollLoop, point);
                }
            }
        }

        private void StopCartLoops(int index)
        {
            if (player == null)
            {
                return;
            }

            if (fillPlaying[index])
            {
                fillPlaying[index] = false;
                player.StopLoop(FillLoop);
            }

            if (rollPlaying[index])
            {
                rollPlaying[index] = false;
                player.StopLoop(RollLoop);
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


        }

        private void OnWaterChanged(int amount, WaterLossReason reason)
        {
            WaterCart bottle = FirstAlive();
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
                    // Wave/overflow warnings are visual only (IGR-693).
                    break;

                // Удар по тележке: кирпич или ловушка. Разделить их по звуку
                // нечем — причина у обоих одна, — и это честнее, чем
                // угадывать: удар звучит ударом.
                case WaterLossReason.Hit:
                    player.PlayAt(BrickHit, point);
                    break;

                case WaterLossReason.Void:
                case WaterLossReason.RamVictim:
                case WaterLossReason.RamAttacker:
                    player.PlayAt(CartSplash, point);
                    break;

                case WaterLossReason.Shove:
                    player.PlayAt(HandleBreak, point);
                    break;

                // Наполнение — петля по флагу, не по единицам.
                case WaterLossReason.Filled:
                    break;
            }
        }

        /// <summary>Ходка закрыта: тележка слита до дна, слив кончился наградой.</summary>
        private void OnTripFinished()
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

            player.Play(BarrowTip);
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
            rushPlaying = false;
            roundRunning = false;
            for (int i = 0; i < fillPlaying.Length; i++)
            {
                fillPlaying[i] = false;
                rollPlaying[i] = false;
            }

            player.Play(RoundEnd);
        }

        private WaterCart FirstAlive()
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

            watched[index].WaterChanged -= OnWaterChanged;
            watched[index].Carry.HandleTaken -= OnHandleTaken;
            StopCartLoops(index);
            watched[index] = null;
        }
    }
}
