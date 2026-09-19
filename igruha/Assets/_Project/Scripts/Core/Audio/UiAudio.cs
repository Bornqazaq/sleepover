using UnityEngine;
using UnityEngine.EventSystems;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Звук интерфейса — общий слой фазы 5. Один на сцену, играет плоско: меню
    /// звучит одинаково у всех и от положения камеры не зависит.
    ///
    /// <b>Наведение ловится само, нажатие — нет.</b> Выделенный элемент известен
    /// системе событий, и его смену видно отсюда без единой правки экранов. А вот
    /// нажатие знает только сама кнопка, и его озвучивает <see cref="UiButtonSound"/>,
    /// который ставится на кнопки редакторным проходом.
    ///
    /// <b>Своего проигрывателя не заводит.</b> Пул источников и лимит копий уже
    /// решены в <see cref="MinigameAudioPlayer"/>, и второй такой же движок ради
    /// плоского звука был бы дублированием.
    ///
    /// Статическая ссылка — тот же приём, что у <c>SessionManager</c>: интерфейс
    /// зовёт звук из десятка мест, и протаскивать ссылку через каждое значило бы
    /// править каждый экран ради одного щелчка.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MinigameAudioPlayer))]
    public sealed class UiAudio : MonoBehaviour
    {
        /// <summary>Звук интерфейса этой сцены. Пусто — интерфейс молчит, и это не ошибка.</summary>
        public static UiAudio Instance { get; private set; }

        [Tooltip("Проигрыватель с библиотекой Core/UI. Не задан — берётся с этого же объекта")]
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        [Tooltip("Звучит ли наведение на элемент. Выключить, если экран сам ведёт озвучку перебора")]
        [SerializeField] private bool playHover = true;

        /// <summary>Что было выделено в прошлом кадре — смена и есть наведение.</summary>
        private GameObject lastSelected;

        /// <summary>Сыграть слот интерфейса. Молча, если звука интерфейса в сцене нет.</summary>
        public static void Play(string slot)
        {
            if (Instance == null || Instance.audioPlayer == null || string.IsNullOrEmpty(slot)) return;
            Instance.audioPlayer.Play(slot);
        }

        private void Awake()
        {
            if (audioPlayer == null) audioPlayer = GetComponent<MinigameAudioPlayer>();
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!playHover) return;

            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == lastSelected) return;

            GameObject previous = lastSelected;
            lastSelected = selected;

            // Первое выделение при открытии экрана не озвучивается: оно случается
            // само, без участия игрока, и звучало бы эхом того щелчка, которым
            // экран открыли.
            if (previous == null || selected == null) return;

            Play(CoreSfx.UiHover);
        }
    }
}
