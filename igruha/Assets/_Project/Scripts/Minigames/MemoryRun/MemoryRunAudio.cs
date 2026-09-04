using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>
    /// Звук «Рейса на память» — подфаза 4.5.
    ///
    /// <b>Своего состояния и своих RPC нет.</b> Компонент висит на том, что игра
    /// уже подняла на каждой машине, и ничего в сеть не добавляет.
    ///
    /// Крючков ровно три, и выбор каждого не случаен:
    ///
    /// <list type="bullet">
    /// <item><see cref="MemoryRunMinigame.MineDetonated"/> — единственное
    /// событие игры, объявленное на каждой машине. Взрыв слышат все, потому
    /// что все его и видели.</item>
    /// <item>Фаза раунда и номер ходящего — реплицированное состояние, у всех
    /// одинаковое.</item>
    /// <item>Всё остальное — <b>наблюдение за идущим у себя</b>: приземление,
    /// падение в провал, приход на выход. Так сделано не от лени.
    /// <c>SafePlateProved</c> и <c>PlayerFinished</c> поднимаются <b>только
    /// на сервере</b>, а <c>ApplyReplicated</c> событий не поднимает вовсе —
    /// повесить на них звук значило бы, что клиенты играют в немой игре.
    /// Наблюдение же работает у всех одинаково: позиция идущего
    /// реплицирована.</item>
    /// </list>
    ///
    /// 🔴 <b>Отдельного «ты угадал» здесь нет и быть не может.</b> Звук
    /// приземления одинаков на любой плите — и на безопасной, и на мине.
    /// Игроку он не добавляет ничего сверх отсутствия взрыва, но зрителю,
    /// который смотрит в сторону, отдельный сигнал сказал бы то, чего он не
    /// видел, а вся игра построена на том, что информацию добывают глазами
    /// (спека, раздел 9.4).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MemoryRunAudio : MonoBehaviour
    {
        private const string SlotAmbience = "hall_ambience";
        private const string SlotRoundStart = "round_start";
        private const string SlotRoundEnd = "round_end";
        private const string SlotTurnAnnounce = "turn_announce";
        private const string SlotPlateLand = "plate_land";
        private const string SlotMineBlast = "mine_blast";
        private const string SlotRagdollLaunch = "ragdoll_launch";
        private const string SlotRagdollLand = "ragdoll_land";
        private const string SlotCrowdGasp = "crowd_gasp";
        private const string SlotPitFall = "pit_fall";
        private const string SlotTurnTick = "turn_tick";
        private const string SlotExitDoor = "exit_door";
        private const string SlotExitFanfare = "exit_fanfare";

        [SerializeField] private MinigameAudioPlayer audioPlayer;
        [SerializeField] private MemoryRunMinigame game;
        [SerializeField] private MemoryRunConfig config;

        [Tooltip("За сколько секунд до конца хода начинает щёлкать отсчёт")]
        [SerializeField] private float tickFromSeconds = 5f;

        [Tooltip("Ниже этой отметки идущий уже падает в провал, а не оступился")]
        [SerializeField] private float pitFallY = -1.5f;

        [Tooltip("Сколько ждать падения тела после взрыва, прежде чем бросить ожидание")]
        [SerializeField] private float ragdollWatchSeconds = 3f;

        private MinigamePhase lastPhase = MinigamePhase.Idle;
        private int lastWalkerId = TurnQueue.NoPlayer;
        private PlayerController walker;

        /// <summary>
        /// Ридер ввода идущего. Кэшируется вместе с ним, а не спрашивается
        /// каждый кадр: <c>GetComponent</c> в <c>Update</c> правила проекта
        /// запрещают, а спрашивать его приходилось бы всё время, пока идёт ход.
        /// </summary>
        private PlayerInputReader walkerInput;
        private bool walkerWasGrounded = true;
        private bool pitFallPlayed;
        private bool exitPlayed;

        private PlayerController ragdollWatch;
        private float ragdollWatchUntil;

        private int nextTick;

        private void Awake()
        {
            if (game == null)
            {
                game = GetComponentInParent<MemoryRunMinigame>();
            }
        }

        private void OnEnable()
        {
            if (game != null)
            {
                game.MineDetonated += OnMineDetonated;
            }
        }

        private void OnDisable()
        {
            if (game != null)
            {
                game.MineDetonated -= OnMineDetonated;
            }

            audioPlayer?.StopAll();
        }

        private void Update()
        {
            if (game == null || audioPlayer == null)
            {
                return;
            }

            TrackPhase();
            TrackTurn();
            TrackWalker();
            TrackRagdoll();
        }

        /// <summary>
        /// Фаза раунда: сирена смены на старте, отбой в конце и гул цеха
        /// всё время между ними.
        ///
        /// Гул — единственный луп игры, и он <b>ровный по всей длине
        /// маршрута</b>. Звук, меняющийся от ряда к ряду, работал бы счётчиком
        /// шагов ничем не хуже метки на плите.
        /// </summary>
        private void TrackPhase()
        {
            MinigamePhase phase = game.Phase;
            if (phase == lastPhase)
            {
                return;
            }

            if (phase == MinigamePhase.Round)
            {
                audioPlayer.Play(SlotRoundStart);
                audioPlayer.StartLoop(SlotAmbience);
            }
            else if (lastPhase == MinigamePhase.Round)
            {
                audioPlayer.StopLoop(SlotAmbience);
                audioPlayer.Play(SlotRoundEnd);
            }

            lastPhase = phase;
        }

        /// <summary>
        /// Смена ходящего и отсчёт последних секунд.
        ///
        /// 🔴 Отсчёт слышит <b>только тот, чей ход</b>. Зрителям он сказал бы,
        /// сколько идущему осталось; торопить его — их право, но узнавать это
        /// от игры они не должны.
        ///
        /// Щелчки играются по одному с сокращающимся интервалом, а не одной
        /// готовой дорожкой. Дорожка на пять секунд разъезжается с настоящим
        /// дедлайном — тот живёт по <c>NetworkClock</c>, а не по времени
        /// начала проигрывания.
        /// </summary>
        private void TrackTurn()
        {
            int walkerId = game.CurrentWalkerId;
            if (walkerId != lastWalkerId)
            {
                lastWalkerId = walkerId;
                BindWalker(game.CurrentWalker);
                walkerWasGrounded = true;
                pitFallPlayed = false;
                exitPlayed = false;
                nextTick = 0;

                if (walkerId != TurnQueue.NoPlayer)
                {
                    audioPlayer.Play(SlotTurnAnnounce);
                }

                return;
            }

            // Аватар может приехать позже номера: на клиенте объект игрока
            // появляется своим порядком, и в кадре смены хода его ещё нет.
            if (walker == null)
            {
                BindWalker(game.CurrentWalker);
            }

            if (!game.TurnArmed || walkerInput == null || !walkerInput.LocallyControlled)
            {
                return;
            }

            float left = game.TurnSecondsLeft;
            if (left > tickFromSeconds)
            {
                nextTick = 0;
                return;
            }

            // Секунда с номером 0 — это отметка «осталось tickFromSeconds»,
            // дальше по одному щелчку на каждую пройденную секунду.
            int shouldHave = Mathf.Clamp(Mathf.FloorToInt(tickFromSeconds - left) + 1,
                0, Mathf.CeilToInt(tickFromSeconds));
            if (shouldHave > nextTick)
            {
                nextTick = shouldHave;
                audioPlayer.Play(SlotTurnTick);
            }
        }

        /// <summary>
        /// Идущий у себя: приземление на плиту, падение в провал, приход
        /// к выходу.
        ///
        /// Приземление ловится переходом «не на земле → на земле» и только над
        /// плитой: шаги по площадке ожидания и по выходному настилу — не
        /// событие маршрута, а гулкий металл там был бы ложью.
        /// </summary>
        private void TrackWalker()
        {
            if (walker == null || config == null)
            {
                return;
            }

            Vector3 position = walker.transform.position;

            if (walker.IsGrounded && !walkerWasGrounded &&
                config.TryGetCell(position, out _, out _))
            {
                audioPlayer.PlayAt(SlotPlateLand, position);
            }

            walkerWasGrounded = walker.IsGrounded;

            if (!pitFallPlayed && position.y < pitFallY)
            {
                pitFallPlayed = true;
                audioPlayer.PlayAt(SlotPitFall, position);
            }

            if (!exitPlayed && position.z > config.ExitPadZ)
            {
                exitPlayed = true;
                audioPlayer.PlayAt(SlotExitDoor, position);
                audioPlayer.Play(SlotExitFanfare);
            }
        }

        /// <summary>
        /// Падение тела после взрыва. Ждём, когда подорванный снова коснётся
        /// земли, но не дольше <see cref="ragdollWatchSeconds"/>: улетевший
        /// в провал не приземлится никогда, и ожидание надо чем-то закрывать.
        /// </summary>
        private void TrackRagdoll()
        {
            if (ragdollWatch == null)
            {
                return;
            }

            if (Time.time > ragdollWatchUntil)
            {
                ragdollWatch = null;
                return;
            }

            if (ragdollWatch.IsGrounded)
            {
                audioPlayer.PlayAt(SlotRagdollLand, ragdollWatch.transform.position);
                ragdollWatch = null;
            }
        }

        /// <summary>
        /// Взрыв. Три звука разом и намеренно тремя слотами: у хлопка,
        /// свиста полёта и вздоха публики разная длина хвоста, и склеенным
        /// файлом их уже не развести.
        /// </summary>
        private void OnMineDetonated(Vector3 center)
        {
            if (audioPlayer == null)
            {
                return;
            }

            audioPlayer.PlayAt(SlotMineBlast, center);
            audioPlayer.PlayAt(SlotRagdollLaunch, center);
            audioPlayer.Play(SlotCrowdGasp);

            WatchRagdollLanding();
        }

        /// <summary>Взять подорванного под наблюдение: следующее касание земли — это падение тела.</summary>
        private void WatchRagdollLanding()
        {
            PlayerController victim = game != null ? game.CurrentWalker : null;
            if (victim == null)
            {
                return;
            }

            ragdollWatch = victim;
            ragdollWatchUntil = Time.time + ragdollWatchSeconds;

            // Тело уже в воздухе: следующее касание земли и есть падение.
            walkerWasGrounded = true;
        }

        /// <summary>
        /// Запомнить идущего и его ридер ввода разом.
        ///
        /// Владение персонажем живёт в <see cref="PlayerInputReader"/>, а не в
        /// контроллере — тем же способом его находит HUD. Берётся здесь один
        /// раз на ход, а не каждый кадр.
        /// </summary>
        private void BindWalker(PlayerController next)
        {
            walker = next;
            walkerInput = next != null ? next.GetComponent<PlayerInputReader>() : null;
        }
    }
}
