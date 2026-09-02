using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Эффекты «Дырки в стене» — подфаза 4.4. Всплеск при падении в воду,
    /// вспышка по контуру пройденного выреза, удар по телу сметённого, искры
    /// на натянутом тросе и след подвоха.
    ///
    /// <b>Своего состояния здесь нет ни на копейку, и это главное правило
    /// подфазы.</b> Каждый эффект висит на том, что игра уже посчитала и уже
    /// показала всем: вердикт стены приходит событием контроллера, подвох —
    /// событием стены, падение и натяжение читаются из позиций, которые
    /// сетевой транспорт и так реплицирует каждому. Ни одного нового RPC,
    /// ни одной новой переменной, <c>Core/</c> не тронут вовсе.
    /// </summary>
    /// <remarks>
    /// <b>Почему падение в воду не событие, а замер.</b> Возвраты считает
    /// <see cref="HoleInWallMinigame"/> под авторитетом — у клиента этот код
    /// не идёт вовсе, и события «упал» на клиенте просто нет. Заводить его
    /// оповещением значило бы завести новый RPC ради брызг. Вместо этого
    /// высота сравнивается с <see cref="HoleInWallConfig.WaterSurfaceY"/> —
    /// ровно тем же числом и ровно так же, как это делает сам контроллер
    /// в <c>TickReturns</c>. Позиция аватара видна каждой машине, число одно
    /// на всех, значит и всплеск поднимется у всех и в один момент. Тот же
    /// приём, что у положения стены: чистая функция от того, что и так у всех
    /// одинаково, вместо пакета.
    ///
    /// <b>Ни один эффект не вложен в то, что исчезает.</b> Все партиклы лежат
    /// отдельной группой в сцене и лишь переставляются в нужную точку перед
    /// запуском. Вложить вспышку в контур выреза было бы естественнее всего —
    /// и она гасла бы ровно в тот момент, ради которого её ставили: стена
    /// уезжает за спину и прячет все свои плиты, а вспышка обязана догореть
    /// на линии проверки. То же со сметённым: аватар через четыре секунды
    /// телепортируется на платформу и утащил бы облако удара за собой.
    ///
    /// <b>Партиклы пака запускаются вручную.</b> Автостарт и зацикливание
    /// сняты построителем со всех одноразовых эффектов; зацикленным оставлен
    /// только туман над водой — он не событие, а воздух студии, и обязан идти
    /// всегда.
    /// </remarks>
    public sealed class HoleInWallEffects : MonoBehaviour
    {
        /// <summary>Мест на дорожке: пара. Столько же вырезов у её стены.</summary>
        private const int SlotsPerTrack = 2;

        /// <summary>
        /// На сколько след подвоха поднят над верхней кромкой стены, м.
        ///
        /// <b>Не украшение и не вкус.</b> Плита стены — белый глянцевый пластик
        /// во весь кадр, а эффекты пака рисуются аддитивным смешением: они
        /// <i>добавляют</i> свой цвет к тому, что за ними. Добавить к белому
        /// нечего, и на плите розовый мазок переворота пропадал целиком —
        /// проверено рендером. Над кромкой за ним чёрный потолок студии,
        /// и тот же мазок читается с дорожки сразу.
        ///
        /// Вырез он при этом не задевает ни на каком рисунке: самый высокий
        /// силуэт — «руки вверх», 2.66 м при высоте стены 3.60 м, то есть
        /// след идёт выше любой дырки почти на метр.
        /// </summary>
        private const float TrickLift = 0.35f;

        [Tooltip("Контроллер игры: у него фаза, состав дорожек и вердикт стены")]
        [SerializeField] private HoleInWallMinigame game;

        [Tooltip("Числа игры: уровень воды, габариты силуэтов, высота стены")]
        [SerializeField] private HoleInWallConfig config;

        [Tooltip("Все дорожки арены. По ним ищется стена, поднявшая подвох")]
        [SerializeField] private HoleInWallTrack[] tracks = System.Array.Empty<HoleInWallTrack>();

        [Tooltip("Всплеск с расходящимися кругами: по одному на место дорожки")]
        [SerializeField] private ParticleSystem[] splashes = System.Array.Empty<ParticleSystem>();

        [Tooltip("Удар стены по телу: по одному на место дорожки")]
        [SerializeField] private ParticleSystem[] impacts = System.Array.Empty<ParticleSystem>();

        [Tooltip("Вспышка по контуру пройденного выреза: по одной на вырез дорожки")]
        [SerializeField] private ParticleSystem[] flashes = System.Array.Empty<ParticleSystem>();

        [Tooltip("Искры на натянутом тросе: по одному на дорожку")]
        [SerializeField] private ParticleSystem[] tethers = System.Array.Empty<ParticleSystem>();

        [Tooltip("След зеркального переворота: по одному на дорожку")]
        [SerializeField] private ParticleSystem[] mirrors = System.Array.Empty<ParticleSystem>();

        [Tooltip("Волна смены формы: по одной на дорожку")]
        [SerializeField] private ParticleSystem[] morphs = System.Array.Empty<ParticleSystem>();

        /// <summary>Кто сейчас под водой. По ключу «дорожка × место»: всплеск поднимается на входе, а не каждый кадр.</summary>
        private bool[] submerged = System.Array.Empty<bool>();

        /// <summary>У какой дорожки трос сейчас натянут. Искры включаются на переходе, а не перезапускаются кадрово.</summary>
        private bool[] taut = System.Array.Empty<bool>();

        private void Awake()
        {
            submerged = new bool[Mathf.Max(0, tracks.Length) * SlotsPerTrack];
            taut = new bool[Mathf.Max(0, tracks.Length)];
        }

        private void OnEnable()
        {
            if (game != null)
            {
                game.WallResolved += HandleWallResolved;
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
            }

            for (int i = 0; i < tracks.Length; i++)
            {
                SweepingWall wall = tracks[i] != null ? tracks[i].Wall : null;
                if (wall != null)
                {
                    wall.TrickTriggered -= HandleTrick;
                }
            }

            Quiet();
        }

        // ========== НАБЛЮДЕНИЕ ==========

        /// <summary>
        /// Всплеск и трос — не события, а состояния: их видно по позициям,
        /// одинаковым у всех. Отсюда наблюдение кадром, а не подписка.
        /// </summary>
        private void Update()
        {
            if (game == null || config == null)
            {
                return;
            }

            if (game.Phase != MinigamePhase.Round)
            {
                Quiet();
                return;
            }

            IReadOnlyList<HoleInWallTrack> playing = game.PlayingTracks;
            for (int i = 0; i < playing.Count; i++)
            {
                HoleInWallTrack track = playing[i];
                if (track == null || !track.Active)
                {
                    continue;
                }

                WatchWater(track);
                WatchTether(track);
            }
        }

        /// <summary>
        /// Кто ушёл под воду — тому всплеск. Порог тот же, по которому
        /// контроллер назначает возврат: два разных числа на одно и то же
        /// событие разъехались бы, и брызги вставали бы не там, где барахтанье.
        /// </summary>
        private void WatchWater(HoleInWallTrack track)
        {
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;

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

                if (under && !submerged[key])
                {
                    // Всплеск встаёт на поверхность воды, а не на голову
                    // ушедшего под неё: круги обязаны расходиться по зеркалу.
                    PlayAt(splashes, key, new Vector3(position.x, config.WaterSurfaceY, position.z));
                }

                submerged[key] = under;
            }
        }

        /// <summary>
        /// Искры на тросе, пока пара тянет друг друга. Натяжение спрашивается
        /// у самого троса: <see cref="PlayerTether.IsTaut"/> считается на каждой
        /// машине из двух позиций, поэтому у всех загорается одновременно.
        /// </summary>
        private void WatchTether(HoleInWallTrack track)
        {
            int index = track.Index;
            if (index < 0 || index >= taut.Length)
            {
                return;
            }

            ParticleSystem sparks = At(tethers, index);
            if (sparks == null)
            {
                return;
            }

            PlayerTether tether = track.Tether;
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            bool tight = tether != null && tether.Bound && tether.IsTaut && members.Count >= SlotsPerTrack;

            if (tight)
            {
                PlayerController first = members[0].Avatar;
                PlayerController second = members[1].Avatar;
                if (first == null || second == null)
                {
                    tight = false;
                }
                else
                {
                    // Ровно там же, где трос рисует свою верёвку: от груди
                    // к груди. Середина отрезка — единственная точка, которая
                    // остаётся на тросе при любом развороте пары.
                    sparks.transform.position =
                        (first.CameraTarget.position + second.CameraTarget.position) * 0.5f;
                }
            }

            if (tight == taut[index])
            {
                return;
            }

            taut[index] = tight;

            if (tight)
            {
                sparks.Play(true);
            }
            else
            {
                // StopEmitting, а не Clear: уже вылетевшие искры обязаны
                // догореть, иначе трос гаснет рывком на полукадре.
                sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        // ========== СОБЫТИЯ ИГРЫ ==========

        /// <summary>
        /// Стена разрешена. Событие поднимается и на сервере, где вердикт
        /// посчитан, и на клиенте, где он получен оповещением, — поэтому
        /// эффект виден у всех, а не только у того, кто считал.
        /// </summary>
        private void HandleWallResolved(HoleInWallTrack track, bool passed)
        {
            if (track == null || config == null)
            {
                return;
            }

            if (passed)
            {
                FlashCutouts(track);
            }
            else
            {
                ImpactMembers(track);
            }
        }

        /// <summary>
        /// Вспышка по контуру каждого выреза, в который дорожка пролезла.
        /// Точка берётся расчётом, а не у рамки контура: рамка едет вместе
        /// со стеной и через полсекунды будет уже за спиной, а вспышка обязана
        /// остаться на линии проверки — там, где проход и случился.
        /// </summary>
        private void FlashCutouts(HoleInWallTrack track)
        {
            SweepingWall wall = track.Wall;
            if (wall == null)
            {
                return;
            }

            float trackX = track.transform.position.x;

            for (int cutout = 0; cutout < SlotsPerTrack; cutout++)
            {
                if (!wall.TryGetCutout(cutout, out HoleInWallPose pose, out float offset))
                {
                    continue;
                }

                Vector2 size = config.SilhouetteSize(pose);
                var point = new Vector3(trackX + offset,
                    config.PlatformSurfaceY + size.y * 0.5f, config.CheckLineZ);

                PlayAt(flashes, Key(track.Index, cutout), point);
            }
        }

        /// <summary>Облако удара на каждом, кого стена только что снесла.</summary>
        private void ImpactMembers(HoleInWallTrack track)
        {
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;

            for (int slot = 0; slot < members.Count && slot < SlotsPerTrack; slot++)
            {
                PlayerController avatar = members[slot].Avatar;
                if (avatar == null)
                {
                    continue;
                }

                PlayAt(impacts, Key(track.Index, slot), avatar.CameraTarget.position);
            }
        }

        /// <summary>
        /// Подвох сработал. Стена поднимает это на каждой машине сама —
        /// момент срабатывания объявлен вместе с рисунком, и каждая машина
        /// доходит до него по своим часам без единого пакета.
        /// </summary>
        private void HandleTrick(SweepingWall wall, WallTrick trick)
        {
            HoleInWallTrack track = TrackOfWall(wall);
            if (track == null || config == null)
            {
                return;
            }

            ParticleSystem[] bank = trick == WallTrick.Mirror ? mirrors
                : trick == WallTrick.Morph ? morphs
                : null;

            if (bank == null)
            {
                return;
            }

            // Над верхней кромкой стены в момент срабатывания — разбор
            // в <see cref="TrickLift"/>. След остаётся на месте, а стена
            // уезжает дальше: так это и читается следом, а не приклеенным
            // к плите пятном.
            var point = new Vector3(track.transform.position.x,
                config.PlatformSurfaceY + config.WallHeight + TrickLift, wall.FrontZ);

            PlayAt(bank, track.Index, point);
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

        // ========== ЗАПУСК ==========

        /// <summary>
        /// Переставить эффект в точку и пустить заново. <c>Clear</c> перед
        /// <c>Play</c> обязателен: без него повторный запуск на том же месте
        /// не перезапускает всплеск, а доигрывает предыдущий — а падают в воду
        /// вдвоём и подряд.
        /// </summary>
        private static void PlayAt(ParticleSystem[] bank, int index, Vector3 point)
        {
            ParticleSystem effect = At(bank, index);
            if (effect == null)
            {
                return;
            }

            effect.transform.position = point;
            effect.Clear(true);
            effect.Play(true);
        }

        /// <summary>Погасить всё, что могло остаться от прошлого раунда.</summary>
        private void Quiet()
        {
            for (int i = 0; i < taut.Length; i++)
            {
                if (!taut[i])
                {
                    continue;
                }

                taut[i] = false;
                ParticleSystem sparks = At(tethers, i);
                sparks?.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            for (int i = 0; i < submerged.Length; i++)
            {
                submerged[i] = false;
            }
        }

        private static ParticleSystem At(ParticleSystem[] bank, int index) =>
            bank != null && index >= 0 && index < bank.Length ? bank[index] : null;

        private static int Key(int track, int slot) => track * SlotsPerTrack + slot;
    }
}
