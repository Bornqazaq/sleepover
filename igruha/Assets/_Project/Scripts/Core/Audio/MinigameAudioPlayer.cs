using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Проигрыватель звука мини-игры: играет слот по имени из <see cref="MinigameSfxLibrary"/>.
    /// Общий слой подфазы 4.5 для всех пятнадцати игр, к конкретной игре не привязан.
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

        [Tooltip("Библиотека слотов этой игры")]
        [SerializeField] private MinigameSfxLibrary library;

        [Tooltip("Сколько одноразовых звуков может звучать одновременно")]
        [SerializeField] private int voices = DefaultVoices;

        [Tooltip("Дистанция полного затухания трёхмерных звуков, м")]
        [SerializeField] private float maxDistance = DefaultMaxDistance;

        /// <summary>Пул одноразовых. Лупы сюда не попадают: их нельзя перебивать очередным всплеском.</summary>
        private AudioSource[] pool;

        /// <summary>Звучащие лупы по идентификатору слота. Источник заводится один раз и переиспользуется.</summary>
        private readonly Dictionary<string, AudioSource> loops = new Dictionary<string, AudioSource>();

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
            if (!TryGetEntry(id, out MinigameSfxLibrary.Entry entry)) return;
            AudioSource source = TakeFreeSource();
            Configure(source, entry, spatial: false);
            source.Play();
        }

        /// <summary>Играет слот из точки события: всплеск в воде, удар по телу.</summary>
        public void PlayAt(string id, Vector3 point)
        {
            if (!TryGetEntry(id, out MinigameSfxLibrary.Entry entry)) return;
            AudioSource source = TakeFreeSource();
            source.transform.position = point;
            Configure(source, entry, entry.Spatial);
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

            Configure(source, entry, entry.Spatial);
            source.loop = true;
            source.Play();
        }

        /// <summary>Глушит зацикленный слот. Молча, если он и не звучал.</summary>
        public void StopLoop(string id)
        {
            if (loops.TryGetValue(id, out AudioSource source) && source != null) source.Stop();
        }

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

        private bool TryGetEntry(string id, out MinigameSfxLibrary.Entry entry)
        {
            entry = default;
            if (library == null) return false;
            if (library.TryGet(id, out entry) && entry.Clip != null) return true;

            ReportMiss(id);
            return false;
        }

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

        private void Configure(AudioSource source, MinigameSfxLibrary.Entry entry, bool spatial)
        {
            source.clip = entry.Clip;
            source.volume = entry.Volume;
            source.pitch = 1f;
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
