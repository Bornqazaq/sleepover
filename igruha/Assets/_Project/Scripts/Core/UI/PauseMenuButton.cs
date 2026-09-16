using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    [RequireComponent(typeof(Button))]
    public sealed class PauseMenuButton : MonoBehaviour, IPointerEnterHandler
    {
        [SerializeField] private Image surface, accent;
        [SerializeField] private TMP_Text label, marker;
        private Button button;
        private float emphasis;
        private static readonly Color Rest = new Color(.075f, .14f, .15f);
        private static readonly Color Gold = new Color(.94f, .73f, .46f);
        private static readonly Color Cream = new Color(.96f, .93f, .85f);
        private static readonly Color Ink = new Color(.04f, .075f, .075f);
        private const float TransitionSeconds = .12f;
        private void Awake() => button = GetComponent<Button>();
        private void OnEnable() { emphasis = 0; Paint(); }
        private void Update()
        {
            bool selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
            emphasis = Mathf.MoveTowards(emphasis, selected ? 1 : 0, Time.unscaledDeltaTime / TransitionSeconds);
            Paint();
        }
        private void Paint()
        {
            surface.color = Color.Lerp(Rest, Gold, emphasis);
            label.color = marker.color = Color.Lerp(Cream, Ink, emphasis);
            accent.color = new Color(Gold.r, Gold.g, Gold.b, Mathf.Lerp(.3f, 1, emphasis));
        }
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (button.IsInteractable()) button.Select();
        }
    }
}
