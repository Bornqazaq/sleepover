using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Core.Hub
{
    public sealed class ConsoleGameCard : MonoBehaviour
    {
        [SerializeField] private Image cover;
        [SerializeField] private Image border;
        [SerializeField] private Image surface;
        [SerializeField] private TMP_Text title;
        [SerializeField] private GameObject selectedMark;
        [SerializeField] private Color idleBorder = new Color(0.18f, 0.27f, 0.29f);
        [SerializeField] private Color idleSurface = new Color(0.09f, 0.16f, 0.18f);
        [SerializeField] private Color selectedSurface = new Color(0.19f, 0.29f, 0.30f);

        public void Bind(string label, Sprite artwork, bool playable)
        {
            title.text = playable ? label : label + " · скоро";
            cover.sprite = artwork;
            cover.enabled = artwork != null;
            cover.color = playable ? Color.white : new Color(0.4f, 0.4f, 0.4f);
        }

        public void SetSelected(bool selected, Color accent)
        {
            border.color = selected ? accent : idleBorder;
            surface.color = selected ? selectedSurface : idleSurface;
            title.color = selected ? Color.white : new Color(0.73f, 0.80f, 0.79f);
            selectedMark.SetActive(selected);
        }
    }
}
