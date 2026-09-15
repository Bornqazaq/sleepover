using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Igruha.Core.Hub
{
    public sealed class ConsoleGameCard : Button, ICancelHandler
    {
        [SerializeField] private Image cover;
        [SerializeField] private Image border;
        [SerializeField] private Image surface;
        [SerializeField] private TMP_Text title;
        [SerializeField] private GameObject selectedMark;
        [SerializeField] private Color idleBorder = new Color(0.18f, 0.27f, 0.29f);
        [SerializeField] private Color idleSurface = new Color(0.09f, 0.16f, 0.18f);
        [SerializeField] private Color selectedSurface = new Color(0.19f, 0.29f, 0.30f);

        public int CatalogIndex { get; set; }
        private bool selected;
        private bool hovered;
        private Color selectedAccent;

        public override void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && IsInteractable())
            { Select(); ConsoleMenu.Active?.SelectGame(CatalogIndex); }
        }
        public override void OnSelect(BaseEventData e)
        {
            base.OnSelect(e);
            ConsoleMenu.Active?.SelectGame(CatalogIndex);
        }
        public void OnCancel(BaseEventData e) { ConsoleMenu.Active?.Back(); e.Use(); }
        public override void OnSubmit(BaseEventData e)
        {
            if (IsInteractable()) ConsoleMenu.Active?.Launch();
        }
        public override void OnPointerEnter(PointerEventData e) { base.OnPointerEnter(e); hovered = true; Paint(); }
        public override void OnPointerExit(PointerEventData e) { base.OnPointerExit(e); hovered = false; Paint(); }
        protected override void OnDisable() { hovered = false; base.OnDisable(); }

        public void Bind(string label, Sprite artwork, bool playable)
        {
            border.raycastTarget = true; targetGraphic = border; transition = Transition.None;
            interactable = playable;
            title.text = playable ? label : label + " · скоро";
            cover.sprite = artwork;
            cover.enabled = artwork != null;
            cover.color = playable ? Color.white : new Color(0.4f, 0.4f, 0.4f);
        }

        public void SetSelected(bool selected, Color accent)
        {
            this.selected = selected; selectedAccent = accent; Paint();
        }
        private void Paint()
        {
            if (border == null) return;
            border.color = selected ? selectedAccent : hovered && IsInteractable() ? new Color(.96f, .74f, .48f) : idleBorder;
            surface.color = selected ? selectedSurface : idleSurface;
            title.color = selected ? Color.white : new Color(0.73f, 0.80f, 0.79f);
            selectedMark.SetActive(selected);
        }
    }
}
