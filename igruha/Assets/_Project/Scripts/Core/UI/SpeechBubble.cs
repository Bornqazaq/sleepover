using TMPro;
using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Пузырь с репликой над головой персонажа. Мировой объект, а не элемент
    /// экранного HUD: реплику должны читать все, кто рядом, а не только тот,
    /// кто её сказал.
    ///
    /// Пузырь не создаётся на лету и не уничтожается: он висит в сцене
    /// выключенным и включается на время реплики. Так в игровом цикле нет
    /// ни одной аллокации, а число пузырей известно заранее — по числу мест,
    /// с которых вообще можно говорить.
    /// </summary>
    public sealed class SpeechBubble : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text label;

        [Tooltip("На сколько пузырь висит над точкой привязки, метры")]
        [SerializeField] private float heightOffset = 1.1f;

        private Transform anchor;
        private float hideAt;
        private Camera view;

        /// <summary>Пузырь сейчас показан.</summary>
        public bool IsVisible { get; private set; }

        private void Awake()
        {
            SetRootActive(false);
        }

        /// <summary>
        /// К кому пузырь привязан. Пусто — пузырь висит там, где стоит сам
        /// объект: так его можно поставить над стулом, а не над персонажем.
        /// </summary>
        public void AttachTo(Transform target) => anchor = target;

        /// <summary>Показать реплику на заданное время.</summary>
        public void Show(string text, float seconds)
        {
            if (label != null)
            {
                label.text = text;
            }

            hideAt = Time.time + Mathf.Max(0.1f, seconds);
            IsVisible = true;
            SetRootActive(true);
        }

        /// <summary>Убрать немедленно: кон кончился, игрок встал из-за стола, матч закрылся.</summary>
        public void Hide()
        {
            IsVisible = false;
            SetRootActive(false);
        }

        private void LateUpdate()
        {
            if (!IsVisible)
            {
                return;
            }

            if (Time.time >= hideAt)
            {
                Hide();
                return;
            }

            if (anchor != null)
            {
                transform.position = anchor.position + Vector3.up * heightOffset;
            }

            FaceCamera();
        }

        /// <summary>
        /// Развернуть пузырь к камере. Камера ищется лениво и запоминается:
        /// в мини-играх она переключается между ригами, поэтому ссылку
        /// приходится перечитывать, когда прежняя выключилась.
        /// </summary>
        private void FaceCamera()
        {
            if (view == null || !view.isActiveAndEnabled)
            {
                view = Camera.main;
            }

            if (view == null)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(transform.position - view.transform.position, Vector3.up);
        }

        private void SetRootActive(bool value)
        {
            GameObject target = root != null ? root : gameObject;

            // Выключать себя целиком нельзя: с выключенным объектом не приедет
            // LateUpdate, и пузырь больше никогда не покажется.
            if (target == gameObject)
            {
                if (label != null)
                {
                    label.enabled = value;
                }

                return;
            }

            target.SetActive(value);
        }
    }
}
