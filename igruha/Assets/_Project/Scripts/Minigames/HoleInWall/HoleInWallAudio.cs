using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Звук «Дырки в стене» — подфаза 4.5. Гонг и гул стены, сигнал перед
    /// ударом, «дзынь» с рёвом публики на проходе, удар по телу на провале,
    /// всплеск с подводным слоем, свуши подвохов, тема раунда
    /// и финальный джингл.
    ///
    /// <b>Свиста полёта здесь больше нет, и это решение, а не пропуск.</b>
    /// Слот <c>fall_whoosh</c> был в 4.5 и убран 03.09 по слуху геймдизайнера:
    /// он мешал удару, а не дополнял его. Генератор на нём был слаб с самого
    /// начала — три тишины из пяти вариантов, слот стоял на единственном годном
    /// (см. STATE 3.44). Момент провала озвучивает удар по телу, всплеск и,
    /// если пара падает, подводный слой; свист поверх них был лишним слоем.
    ///
    /// <b>Своего состояния и своих RPC здесь нет</b> — ровно как у
    /// <see cref="HoleInWallEffects"/> подфазы 4.4, и по той же причине.
    /// Каждый звук висит на том, что игра уже посчитала и уже показала всем:
    /// вердикт стены и сигнал перед ударом приходят событиями контроллера,
    /// подвох — событием стены, а положение стены, высота аватара и натяжение
    /// троса читаются из чисел, одинаковых на каждой машине. Заводить ради
    /// звука хоть один пакет не потребовалось.
    ///
    /// Отсюда же и проверка привязки: <b>звук, слышный только инициатору,
    /// означает неверное событие, а не проблему звука.</b>
    /// </summary>
    /// <remarks>
    /// <b>Почему звук не по одному на дорожку, а по одному на стену.</b>
    /// Момент удара у всех дорожек общий, и четыре одинаковых источника
    /// дали бы четырёхкратную громкость вместо подсказки — этим правилом
    /// уже живёт сигнал перед ударом с каркаса. Поэтому на стену приходится
    /// один гонг, один гул и один рёв публики, а по дорожкам разведено
    /// только то, что игрок обязан отличить у себя от соседского: «дзынь»
    /// прохода, удар по телу, всплеск и свуш подвоха.
    /// Они трёхмерные, и дорожки разносит затухание по расстоянию.
    ///
    /// <b>Провал звучит раз на дорожку, а не раз на человека.</b> Пара стоит
    /// в двух метрах друг от друга, и два одинаковых удара в один кадр дают
    /// не «снесло двоих», а вдвое более громкий удар. Точка берётся между
    /// сметёнными — там же, где стена их и достала.
    ///
    /// <b>Лупов по одному на слот.</b> Подводный слой и трос заводятся один
    /// раз и ведутся за первым, кто под водой или кого тянет; остальное
    /// делает трёхмерное затухание — свой всплеск у игрока в упор, чужой
    /// через дорожку. Строго от первого лица подводный слой сделать можно,
    /// но для этого нужен идентификатор своего игрока, а он сюда не ходит.
    /// </remarks>
    public sealed class HoleInWallAudio : MonoBehaviour
    {
        /// <summary>Мест на дорожке: пара.</summary>
        private const int SlotsPerTrack = 2;

        // ========== СЛОТЫ БИБЛИОТЕКИ ==========
        // Те же идентификаторы, что в docs/art/hole-in-wall-sfx.json.

        private const string SlotWallStart = "wall_start_gong";
        private const string SlotWallMove = "wall_move_loop";
        private const string SlotWarning = "impact_warning";
        private const string SlotDing = "pass_success_ding";
        private const string SlotCheer = "crowd_cheer";
        private const string SlotBodyHit = "fail_body_hit";
        private const string SlotSplash = "water_splash";
        private const string SlotUnderwater = "underwater_loop";
        private const string SlotMirror = "mirror_whoosh";
        private const string SlotMorph = "morph_whoosh";
        private const string SlotJingle = "round_end_jingle";
        private const string SlotTheme = "round_theme";

        // ========== ГУЛ СТЕНЫ ==========

        /// <summary>
        /// Громкость гула на самой медленной стене.
        ///
        /// ⚠️ <b>Было 0.55 и оказалось глухо.</b> Множители перемножаются:
        /// слот 0.85 × этот × <see cref="FarRumbleVolume"/>, — и на старте
        /// стены давали 0.15 при теме раунда 0.35. Подъезжающая стена звучала
        /// вдвое тише фоновой музыки, то есть подсказки не было вовсе.
        /// </summary>
        private const float SlowRumbleVolume = 0.8f;

        /// <summary>Тон гула на самой медленной стене. Ниже единицы — стена «тяжелее».</summary>
        private const float SlowRumblePitch = 0.9f;

        /// <summary>Тон гула на самой быстрой стене.</summary>
        private const float FastRumblePitch = 1.25f;

        /// <summary>
        /// Доля громкости гула в момент старта стены. К линии проверки она дорастает до полной.
        ///
        /// ⚠️ <b>Было 0.4.</b> Смысл множителя — «далёкая стена тише близкой»,
        /// но вместе с полом громкости он же и делал старт неслышным. Слышать
        /// стену игрок обязан с первой секунды: на это у него уходит выбор позы.
        /// Разница между далёкой и близкой остаётся, но начинается не с шёпота.
        /// </summary>
        private const float FarRumbleVolume = 0.75f;

        /// <summary>На этой высоте над полом платформы стоит источник гула — на уровне корпуса стены.</summary>
        private const float RumbleHeightFactor = 0.5f;

        /// <summary>Над полом платформы, м: высота источника звуков, привязанных к дорожке.</summary>
        private const float TrackSoundHeight = 1f;

        /// <summary>
        /// Сколько луп держится после того, как его состояние пропало, с.
        ///
        /// <b>Замер прогона, а не запас на всякий случай.</b> Состояние, которое
        /// держит луп, дребезжит: на ленте прогона 4.5 это дало десяток включений
        /// очередями по 0.2–1.3 с — луп не звучал, а трещал заеданием. Глазу такое
        /// мерцание почти незаметно (искры 4.4 живут с ним же), уху — нет: любой
        /// перезапуск лупа слышен щелчком. Задержка гасит дребезг и не мешает
        /// честному отпусканию: состояние пропадает надолго, а не на треть секунды.
        ///
        /// Замерено на скрипе троса, который дёргался от <c>PlayerTether.IsTaut</c>;
        /// сам скрип убран 03.09 (его было не слышно), а константа осталась —
        /// подводный слой дребезжит так же.
        /// </summary>
        private const float LoopReleaseHold = 0.35f;

        [Tooltip("Контроллер игры: фаза, состав дорожек, вердикт стены и сигнал перед ударом")]
        [SerializeField] private HoleInWallMinigame game;

        [Tooltip("Числа игры: уровень воды, габариты стены, расписание подъездов")]
        [SerializeField] private HoleInWallConfig config;

        [Tooltip("Все дорожки арены. По ним ищется стена, поднявшая подвох")]
        [SerializeField] private HoleInWallTrack[] tracks = System.Array.Empty<HoleInWallTrack>();

        [Tooltip("Проигрыватель со ссылкой на библиотеку слотов этой игры")]
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        /// <summary>Кто сейчас под водой. По ключу «дорожка × место»: всплеск поднимается на входе, а не каждый кадр.</summary>
        private bool[] submerged = System.Array.Empty<bool>();


        /// <summary>Фаза прошлого кадра: тема и джингл вешаются на переход, а не на состояние.</summary>
        private MinigamePhase lastPhase = MinigamePhase.Idle;

        /// <summary>Хоть одна стена ехала в прошлом кадре: по переходу бьёт гонг и заводится гул.</summary>
        private bool wallsRunning;

        /// <summary>Подводный слой звучит. Считается по всем местам сразу — источник один.</summary>
        private bool underwater;


        /// <summary>Когда глушить подводный слой, если состояние так и не вернётся. Ноль — глушить не собирались.</summary>
        private float underwaterReleaseAt;


        /// <summary>На какой стене публика уже отревела. Рёв один на стену, а не на дорожку.</summary>
        private int cheeredWall = -1;

        /// <summary>Скорость самой медленной стены раунда, м/с. Считается один раз: расписание не меняется.</summary>
        private float slowestWallSpeed;

        /// <summary>Скорость самой быстрой стены раунда, м/с.</summary>
        private float fastestWallSpeed;

        private void Awake()
        {
            submerged = new bool[Mathf.Max(0, tracks.Length) * SlotsPerTrack];
            MeasureWallSpeeds();
        }

        /// <summary>
        /// Края скорости стен — из расписания подъездов, а не на глаз.
        /// Ими нормируется тон и громкость гула: на первой стене он тяжёлый
        /// и тихий, на восьмой звонкий и полный.
        /// </summary>
        private void MeasureWallSpeeds()
        {
            slowestWallSpeed = float.MaxValue;
            fastestWallSpeed = 0f;

            if (config == null)
            {
                slowestWallSpeed = 0f;
                return;
            }

            for (int wall = 0; wall < config.WallCount; wall++)
            {
                // Одиночка едет быстрее пары на тот же бонус, и её стена —
                // законный край диапазона, а не исключение.
                for (int solo = 0; solo < 2; solo++)
                {
                    float speed = config.WallSpeed(wall, solo == 1);
                    slowestWallSpeed = Mathf.Min(slowestWallSpeed, speed);
                    fastestWallSpeed = Mathf.Max(fastestWallSpeed, speed);
                }
            }

            if (slowestWallSpeed > fastestWallSpeed)
            {
                slowestWallSpeed = fastestWallSpeed;
            }
        }

        private void OnEnable()
        {
            if (game != null)
            {
                game.WallResolved += HandleWallResolved;
                game.WallWarning += HandleWallWarning;
            }

            for (int i = 0; i < tracks.Length; i++)
            {
                SweepingWall wall = tracks[i] != null ? tracks[i].Wall : null;
                if (wall != null)
                {
                    wall.TrickTriggered += HandleTrick;
                }
            }
        }

        private void OnDisable()
        {
            if (game != null)
            {
                game.WallResolved -= HandleWallResolved;
                game.WallWarning -= HandleWallWarning;
            }

            for (int i = 0; i < tracks.Length; i++)
            {
                SweepingWall wall = tracks[i] != null ? tracks[i].Wall : null;
                if (wall != null)
                {
                    wall.TrickTriggered -= HandleTrick;
                }
            }

            Silence();
        }

        // ========== НАБЛЮДЕНИЕ ==========

        /// <summary>
        /// Гул, всплеск, трос и тема — не события, а состояния: их видно по
        /// числам, одинаковым у всех. Отсюда наблюдение кадром, а не подписка.
        /// </summary>
        private void Update()
        {
            if (game == null || config == null || audioPlayer == null)
            {
                return;
            }

            WatchPhase();

            if (game.Phase != MinigamePhase.Round)
            {
                return;
            }

            WatchWall();

            IReadOnlyList<HoleInWallTrack> playing = game.PlayingTracks;
            bool anySubmerged = false;
            Vector3 underwaterPoint = Vector3.zero;

            for (int i = 0; i < playing.Count; i++)
            {
                HoleInWallTrack track = playing[i];
                if (track == null || !track.Active)
                {
                    continue;
                }

                WatchWater(track, ref anySubmerged, ref underwaterPoint);
            }

            DriveLoop(SlotUnderwater, anySubmerged, underwaterPoint, ref underwater, ref underwaterReleaseAt);
        }

        /// <summary>
        /// Тема раунда и финальный джингл. Вешаются на смену фазы, потому что
        /// это единственные два звука игры, у которых событие — сам переход:
        /// тема заводится вместе с раундом и глохнет вместе с ним, джингл бьёт
        /// ровно в момент, когда раунд стал результатами.
        /// </summary>
        private void WatchPhase()
        {
            MinigamePhase phase = game.Phase;
            if (phase == lastPhase)
            {
                return;
            }

            bool leftRound = lastPhase == MinigamePhase.Round;
            lastPhase = phase;

            if (phase == MinigamePhase.Round)
            {
                audioPlayer.StartLoop(SlotTheme);
                return;
            }

            Silence();

            if (leftRound)
            {
                audioPlayer.Play(SlotJingle);
            }
        }

        /// <summary>
        /// Гонг на старте стены и гул, пока она едет.
        ///
        /// Стены дорожек стартуют почти вместе, поэтому гонг бьёт по первой
        /// поехавшей, а гул ведётся за самой близкой к игрокам: он и есть
        /// та угроза, которую слышно. Громкость растёт вдвое к линии проверки,
        /// тон — с номером стены, потому что расписание её разгоняет.
        /// </summary>
        private void WatchWall()
        {
            IReadOnlyList<HoleInWallTrack> playing = game.PlayingTracks;

            bool running = false;
            float nearestFrontZ = float.MaxValue;
            float fastestSpeed = 0f;

            for (int i = 0; i < playing.Count; i++)
            {
                HoleInWallTrack track = playing[i];
                SweepingWall wall = track != null ? track.Wall : null;
                if (wall == null || !wall.Running)
                {
                    continue;
                }

                running = true;
                nearestFrontZ = Mathf.Min(nearestFrontZ, wall.FrontZ);
                fastestSpeed = Mathf.Max(fastestSpeed, wall.Speed);
            }

            if (running && !wallsRunning)
            {
                audioPlayer.Play(SlotWallStart);
                audioPlayer.StartLoop(SlotWallMove, RumblePoint(nearestFrontZ));
            }
            else if (!running && wallsRunning)
            {
                audioPlayer.StopLoop(SlotWallMove);
            }

            wallsRunning = running;

            if (!running)
            {
                return;
            }

            audioPlayer.MoveLoop(SlotWallMove, RumblePoint(nearestFrontZ));

            float speedFactor = fastestWallSpeed > slowestWallSpeed
                ? Mathf.InverseLerp(slowestWallSpeed, fastestWallSpeed, fastestSpeed)
                : 0f;

            // Чем ближе передняя грань к линии проверки, тем полнее гул:
            // «нарастающий гул» из спеки — это про подъезд, а не про номер стены.
            float travelled = Mathf.InverseLerp(config.WallStartZ, config.CheckLineZ, nearestFrontZ);
            float closeness = Mathf.Lerp(FarRumbleVolume, 1f, travelled);

            audioPlayer.SetLoopLevel(SlotWallMove,
                Mathf.Lerp(SlowRumbleVolume, 1f, speedFactor) * closeness,
                Mathf.Lerp(SlowRumblePitch, FastRumblePitch, speedFactor));
        }

        /// <summary>Источник гула — по центру арены на уровне корпуса стены, а не у одной из дорожек.</summary>
        private Vector3 RumblePoint(float frontZ) =>
            new Vector3(0f, config.PlatformSurfaceY + config.WallHeight * RumbleHeightFactor, frontZ);

        /// <summary>
        /// Всплеск на входе в воду и подводный слой, пока в ней барахтаются.
        /// Порог тот же, по которому контроллер назначает возврат, — иначе
        /// брызги вставали бы не там, где барахтанье.
        /// </summary>
        private void WatchWater(HoleInWallTrack track, ref bool anySubmerged, ref Vector3 point)
        {
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            bool splashed = false;

            for (int slot = 0; slot < members.Count && slot < SlotsPerTrack; slot++)
            {
                PlayerController avatar = members[slot].Avatar;
                if (avatar == null)
                {
                    continue;
                }

                int key = Key(track.Index, slot);
                if (key < 0 || key >= submerged.Length)
                {
                    continue;
                }

                Vector3 position = avatar.Position;
                bool under = position.y < config.WaterSurfaceY;

                // Всплеск звучит с зеркала воды, а не с головы ушедшего под неё.
                var surface = new Vector3(position.x, config.WaterSurfaceY, position.z);

                // Всплеск — один на дорожку, а не на человека, тем же правилом,
                // которым живёт удар по телу. Пара падает в одну точку в один
                // кадр, и два одинаковых всплеска там дают не «плюхнулись двое»,
                // а вдвое более громкий всплеск: +6 дБ на самом громком звуке
                // игры. Геймдизайнер услышал это первым же, что сказал про звук.
                if (under && !submerged[key] && !splashed)
                {
                    splashed = true;
                    audioPlayer.PlayAt(SlotSplash, surface);
                }

                submerged[key] = under;

                if (under && !anySubmerged)
                {
                    anySubmerged = true;
                    point = surface;
                }
            }
        }

        /// <summary>
        /// Завести, вести или погасить луп по состоянию, которое его держит.
        /// Гасит не сразу, а через <see cref="LoopReleaseHold"/>: разбор там же.
        /// </summary>
        private void DriveLoop(string slot, bool wanted, Vector3 point, ref bool playing, ref float releaseAt)
        {
            if (wanted)
            {
                releaseAt = 0f;

                if (!playing)
                {
                    audioPlayer.StartLoop(slot, point);
                    playing = true;
                }
                else
                {
                    audioPlayer.MoveLoop(slot, point);
                }

                return;
            }

            if (!playing)
            {
                return;
            }

            // Состояние пропало — засекаем, но не глушим: вернётся в пределах
            // задержки, и луп даже не узнает, что его собирались выключить.
            if (releaseAt <= 0f)
            {
                releaseAt = Time.time + LoopReleaseHold;
                return;
            }

            if (Time.time < releaseAt)
            {
                return;
            }

            audioPlayer.StopLoop(slot);
            playing = false;
            releaseAt = 0f;
        }

        // ========== СОБЫТИЯ ИГРЫ ==========

        /// <summary>
        /// Сигнал за секунду до удара. Контроллер поднимает его один раз на
        /// стену и на каждой машине — по общим часам, без пакета.
        /// </summary>
        private void HandleWallWarning() => audioPlayer.Play(SlotWarning);

        /// <summary>
        /// Вердикт стены. Событие поднимается и на сервере, где вердикт
        /// посчитан, и на клиенте, где он получен оповещением, — поэтому
        /// звук слышат все, а не только тот, кто считал.
        /// </summary>
        private void HandleWallResolved(HoleInWallTrack track, bool passed)
        {
            if (track == null || config == null || audioPlayer == null)
            {
                return;
            }

            if (passed)
            {
                audioPlayer.PlayAt(SlotDing, TrackPoint(track));

                // Публика в студии одна, и реветь ей положено раз на стену.
                // Иначе четыре прошедшие дорожки дают четырёхкратный рёв.
                if (cheeredWall != game.CurrentWallNumber)
                {
                    cheeredWall = game.CurrentWallNumber;
                    audioPlayer.Play(SlotCheer);
                }

                return;
            }

            Vector3 point = SweptPoint(track);
            audioPlayer.PlayAt(SlotBodyHit, point);
        }

        /// <summary>
        /// Подвох сработал. Стена поднимает это сама на каждой машине: момент
        /// объявлен вместе с рисунком, и до него каждая доходит по общим часам.
        /// </summary>
        private void HandleTrick(SweepingWall wall, WallTrick trick)
        {
            HoleInWallTrack track = TrackOfWall(wall);
            if (track == null || config == null || audioPlayer == null)
            {
                return;
            }

            // Переворот и смена формы звучат по-разному намеренно: подвоха
            // в игре два, у каждого свой эффект с 4.4, и один свуш на оба
            // читался бы как один и тот же подвох.
            string slot = trick == WallTrick.Mirror ? SlotMirror
                : trick == WallTrick.Morph ? SlotMorph
                : null;

            if (slot == null)
            {
                return;
            }

            audioPlayer.PlayAt(slot, new Vector3(track.transform.position.x,
                config.PlatformSurfaceY + config.WallHeight * RumbleHeightFactor, wall.FrontZ));
        }

        /// <summary>Линия проверки на этой дорожке — там, где проход и случился.</summary>
        private Vector3 TrackPoint(HoleInWallTrack track) =>
            new Vector3(track.transform.position.x,
                config.PlatformSurfaceY + TrackSoundHeight, config.CheckLineZ);

        /// <summary>
        /// Где стена достала дорожку: между сметёнными, а не на каждом из них.
        /// Разбор — в замечании к классу.
        /// </summary>
        private Vector3 SweptPoint(HoleInWallTrack track)
        {
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;

            Vector3 sum = Vector3.zero;
            int counted = 0;

            for (int slot = 0; slot < members.Count && slot < SlotsPerTrack; slot++)
            {
                PlayerController avatar = members[slot].Avatar;
                if (avatar == null)
                {
                    continue;
                }

                sum += avatar.CameraTarget.position;
                counted++;
            }

            return counted > 0 ? sum / counted : TrackPoint(track);
        }

        private HoleInWallTrack TrackOfWall(SweepingWall wall)
        {
            for (int i = 0; i < tracks.Length; i++)
            {
                if (tracks[i] != null && tracks[i].Wall == wall)
                {
                    return tracks[i];
                }
            }

            return null;
        }

        /// <summary>Погасить всё зацикленное: раунд кончился или компонент выключили.</summary>
        private void Silence()
        {
            if (audioPlayer == null)
            {
                return;
            }

            audioPlayer.StopLoop(SlotTheme);
            audioPlayer.StopLoop(SlotWallMove);
            audioPlayer.StopLoop(SlotUnderwater);

            wallsRunning = false;
            underwater = false;
            underwaterReleaseAt = 0f;
            cheeredWall = -1;

            for (int i = 0; i < submerged.Length; i++)
            {
                submerged[i] = false;
            }
        }

        private static int Key(int track, int slot) => track * SlotsPerTrack + slot;
    }
}
