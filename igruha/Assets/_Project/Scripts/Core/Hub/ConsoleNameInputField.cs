using TMPro;
using UnityEngine.EventSystems;
using UnityEngine;
using Igruha.Core.Audio;

namespace Igruha.Core.Hub
{
    /// <summary>Entering edit mode and cancelling it must not submit or reactivate the profile field.</summary>
    public sealed class ConsoleNameInputField : TMP_InputField
    {
        private int editingEndedFrame = -1;
        private Vector2 textRestPosition;
        protected override void Awake()
        {
            base.Awake();
            textRestPosition = textComponent.rectTransform.anchoredPosition;

            // Щелчок клавиши озвучивается по изменению текста, а не по нажатию:
            // поле принимает ввод и с клавиатуры, и с экранной раскладки, и
            // вставкой, а слышно должно быть в любом случае.
            onValueChanged.AddListener(_ => UiAudio.Play(CoreSfx.UiTypeKey));
            onEndEdit.AddListener(_ =>
            {
                editingEndedFrame = Time.frameCount;
                // TMP otherwise leaves a short restored name scrolled outside the viewport after Escape.
                textComponent.rectTransform.anchoredPosition = textRestPosition;
                ForceLabelUpdate();
            });
        }
        public override void OnSubmit(BaseEventData eventData)
        {
            if (!IsActive() || !IsInteractable()) return;
            if (!isFocused)
            {
                ActivateInputField();
                eventData?.Use();
                return;
            }
            base.OnSubmit(eventData);
        }

        public override void OnCancel(BaseEventData eventData)
        {
            // TMP may already have consumed Escape through its text event queue this frame.
            // Its base OnCancel schedules activation when not focused, even across hidden pages.
            if (isFocused) base.OnCancel(eventData);
            else if (editingEndedFrame != Time.frameCount) ConsoleMenu.Active?.Back();
            eventData?.Use();
        }
    }
}
