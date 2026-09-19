using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Щелчок кнопки интерфейса — общий слой фазы 5.
    ///
    /// Отдельный компонент на кнопку, а не один общий обработчик, потому что
    /// звук у кнопок разный: «Играть» подтверждает, «Назад» отменяет, серая
    /// кнопка отказывает. Разница слышна и несёт смысл, и свести её к одному
    /// щелчку значило бы потерять половину того, зачем звук интерфейса нужен.
    ///
    /// Ставится не мышью, а проходом <c>Igruha/Арт/Озвучить интерфейс сцены</c>:
    /// экраны проекта собираются редакторными билдерами и пересобираются целиком,
    /// так что расставленное руками исчезло бы на первой же пересборке.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class UiButtonSound : MonoBehaviour
    {
        [Tooltip("Слот на нажатие. Пусто — кнопка молчит")]
        [SerializeField] private string clickSlot = CoreSfx.UiClick;

        [Tooltip("Слот на нажатие заблокированной кнопки. Пусто — отказ молчит")]
        [SerializeField] private string blockedSlot = CoreSfx.UiError;

        private Button button;

        private void Awake() => button = GetComponent<Button>();

        private void OnEnable() => button.onClick.AddListener(OnClick);

        private void OnDisable() => button.onClick.RemoveListener(OnClick);

        private void OnClick() => UiAudio.Play(clickSlot);

        /// <summary>
        /// Отказ на заблокированной кнопке. Зовёт экран: <c>onClick</c> у выключенной
        /// кнопки не поднимается вовсе, так что сама кнопка про своё нажатие не узнает.
        /// </summary>
        public void PlayBlocked() => UiAudio.Play(blockedSlot);
    }
}
