using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Музыка всей игры: одна тема на сцену, смена — перекрёстным затуханием.
    ///
    /// <b>Поднимается сам и в сценах его нет.</b> Тот же приём, что у голоса:
    /// объект заводится из <see cref="RuntimeInitializeOnLoadMethod"/> и
    /// переживает смену сцен. Иначе пришлось бы класть источник в каждую из
    /// одиннадцати сцен, в том числе в чужую Duck Hunt, а правка чужой сцены
    /// ради подложки — это конфликт слияния на ровном месте.
    ///
    /// <b>Два источника, а не один.</b> Переключение темы одним источником
    /// слышно как обрыв: хаб обрывается на полутакте, мини-игра начинается с
    /// тишины. Здесь старая тема гаснет, пока новая поднимается, и стык не
    /// слышен.
    ///
    /// Музыка не ставится на паузу вместе с игрой (<c>ignoreListenerPause</c>):
    /// меню паузы — это те же полминуты в общей комнате, и тишина в них
    /// читается как «игра вылетела».
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MusicPlayer : MonoBehaviour
    {
        /// <summary>Где лежит библиотека тем. Грузится из Resources: сцен у музыки нет.</summary>
        private const string LibraryResource = "MusicLibrary";

        /// <summary>За сколько секунд одна тема сменяет другую.</summary>
        private const float CrossfadeSeconds = 1.5f;

        public static MusicPlayer Instance { get; private set; }

        private MusicLibrary library;
        private AudioSource current;
        private AudioSource previous;

        /// <summary>Громкость темы, заданная библиотекой, — поверх общей громкости.</summary>
        private float trackVolume = 1f;

        private float fade;
        private string playingScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // Серверу без картинки музыка не нужна, а лишний источник звука на
            // восьми процессах стенда — это восемь декодеров впустую.
            if (Application.isBatchMode) return;
            if (Instance != null) return;

            var host = new GameObject("MusicPlayer");
            DontDestroyOnLoad(host);
            host.AddComponent<MusicPlayer>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            library = Resources.Load<MusicLibrary>(LibraryResource);

            // Единственный след того, что музыка вообще доехала до билда:
            // молчащая подложка и отсутствующая библиотека на слух неотличимы.
            if (library == null) Debug.LogWarning("🎵 музыка: библиотеки нет в Resources — играть нечего");

            current = CreateSource("Music_A");
            previous = CreateSource("Music_B");

            SceneManager.activeSceneChanged += OnSceneChanged;
            Apply(SceneManager.GetActiveScene().name, immediate: true);
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>Сменить громкость музыки — зовёт меню паузы.</summary>
        public void SetVolume(float volume)
        {
            MusicSettings.Volume = volume;
            ApplyVolume();
        }

        /// <summary>Выключить или включить музыку целиком.</summary>
        public void SetEnabled(bool enabled)
        {
            MusicSettings.Enabled = enabled;
            if (!enabled)
            {
                current.Stop();
                previous.Stop();
                playingScene = null;
                return;
            }

            Apply(SceneManager.GetActiveScene().name, immediate: true);
        }

        private AudioSource CreateSource(string sourceName)
        {
            var host = new GameObject(sourceName);
            host.transform.SetParent(transform, false);

            AudioSource source = host.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.bypassReverbZones = true;
            source.ignoreListenerPause = true;
            source.volume = 0f;
            return source;
        }

        private void OnSceneChanged(Scene from, Scene to) => Apply(to.name, immediate: false);

        private void Apply(string scene, bool immediate)
        {
            if (library == null || !MusicSettings.Enabled) return;
            if (scene == playingScene && current.isPlaying) return;

            if (!library.TryGet(scene, out AudioClip clip, out float volume))
            {
                // Тишина — тоже решение библиотеки, но уходить в неё надо
                // так же плавно, как приходить.
                playingScene = scene;
                Swap(null, 0f, immediate);
                return;
            }

            playingScene = scene;
            Swap(clip, volume, immediate);
            Debug.Log($"🎵 музыка: сцена «{scene}» — тема {clip.name}");
        }

        private void Swap(AudioClip clip, float volume, bool immediate)
        {
            (current, previous) = (previous, current);

            trackVolume = volume;
            current.clip = clip;
            current.volume = 0f;

            if (clip != null) current.Play();

            fade = immediate ? 1f : 0f;
            if (!immediate) return;

            previous.Stop();
            ApplyVolume();
        }

        private void Update()
        {
            if (fade >= 1f) return;

            fade = Mathf.Min(1f, fade + Time.unscaledDeltaTime / CrossfadeSeconds);
            ApplyVolume();

            if (fade >= 1f) previous.Stop();
        }

        private void ApplyVolume()
        {
            float target = MusicSettings.Enabled ? MusicSettings.Volume * trackVolume : 0f;
            current.volume = target * fade;
            previous.volume = target * (1f - fade);
        }
    }
}
