using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Звук «Плачущих ангелов» — фаза 5.
    ///
    /// <b>Своего состояния и своих RPC нет.</b> Компонент висит на том, что игра
    /// уже подняла на каждой машине, и в сеть не добавляет ни пакета.
    ///
    /// Крючков пять, и каждый выбран под своё:
    ///
    /// <list type="bullet">
    /// <item><b>Фон зала</b> — на фазу раунда. Фаза реплицирована, значит гул
    /// поднимается и гаснет у всех в один момент.</item>
    /// <item><b>Скрип постамента</b> — на поворот Водящего. Не на ввод, а на
    /// фактический разворот тела: положение Водящего реплицировано, поэтому
    /// скрип слышат все, а у самого Водящего он ещё и подтверждает, что
    /// поворот пошёл.</item>
    /// <item><b>Окаменение</b> — на <see cref="CryingAngelsMinigame.RunnerPetrified"/>.
    /// Это единственное событие игры, объявленное на каждой машине: сервер
    /// приходит к нему из своего такта, клиент — из применённого состояния.</item>
    /// <item><b>Скример</b> — на <see cref="KeeperScreamer.Started"/>. Сцену
    /// касания играют все три стороны — Водящий, дошедший и зрители, — каждая
    /// свою, и событие поднимается у каждой.</item>
    /// <item><b>Луч нашёл тебя</b> — на <see cref="BeamCaughtFeedback.Caught"/>.
    /// Единственный звук здесь, который слышит один игрок, и это осознанно:
    /// он не событие арены, а часть того же экранного ответа, что вспышка и
    /// надпись «ТЫ В ЛУЧЕ». Арена в этот момент и так звучит — по окаменению.</item>
    /// </list>
    ///
    /// <b>Шаг по камню подменяется у всех Бегущих сразу.</b> Зал каменный, и
    /// крадущийся шаг в нём — половина игры: по нему Водящий решает, куда
    /// повернуться. Подмена идёт через общий слой (<see cref="CharacterFootsteps"/>),
    /// а не через свой источник, чтобы сохранить вариации, разброс высоты и
    /// тишину приседа.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CryingAngelsAudio : MonoBehaviour
    {
        private const string SlotHallAmbience = "SFX_ANGL_Amb_Hall_Loop";
        private const string SlotPedestalGrind = "SFX_ANGL_Pedestal_Grind_Loop";
        private const string SlotPetrifyCrumble = "SFX_ANGL_Petrify_Crumble";
        private const string SlotPetrifiedImpact = "SFX_ANGL_Petrified_Impact";
        private const string SlotScreamer = "SFX_ANGL_Screamer";
        private const string SlotSpottedSting = "SFX_ANGL_Spotted_Sting";
        private const string SlotStepStone = "SFX_ANGL_Step_Stone";

        /// <summary>Ниже этой скорости поворота постамент молчит: Водящий стоит, а не крутится, °/с.</summary>
        private const float GrindMinSpeed = 8f;

        /// <summary>Скорость поворота, на которой скрип звучит в полную силу, °/с.</summary>
        private const float GrindFullSpeed = 45f;

        /// <summary>Насколько скрип тише в самом начале поворота.</summary>
        private const float GrindQuietLevel = 0.35f;

        /// <summary>Разброс высоты скрипа между медленным и быстрым поворотом.</summary>
        private const float GrindPitchRange = 0.25f;

        [Tooltip("Проигрыватель звука арены")]
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        [Tooltip("Контроллер раунда. Не задан — ищется на родителях")]
        [SerializeField] private CryingAngelsMinigame game;

        [Tooltip("Числа раунда: отсюда берётся длительность окаменения")]
        [SerializeField] private CryingAngelsConfig config;

        [Tooltip("Библиотека игры. Подкладывается персонажам ради шага по камню")]
        [SerializeField] private MinigameSfxLibrary library;

        [Tooltip("Сцена касания. Заполняет билдер арены")]
        [SerializeField] private KeeperScreamer screamer;

        [Tooltip("Экранный ответ на луч у своего игрока. Заполняет билдер арены")]
        [SerializeField] private BeamCaughtFeedback caughtFeedback;

        /// <summary>Кому подменён шаг. Список нужен, чтобы вернуть обычный шаг на выходе из раунда.</summary>
        private readonly List<CharacterFootsteps> stoneWalkers = new List<CharacterFootsteps>(8);

        private MinigamePhase lastPhase = MinigamePhase.Idle;
        private float keeperYaw;
        private bool keeperYawKnown;

        /// <summary>Где рухнет статуя. Одна точка, а не очередь: окаменения подряд в одну секунду не случаются.</summary>
        private Vector3 pendingImpact;

        private void Awake()
        {
            if (game == null) game = GetComponentInParent<CryingAngelsMinigame>();
        }

        private void OnEnable()
        {
            if (game != null) game.RunnerPetrified += OnRunnerPetrified;
            if (screamer != null) screamer.Started += OnScreamerStarted;
            if (caughtFeedback != null) caughtFeedback.Caught += OnCaughtByBeam;
        }

        private void OnDisable()
        {
            if (game != null) game.RunnerPetrified -= OnRunnerPetrified;
            if (screamer != null) screamer.Started -= OnScreamerStarted;
            if (caughtFeedback != null) caughtFeedback.Caught -= OnCaughtByBeam;

            ReleaseStoneSteps();
            CancelInvoke();
            audioPlayer?.StopAll();
        }

        private void Update()
        {
            if (game == null || audioPlayer == null) return;

            TrackPhase();
            TrackPedestal();
        }

        /// <summary>
        /// Фон зала идёт ровно в раунде. Фазу опрашиваем, а не слушаем: своего
        /// события о смене фазы у контроллера нет, а само поле реплицировано —
        /// то есть у всех машин одно и то же.
        /// </summary>
        private void TrackPhase()
        {
            MinigamePhase phase = game.Phase;
            if (phase == lastPhase) return;

            bool wasRound = lastPhase == MinigamePhase.Round;
            lastPhase = phase;

            if (phase == MinigamePhase.Round)
            {
                audioPlayer.StartLoop(SlotHallAmbience);
                BindStoneSteps();
                return;
            }

            if (!wasRound) return;

            audioPlayer.StopLoop(SlotHallAmbience);
            audioPlayer.StopLoop(SlotPedestalGrind);
            keeperYawKnown = false;
            ReleaseStoneSteps();
        }

        /// <summary>
        /// Скрип постамента ведётся за фактической скоростью разворота Водящего.
        /// Громкость и высота растут со скоростью — иначе скрип превращается в
        /// ровный шум, по которому не понять, крутится Водящий или замер.
        /// </summary>
        private void TrackPedestal()
        {
            PlayerController keeper = game.Keeper;
            if (keeper == null || lastPhase != MinigamePhase.Round)
            {
                if (audioPlayer.IsLoopPlaying(SlotPedestalGrind)) audioPlayer.StopLoop(SlotPedestalGrind);
                keeperYawKnown = false;
                return;
            }

            float yaw = keeper.transform.eulerAngles.y;
            if (!keeperYawKnown)
            {
                keeperYaw = yaw;
                keeperYawKnown = true;
                return;
            }

            float turned = Mathf.Abs(Mathf.DeltaAngle(keeperYaw, yaw));
            keeperYaw = yaw;

            float speed = Time.deltaTime > 0f ? turned / Time.deltaTime : 0f;
            if (speed < GrindMinSpeed)
            {
                if (audioPlayer.IsLoopPlaying(SlotPedestalGrind)) audioPlayer.StopLoop(SlotPedestalGrind);
                return;
            }

            Vector3 point = keeper.Position;
            audioPlayer.StartLoop(SlotPedestalGrind, point);
            audioPlayer.MoveLoop(SlotPedestalGrind, point);

            float effort = Mathf.InverseLerp(GrindMinSpeed, GrindFullSpeed, speed);
            audioPlayer.SetLoopLevel(
                SlotPedestalGrind,
                Mathf.Lerp(GrindQuietLevel, 1f, effort),
                Mathf.Lerp(1f - GrindPitchRange * 0.5f, 1f + GrindPitchRange * 0.5f, effort));
        }

        /// <summary>
        /// Окаменение: камень трескается сразу, а падает статуя в конце анимации —
        /// ровно перед тем, как игрока вернёт на старт. Два слота, потому что
        /// между ними секунда с лишним, и склеенным клипом её уже не подвинуть,
        /// если геймдизайнер поменяет длительность в конфиге.
        /// </summary>
        private void OnRunnerPetrified(Vector3 point)
        {
            if (audioPlayer == null) return;

            audioPlayer.PlayAt(SlotPetrifyCrumble, point);

            float fall = config != null ? config.PetrifyAnimationDuration : 0f;
            if (fall <= 0f)
            {
                audioPlayer.PlayAt(SlotPetrifiedImpact, point);
                return;
            }

            pendingImpact = point;
            CancelInvoke(nameof(PlayPetrifiedImpact));
            Invoke(nameof(PlayPetrifiedImpact), fall);
        }

        private void PlayPetrifiedImpact() => audioPlayer?.PlayAt(SlotPetrifiedImpact, pendingImpact);

        private void OnScreamerStarted(int playerId) => audioPlayer?.Play(SlotScreamer);

        private void OnCaughtByBeam() => audioPlayer?.Play(SlotSpottedSting);

        /// <summary>
        /// Подменить шаг всем участникам раунда на каменный.
        ///
        /// Состав берётся из табло катки, а не из спавнера: в сетевой катке
        /// персонажей создаёт сервер, и локальный спавнер пуст.
        ///
        /// Библиотека игры подкладывается персонажу здесь же: проигрыватель на
        /// его префабе один на все пятнадцать игр и знает только общий слой,
        /// а каменный шаг — слот этой игры.
        /// </summary>
        private void BindStoneSteps()
        {
            ReleaseStoneSteps();

            IReadOnlyList<SessionPlayer> players = SessionScoreboard.Current?.Players;
            if (players == null) return;

            for (int i = 0; i < players.Count; i++)
            {
                PlayerController avatar = players[i]?.Avatar;
                if (avatar == null) continue;

                if (library != null && avatar.TryGetComponent(out MinigameAudioPlayer voice))
                {
                    voice.AddLibrary(library);
                }

                if (!avatar.TryGetComponent(out CharacterFootsteps steps)) continue;

                steps.SetSlotOverride(SlotStepStone);
                stoneWalkers.Add(steps);
            }
        }

        /// <summary>Вернуть обычный шаг: раунд кончился или сцену выгружают.</summary>
        private void ReleaseStoneSteps()
        {
            for (int i = 0; i < stoneWalkers.Count; i++)
            {
                if (stoneWalkers[i] != null) stoneWalkers[i].SetSlotOverride(string.Empty);
            }

            stoneWalkers.Clear();
        }
    }
}
