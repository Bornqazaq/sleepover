using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Igruha.Core.Hub
{
    /// <summary>Visible pointer/keyboard feedback for the television; selection stays in EventSystem.</summary>
    [RequireComponent(typeof(Selectable))]
    public sealed class ConsoleButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler, IPointerDownHandler, IPointerUpHandler, ICancelHandler
    {
        [SerializeField] private Image outline;
        [SerializeField] private Image surface;
        [SerializeField] private TMP_Text heading, description, arrow;
        [SerializeField] private bool fillSelection;
        [SerializeField] private bool followPointer;
        [SerializeField] private Color idle = new Color(.055f, .105f, .12f, .97f);
        [SerializeField] private Color accent = new Color(.96f, .74f, .48f);
        [SerializeField, Min(.01f)] private float transitionSeconds = .12f;
        private static readonly Color Cream = new Color(.96f, .93f, .86f);
        private static readonly Color Ink = new Color(.06f, .105f, .12f);
        private Selectable control;
        private bool hovered, pressed;
        private float emphasis;

        public void Configure(Image rim, Image background = null, TMP_Text title = null,
            TMP_Text subtitle = null, TMP_Text marker = null, bool fill = false)
        {
            outline = rim; surface = background; heading = title; description = subtitle;
            arrow = marker; fillSelection = fill; followPointer = fill;
        }
        private void Awake() => control = GetComponent<Selectable>();
        private void OnEnable() { hovered = pressed = false; emphasis = 0; }
        private void OnDisable() { hovered = pressed = false; }
        private void LateUpdate()
        {
            bool available = control.IsInteractable();
            bool focused = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
            float target = available && (focused || hovered) ? 1 : 0;
            emphasis = Mathf.MoveTowards(emphasis, target, Time.unscaledDeltaTime / transitionSeconds);
            if (outline != null) outline.color = new Color(accent.r, accent.g, accent.b, emphasis);
            if (!fillSelection) return;
            surface.color = Color.Lerp(idle, accent, emphasis);
            if (pressed && available) surface.color *= .84f;
            heading.color = Color.Lerp(Cream, Ink, emphasis);
            description.color = Color.Lerp(new Color(.69f,.77f,.76f), Ink, emphasis);
            arrow.color = Color.Lerp(Cream, Ink, emphasis);
            if (!available) { heading.color *= .6f; description.color *= .6f; arrow.color *= .6f; }
        }
        public void OnPointerEnter(PointerEventData e)
        {
            hovered = true;
            if (followPointer && control.IsInteractable()) control.Select();
        }
        public void OnPointerExit(PointerEventData e) { hovered = false; pressed = false; }
        public void OnSelect(BaseEventData e) { }
        public void OnDeselect(BaseEventData e) { pressed = false; }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) pressed = true; }
        public void OnPointerUp(PointerEventData e) { pressed = false; }
        public void OnCancel(BaseEventData e)
        {
            if (control is TMP_InputField) return; // The field distinguishes cancelling text from leaving the page.
            ConsoleMenu.Active?.Back(); e.Use();
        }
    }
}
