using System;
using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Живой зал «Дырки в стене»: публика на трибунах дышит битом студии,
    /// прокатывает волну, замирает перед ударом стены и взрывается после
    /// вердикта.
    ///
    /// Появился по разбору кадра: зал 4.7 стоял <b>манекенами</b>. Двести
    /// человек в позе «руки вниз», не сдвинувшихся ни на пиксель за весь
    /// раунд, читаются не публикой, а расставленными по трибуне болванками —
    /// и чем их больше, тем сильнее это бьёт. Оживление здесь дороже любой
    /// добавленной модели: шевелящаяся толпа из тех же мешей выглядит живой,
    /// неподвижная из вдвое лучших — мёртвой.
    ///
    /// <b>Своего состояния нет ни на копейку, сети нет вовсе.</b> Тот же
    /// принцип, что у эффектов 4.4 и звука 4.5: компонент висит на событиях,
    /// которые игра уже подняла <i>на каждой машине</i>
    /// (<see cref="HoleInWallMinigame.WallResolved"/> и
    /// <see cref="HoleInWallMinigame.WallWarning"/>), а всё остальное считает
    /// чистой функцией от <see cref="Time.time"/>. Ни одного RPC, ни одной
    /// <c>NetworkVariable</c>, ни одного обращения к <c>Core/</c>.
    ///
    /// <b>Расхождение зала между машинами допустимо и заложено.</b> Фаза
    /// каждого зрителя берётся от его места, а не от общих часов, поэтому
    /// у хоста и клиента толпа шевелится по-своему. Это декорация: она не
    /// решает ничего, её никто не сверяет, и синхронизировать двести
    /// подпрыгиваний означало бы гонять пакеты ради того, чего никто не
    /// заметит.
    /// </summary>
    /// <remarks>
    /// <b>Почему один компонент на весь зал, а не по одному на зрителя.</b>
    /// Двести <c>MonoBehaviour</c> — это двести вызовов <c>Update</c> через
    /// границу движка на кадр, каждый со своим поиском компонентов. Здесь
    /// один цикл по плоским массивам: ни одной аллокации в кадре, ни одного
    /// <c>GetComponent</c> после <c>Awake</c> — правило проекта про
    /// <c>Update</c> соблюдается буквально.
    ///
    /// <b>Меши переключаются, а не анимируются.</b> Зал набран из мешей,
    /// запечённых в двух позах — «руки вниз» и «руки вверх», — потому что
    /// скиннинг двухсот персонажей стоил бы дороже всей остальной сцены.
    /// Поднятые руки поэтому не выезжают плавно, а встают разом на пике
    /// прыжка, где движение и так самое быстрое: подмену в этот момент глаз
    /// не ловит. Ровно этот приём используют настоящие трибуны в спортивных
    /// играх.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class HoleInWallCrowd : MonoBehaviour
    {
        /// <summary>Приставка в имени зрителя. По ней зал набирается в <see cref="Awake"/>.</summary>
        private const string FanPrefix = "Fan_";

        /// <summary>
        /// Пара мешей одного персонажа: спокойный и болеющий.
        ///
        /// Хранится таблицей на весь зал, а не ссылкой у каждого зрителя.
        /// Персонажей восемь, зрителей больше двухсот: восемь пар в сцене
        /// против двухсот ссылок — разница в размере YAML сцены, который
        /// потом мерджится вручную.
        /// </summary>
        [Serializable]
        public struct Wardrobe
        {
            [Tooltip("Поза покоя: руки вниз")]
            public Mesh Idle;

            [Tooltip("Поза ликования: руки вверх")]
            public Mesh Cheer;
        }

        [Tooltip("Контроллер игры: у него вердикт стены и предупреждение об ударе")]
        [SerializeField] private HoleInWallMinigame game;

        [Tooltip("Пары мешей «спокоен / болеет» на каждого персонажа зала")]
        [SerializeField] private Wardrobe[] wardrobe = Array.Empty<Wardrobe>();

        [Tooltip("Темп студии, ударов в минуту. Под него зал качается и подпрыгивает")]
        [SerializeField] private float beatsPerMinute = 104f;

        [Tooltip("Высота подскока на пике биения, м")]
        [SerializeField] private float bobHeight = 0.12f;

        [Tooltip("Разворот корпуса на качании, градусов")]
        [SerializeField] private float swayDegrees = 7f;

        [Tooltip("Насколько зрителя подбрасывает гребнем волны, м")]
        [SerializeField] private float waveLift = 0.55f;

        [Tooltip("Как часто по трибуне прокатывается волна, с")]
        [SerializeField] private float wavePeriod = 17f;

        [Tooltip("Сколько секунд волна идёт от одного края зала до другого")]
        [SerializeField] private float waveTravel = 3.2f;

        [Tooltip("Ширина гребня волны в тех же единицах, что и место зрителя, м")]
        [SerializeField] private float waveWidth = 6f;

        [Tooltip("Сколько секунд зал ликует после вердикта стены")]
        [SerializeField] private float cheerSeconds = 2.6f;

        [Tooltip("Сколько секунд зал держит дыхание после предупреждения об ударе")]
        [SerializeField] private float hushSeconds = 1.1f;

        [Tooltip("Оживление зала между событиями: 0 — стоит, 1 — пляшет")]
        [SerializeField, Range(0f, 1f)] private float restingEnergy = 0.34f;

        [Tooltip("За сколько секунд зал переходит из покоя в ликование и обратно")]
        [SerializeField] private float energyRamp = 0.35f;

        // ========== ЗАЛ, РАЗОБРАННЫЙ НА МАССИВЫ ==========
        //
        // Плоские массивы, а не список объектов: цикл по ним идёт без
        // разыменований и без единой аллокации на кадр.

        private Transform[] fans = Array.Empty<Transform>();
        private MeshFilter[] filters = Array.Empty<MeshFilter>();
        private Mesh[] idleMesh = Array.Empty<Mesh>();
        private Mesh[] cheerMesh = Array.Empty<Mesh>();
        private Vector3[] seat = Array.Empty<Vector3>();
        private float[] baseYaw = Array.Empty<float>();
        private float[] phase = Array.Empty<float>();

        /// <summary>Место зрителя вдоль зала: по нему бежит волна.</summary>
        private float[] waveKey = Array.Empty<float>();

        /// <summary>Кто сейчас с поднятыми руками. Меш меняется только на переходе.</summary>
        private bool[] cheering = Array.Empty<bool>();

        private float keyMin;
        private float keyLength = 1f;

        private float energy;
        private float cheerUntil = float.NegativeInfinity;
        private float hushUntil = float.NegativeInfinity;

        private void Awake()
        {
            Collect();
            energy = restingEnergy;
        }

        private void OnEnable()
        {
            if (game == null)
            {
                return;
            }

            game.WallResolved += HandleWallResolved;
            game.WallWarning += HandleWallWarning;
        }

        private void OnDisable()
        {
            if (game == null)
            {
                return;
            }

            game.WallResolved -= HandleWallResolved;
            game.WallWarning -= HandleWallWarning;
        }

        /// <summary>
        /// Разобрать зал на массивы. Зрители — прямые дети группы, реквизит
        /// болельщика (телефон, флажок) вложен в самого зрителя и едет вместе
        /// с ним, поэтому отдельного учёта не требует.
        /// </summary>
        private void Collect()
        {
            int count = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                if (transform.GetChild(i).name.StartsWith(FanPrefix, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            fans = new Transform[count];
            filters = new MeshFilter[count];
            idleMesh = new Mesh[count];
            cheerMesh = new Mesh[count];
            seat = new Vector3[count];
            baseYaw = new float[count];
            phase = new float[count];
            waveKey = new float[count];
            cheering = new bool[count];

            keyMin = float.PositiveInfinity;
            float keyMax = float.NegativeInfinity;

            int slot = 0;
            for (int i = 0; i < transform.childCount && slot < count; i++)
            {
                Transform child = transform.GetChild(i);
                if (!child.name.StartsWith(FanPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                fans[slot] = child;
                seat[slot] = child.localPosition;
                baseYaw[slot] = child.localEulerAngles.y;

                // Фаза берётся от места, а не от случайного числа: зал обязан
                // шевелиться одинаково при каждом запуске сцены, иначе снимок
                // кадра на приёмке нельзя повторить.
                phase[slot] = Mathf.Repeat(
                    child.localPosition.x * 1.7f + child.localPosition.z * 2.3f, Mathf.PI * 2f);

                // Волна катится вдоль зала. Боковые трибуны вытянуты по Z,
                // торцевые — по X; общая координата «место в зале» берётся
                // как сумма, поэтому одна волна проходит и те, и другие,
                // а не обрывается на углу.
                float key = child.localPosition.z + child.localPosition.x;
                waveKey[slot] = key;
                keyMin = Mathf.Min(keyMin, key);
                keyMax = Mathf.Max(keyMax, key);

                var filter = child.GetComponent<MeshFilter>();
                filters[slot] = filter;
                Dress(slot, filter != null ? filter.sharedMesh : null);

                slot++;
            }

            keyLength = Mathf.Max(0.01f, keyMax - keyMin);
        }

        /// <summary>
        /// Разложить надетый меш на пару «спокоен / болеет» и запомнить, в
        /// какой из двух зритель поставлен.
        ///
        /// ⚠️ <b>Искать пару мало — нужно знать, которая из двух надета.</b>
        /// Построитель ставит позу случайной, то есть примерно половина зала
        /// приходит уже с поднятыми руками. Первый вариант просто считал
        /// надетый меш спокойным и брал пару к нему: у этой половины позы
        /// оказывались перевёрнуты — на волне они опускали руки, а между
        /// волнами стояли с поднятыми. В кадре это читалось не залом,
        /// а сбоем.
        /// </summary>
        private void Dress(int slot, Mesh worn)
        {
            idleMesh[slot] = worn;
            cheerMesh[slot] = worn;
            cheering[slot] = false;

            if (worn == null)
            {
                return;
            }

            for (int i = 0; i < wardrobe.Length; i++)
            {
                if (wardrobe[i].Idle == worn)
                {
                    cheerMesh[slot] = wardrobe[i].Cheer;
                    return;
                }

                if (wardrobe[i].Cheer == worn)
                {
                    idleMesh[slot] = wardrobe[i].Idle;
                    cheering[slot] = true;
                    return;
                }
            }
        }

        /// <summary>
        /// Вердикт стены объявлен — зал взрывается. Исход не разбирается
        /// намеренно: трибуна орёт и на пройденной дырке, и на улетевшем
        /// в воду, — второе даже громче.
        /// </summary>
        private void HandleWallResolved(HoleInWallTrack track, bool passed)
        {
            cheerUntil = Time.time + cheerSeconds;
            hushUntil = float.NegativeInfinity;
        }

        /// <summary>
        /// До удара секунда — зал замирает. Затишье перед ударом стоит
        /// дороже любого шума после: без него ликование не с чем сравнить,
        /// и трибуна гудит ровно одинаково весь раунд.
        /// </summary>
        private void HandleWallWarning()
        {
            hushUntil = Time.time + hushSeconds;
        }

        private void Update()
        {
            if (fans.Length == 0)
            {
                return;
            }

            float now = Time.time;

            float target = restingEnergy;
            if (now < hushUntil)
            {
                target = 0.04f;
            }
            else if (now < cheerUntil)
            {
                target = 1f;
            }

            energy = Mathf.MoveTowards(energy, target, Time.deltaTime / Mathf.Max(0.01f, energyRamp));

            float beat = now * beatsPerMinute * (Mathf.PI * 2f / 60f);
            float lift = bobHeight * energy;
            float sway = swayDegrees * energy;
            float crest = WaveCrest(now);
            bool storm = energy > 0.72f;

            for (int i = 0; i < fans.Length; i++)
            {
                Transform fan = fans[i];
                if (fan == null)
                {
                    continue;
                }

                float own = beat + phase[i];
                float hop = Mathf.Abs(Mathf.Sin(own * 0.5f));

                // Гребень волны: зритель подбрасывается тем выше, чем ближе
                // к нему фронт. Косинусный колокол, а не ступенька, — иначе
                // волна идёт не волной, а бегущей строкой.
                float reach = crest < 0f ? 2f : Mathf.Abs(waveKey[i] - crest) / waveWidth;
                float ride = reach >= 1f ? 0f : 0.5f + 0.5f * Mathf.Cos(reach * Mathf.PI);

                Vector3 at = seat[i];
                at.y += lift * hop + waveLift * ride;
                fan.localPosition = at;
                fan.localRotation = Quaternion.Euler(0f, baseYaw[i] + sway * Mathf.Sin(own * 0.25f), 0f);

                // Руки вверх — на гребне волны и на буре после вердикта.
                // На буре встают не все: одинаково поднятые двести пар рук
                // читаются строем, а не залом.
                bool wantsCheer = ride > 0.35f || (storm && (i & 1) == 0);
                if (wantsCheer == cheering[i])
                {
                    continue;
                }

                Mesh wanted = wantsCheer ? cheerMesh[i] : idleMesh[i];
                if (wanted == null || filters[i] == null)
                {
                    continue;
                }

                filters[i].sharedMesh = wanted;
                cheering[i] = wantsCheer;
            }
        }

        /// <summary>
        /// Где сейчас фронт волны. Отрицательное значение — волны нет:
        /// между прокатами трибуна просто качается на бите.
        /// </summary>
        private float WaveCrest(float now)
        {
            if (wavePeriod <= 0f || waveTravel <= 0f)
            {
                return -1f;
            }

            float inCycle = Mathf.Repeat(now, wavePeriod);
            if (inCycle > waveTravel)
            {
                return -1f;
            }

            // Фронт выходит на ширину гребня раньше начала зала и уходит на
            // столько же за конец: иначе волна рождается посреди первого ряда
            // и там же умирает, вместо того чтобы прийти и уйти.
            float from = keyMin - waveWidth;
            float to = keyMin + keyLength + waveWidth;
            return Mathf.Lerp(from, to, inCycle / waveTravel);
        }
    }
}
