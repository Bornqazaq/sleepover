using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Короткое появление панели: масштаб из уменьшенного в единицу плюс
    /// проявление прозрачности.
    ///
    /// Нужен затем же, зачем он нужен в любой современной игре: панель,
    /// возникающая мгновенно, читается как сбой картинки, и глаз не успевает
    /// заметить, что именно появилось. Четверти секунды хватает.
    ///
    /// Время — нескорректированное (<see cref="Time.unscaledDeltaTime"/>):
    /// заставка правил показывается до старта раунда и переживает паузу,
    /// а на паузе <c>Time.deltaTime</c> равен нулю, и анимация замерла бы
    /// на первом кадре.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UiPop : MonoBehaviour
    {
        [Tooltip("Длительность появления, секунд")]
        [SerializeField] private float duration = 0.22f;

        [Tooltip("С какого масштаба стартует панель")]
        [SerializeField] private float fromScale = 0.94f;

        [Tooltip("Играть само при включении объекта")]
        [SerializeField] private bool playOnEnable = true;

        private CanvasGroup group;
        private RectTransform rect;
        private float elapsed;
        private bool playing;

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();
            rect = transform as RectTransform;
        }

        private void OnEnable()
        {
            if (playOnEnable)
            {
                Play();
            }
        }

        /// <summary>Проиграть появление с начала.</summary>
        public void Play()
        {
            if (group == null)
            {
                group = GetComponent<CanvasGroup>();
                rect = transform as RectTransform;
            }

            elapsed = 0f;
            playing = true;
            Apply(0f);
        }

        private void Update()
        {
            if (!playing)
            {
                return;
            }

            elapsed += Time.unscaledDeltaTime;
            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            Apply(t);

            if (t >= 1f)
            {
                playing = false;
            }
        }

        private void Apply(float t)
        {
            // Замедление к концу: рывок в начале и мягкая остановка читаются
            // как «панель приехала», а не «панель мигнула».
            float eased = 1f - (1f - t) * (1f - t);
            group.alpha = eased;

            if (rect != null)
            {
                float scale = Mathf.LerpUnclamped(fromScale, 1f, eased);
                rect.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }
}
