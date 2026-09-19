using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Проигрыватель звука мини-игры: играет слот по имени из <see cref="MinigameSfxLibrary"/>.
    /// Общий слой фазы 5 для всех пятнадцати игр, к конкретной игре не привязан.
    ///
    /// <b>Своей сетевой части здесь нет, и это намеренно.</b> Звук вешается на события,
    /// которые игра уже подняла на каждой машине — тот же приём, которым живут эффекты
    /// подфазы 4.4. Ни одного нового RPC ради звука заводить не нужно: если событие
    /// видно всем, то и звук поднимется у всех. Если звук слышит только инициатор,
    /// значит его повесили не на то событие, и чинить надо привязку, а не звук.
    ///
    /// Источники держатся пулом. <c>AudioSource.PlayClipAtPoint</c> на каждый выстрел
    /// создаёт объект и тут же его хоронит, а мини-игра на восьмерых стреляет звуками
    /// пачками — в правилах проекта это прямой запрет на аллокации в игровом цикле.
    /// </summary>
    public sealed class MinigameAudioPlayer : MonoBehaviour
    {
        /// <summary>Одновременных одноразовых звуков. Восемь игроков и их события с запасом.</summary>
        private const int DefaultVoices = 16;

        /// <summary>С этой дистанции трёхмерный звук уже не слышно. Порядок арены мини-игры.</summary>
        private const float DefaultMaxDistance = 40f;

        /// <summary>Ближе этого громкость не растёт — иначе звук в упор бьёт по ушам.</summary>
        private const float MinDistance = 3f;

        /// <summary>
        /// Разброс высоты у слота с вариациями, доля от единицы. Требование поставки:
        /// без него шесть файлов шага по дереву на бегу слышны как шесть файлов,
        /// а не как шаги.
        /// </summary>
        private const float PitchJitter = 0.05f;

        /// <summary>
        /// Сколько копий одного слота может звучать на арене одновременно.
        /// Правило поставки: при восьми игроках девятый одинаковый удар уже не
        /// слышен как удар, он слышен как каша. Счёт общий на все проигрыватели
        /// сцены — иначе восемь персонажей насчитают себе по четыре шага каждый.
        /// </summary>
        private const int MaxCopiesPerSlot = 4;

        [Tooltip("Библиотека слотов этой игры")]
        [SerializeField] private MinigameSfxLibrary library;

        [Tooltip("Общие библиотеки поверх своей: персонаж, интерфейс, ловушки. Слот ищется сперва в своей")]
        [SerializeField] private MinigameSfxLibrary[] extraLibraries;

        [Tooltip("Сколько одноразовых звуков может звучать одновременно")]
        [SerializeField] private int voices = DefaultVoices;

        [Tooltip("Дистанция полного затухания трёхмерных звуков, м")]
        [SerializeField] private float maxDistance = DefaultMaxDistance;

        /// <summary>Пул одноразовых. Лупы сюда не попадают: их нельзя перебивать очередным всплеском.</summary>
        private AudioSource[] pool;

        /// <summary>Звучащие лупы по идентификатору слота. Источник заводится один раз и переиспользуется.</summary>
        private readonly Dictionary<string, AudioSource> loops = new Dictionary<string, AudioSource>();

        /// <summary>Какой вариант слота звучал прошлый раз — чтобы не повторить его подряд.</summary>
        private readonly Dictionary<string, int> lastVariant = new Dictionary<string, int>();

        /// <summary>
        /// Когда освободится каждая из <see cref="MaxCopiesPerSlot"/> копий слота.
        /// Массив меток времени вместо счётчика: уменьшать счётчик по концу звука
        /// некому, а конец копии известен заранее — это её длина.
        /// </summary>
        private static readonly Dictionary<string, float[]> slotReleaseTimes = new Dictionary<string, float[]>();

        /// <summary>О каких слотах уже пожаловались. Без этого промах в Update заспамил бы консоль.</summary>
        private HashSet<string> reportedMisses;

        private void Awake()
        {
            pool = new AudioSource[Mathf.Max(1, voices)];
            for (int i = 0; i < pool.Length; i++) pool[i] = CreateSource($"Voice{i}");
        }

        /// <summary>Играет слот у всех одинаково, без привязки к точке: джингл, тема, сигнал.</summary>
        public void Play(string id)
        {
            if (!TryTakeVoice(id, out MinigameSfxLibrary.Entry entry, out AudioClip clip)) return;
            AudioSource source = TakeFreeSource();
            Configure(source, entry, clip, spatial: false);
            source.Play();
        }

        /// <summary>Играет слот из точки события: всплеск в воде, удар по телу.</summary>
        public void PlayAt(string id, Vector3 point)
        {
            if (!TryTakeVoice(id, out MinigameSfxLibrary.Entry entry, out AudioClip clip)) return;
            AudioSource source = TakeFreeSource();
            source.transform.position = point;
            Configure(source, entry, clip, entry.Spatial);
            source.Play();
        }

        /// <summary>
        /// Играет слот из точки тише или громче обычного. Нужен там, где громкость
        /// несёт смысл: шаг в приседе, мягкое приземление, удар вполсилы.
        /// </summary>
        public void PlayAt(string id, Vector3 point, float volumeScale)
        {
            if (!TryTakeVoice(id, out MinigameSfxLibrary.Entry entry, out AudioClip clip)) return;
            AudioSource source = TakeFreeSource();
            source.transform.position = point;
            Configure(source, entry, clip, entry.Spatial);
            source.volume = entry.Volume * Mathf.Clamp01(volumeScale);
            source.Play();
        }

        /// <summary>
        /// Запускает зацикленный слот — гул стены, подводный слой, тему раунда.
        /// Повторный вызов на звучащий луп ничего не делает: гул не должен дёргаться с начала каждый кадр.
        /// </summary>
        public void StartLoop(string id, Vector3 point = default)
        {
            if (!TryGetEntry(id, out MinigameSfxLibrary.Entry entry)) return;

            if (!loops.TryGetValue(id, out AudioSource source) || source == null)
            {
                source = CreateSource($"Loop_{id}");
                loops[id] = source;
            }

            if (entry.Spatial) source.transform.position = point;
            if (source.isPlaying) return;

            Configure(source, entry, PickClip(id, entry), entry.Spatial);
            source.pitch = 1f;
            source.loop = true;
            source.Play();
        }

        /// <summary>Глушит зацикленный слот. Молча, если он и не звучал.</summary>
        public void StopLoop(string id)
        {
            if (loops.TryGetValue(id, out AudioSource source) && source != null) source.Stop();
        }

        /// <summary>Звучит ли луп прямо сейчас — для тех, кто ведёт его по состоянию игры.</summary>
        public bool IsLoopPlaying(string id)
            => loops.TryGetValue(id, out AudioSource source) && source != null && source.isPlaying;

        /// <summary>
        /// Ведёт звучащий луп по ходу события: громче и выше с ростом скорости стены.
        /// Множители относительные — базовую громкость слот держит сам.
        /// </summary>
        public void SetLoopLevel(string id, float volumeScale, float pitch)
        {
            if (!loops.TryGetValue(id, out AudioSource source) || source == null || !source.isPlaying) return;
            if (!TryGetEntry(id, out MinigameSfxLibrary.Entry entry)) return;

            source.volume = entry.Volume * Mathf.Clamp01(volumeScale);
            source.pitch = pitch;
        }

        /// <summary>Переставляет звучащий трёхмерный луп — если его источник едет вместе со стеной.</summary>
        public void MoveLoop(string id, Vector3 point)
        {
            if (loops.TryGetValue(id, out AudioSource source) && source != null) source.transform.position = point;
        }

        /// <summary>Глушит всё: конец раунда, выход из мини-игры.</summary>
        public void StopAll()
        {
            if (pool != null)
                foreach (AudioSource s in pool) if (s != null) s.Stop();

            foreach (KeyValuePair<string, AudioSource> pair in loops)
                if (pair.Value != null) pair.Value.Stop();
        }

        /// <summary>Есть ли такой слот вообще — для тех, кто выбирает слот по обстановке.</summary>
        public bool HasSlot(string id) => TryFindEntry(id, out _);

        /// <summary>
        /// Добавляет библиотеку в рантайме. Нужно там, где проигрыватель живёт на
        /// префабе персонажа — один на все пятнадцать игр, — а звук у игры свой:
        /// мини-игра подкладывает свою библиотеку спавнящимся персонажам, и её
        /// слоты становятся видны наравне с общими.
        /// </summary>
        public void AddLibrary(MinigameSfxLibrary extra)
        {
            if (extra == null) return;

            if (extraLibraries == null)
            {
                extraLibraries = new[] { extra };
                reportedMisses = null;
                return;
            }

            foreach (MinigameSfxLibrary known in extraLibraries)
                if (known == extra) return;

            var grown = new MinigameSfxLibrary[extraLibraries.Length + 1];
            System.Array.Copy(extraLibraries, grown, extraLibraries.Length);
            grown[extraLibraries.Length] = extra;
            extraLibraries = grown;

            // Жалобы на немые слоты сбрасываются: слот, которого не было минуту
            // назад, теперь может найтись, и старая жалоба про него врёт.
            reportedMisses = null;
        }

        /// <summary>
        /// Слот найден, вариант выбран, копия в бюджете — можно играть.
        /// Три шага вместе, потому что важен их порядок: занимать копию бюджета
        /// имеет смысл только тогда, когда известна длина конкретного варианта.
        /// </summary>
        private bool TryTakeVoice(string id, out MinigameSfxLibrary.Entry entry, out AudioClip clip)
        {
            clip = null;
            if (!TryGetEntry(id, out entry)) return false;

            clip = PickClip(id, entry);
            if (clip == null) return false;

            return TryReserveCopy(id, clip.length);
        }

        /// <summary>
        /// Занимает одну из копий слота на время звучания. Возвращает false, когда все
        /// <see cref="MaxCopiesPerSlot"/> заняты — тогда звук просто не играется.
        /// </summary>
        private static bool TryReserveCopy(string id, float duration)
        {
            if (!slotReleaseTimes.TryGetValue(id, out float[] releases))
            {
                releases = new float[MaxCopiesPerSlot];
                slotReleaseTimes[id] = releases;
            }

            float now = Time.time;
            for (int i = 0; i < releases.Length; i++)
            {
                if (releases[i] > now) continue;
                releases[i] = now + duration;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Вариант слота: случайный, но не тот же, что прошлый раз.
        /// Слот без вариаций отдаёт свой единственный клип.
        /// </summary>
        private AudioClip PickClip(string id, MinigameSfxLibrary.Entry entry)
        {
            AudioClip[] variants = entry.Variants;
            if (variants == null || variants.Length == 0) return entry.Clip;
            if (variants.Length == 1) return variants[0] != null ? variants[0] : entry.Clip;

            lastVariant.TryGetValue(id, out int previous);

            // Бросок по укороченному диапазону со сдвигом мимо прошлого варианта:
            // так «любой, кроме прошлого» выпадает с одного броска, без цикла
            // перебросов, который в худшем случае крутится неизвестно сколько.
            int index = Random.Range(0, variants.Length - 1);
            if (index >= previous) index++;

            lastVariant[id] = index;
            return variants[index] != null ? variants[index] : entry.Clip;
        }

        private bool TryGetEntry(string id, out MinigameSfxLibrary.Entry entry)
        {
            if (TryFindEntry(id, out entry)) return true;

            ReportMiss(id);
            return false;
        }

        /// <summary>
        /// Ищет слот: сперва в своей библиотеке, затем в общих.
        /// Порядок такой, чтобы игра могла перебить общий звук своим — например,
        /// шаг заражённого вместо обычного шага по траве.
        /// </summary>
        private bool TryFindEntry(string id, out MinigameSfxLibrary.Entry entry)
        {
            if (library != null && library.TryGet(id, out entry) && HasClip(entry)) return true;

            if (extraLibraries != null)
            {
                foreach (MinigameSfxLibrary extra in extraLibraries)
                {
                    if (extra == null) continue;
                    if (extra.TryGet(id, out entry) && HasClip(entry)) return true;
                }
            }

            entry = default;
            return false;
        }

        private static bool HasClip(MinigameSfxLibrary.Entry entry)
            => entry.Clip != null || (entry.Variants != null && entry.Variants.Length > 0);

        private void ReportMiss(string id)
        {
            reportedMisses ??= new HashSet<string>();
            if (!reportedMisses.Add(id)) return;
            Debug.LogWarning($"[{nameof(MinigameAudioPlayer)}] В библиотеке нет слота «{id}» или у него пустой клип.", this);
        }

        /// <summary>Свободный источник, а при их нехватке — самый давний по кругу.</summary>
        private AudioSource TakeFreeSource()
        {
            for (int i = 0; i < pool.Length; i++)
                if (!pool[i].isPlaying) return pool[i];

            AudioSource oldest = pool[0];
            System.Array.Copy(pool, 1, pool, 0, pool.Length - 1);
            pool[pool.Length - 1] = oldest;
            oldest.Stop();
            return oldest;
        }

        private void Configure(AudioSource source, MinigameSfxLibrary.Entry entry, AudioClip clip, bool spatial)
        {
            bool varied = entry.Variants != null && entry.Variants.Length > 1;

            source.clip = clip;
            source.volume = entry.Volume;
            source.pitch = varied ? Random.Range(1f - PitchJitter, 1f + PitchJitter) : 1f;
            source.loop = false;
            source.spatialBlend = spatial ? 1f : 0f;
        }

        private AudioSource CreateSource(string sourceName)
        {
            var holder = new GameObject(sourceName);
            holder.transform.SetParent(transform, worldPositionStays: false);

            AudioSource source = holder.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = MinDistance;
            source.maxDistance = maxDistance;
            return source;
        }
    }
}
