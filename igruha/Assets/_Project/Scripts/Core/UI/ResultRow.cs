using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Строка итогов раунда: кружок места, номер и имя игрока. Первые три
    /// места красятся медалью, остальные — общей плашкой.
    /// </summary>
    public sealed class ResultRow : MonoBehaviour
    {
        [SerializeField] private Image badge;
        [SerializeField] private TMP_Text placeText;
        [SerializeField] private TMP_Text nameText;

        public void Set(int place, string playerName)
        {
            if (badge != null)
            {
                badge.color = UiSkin.Place(place);
            }

            if (placeText != null)
            {
                placeText.text = place.ToString();
                placeText.color = UiSkin.PlaceInk(place);
            }

            if (nameText != null)
            {
                nameText.text = playerName;
                nameText.color = place == 1 ? UiSkin.Accent : UiSkin.TextPrimary;
            }

            gameObject.SetActive(true);
        }

        public void Clear()
        {
            gameObject.SetActive(false);
        }
    }
}
