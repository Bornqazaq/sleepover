using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Строка итогов: кружок места, номер и имя игрока. Первые три места
    /// красятся медалью, остальные — общей плашкой.
    ///
    /// Очки и сумма идут той же строкой, что и имя, разметкой TMP с
    /// колонками <c>&lt;pos&gt;</c>: панель собрана в каждой из десяти сцен,
    /// и отдельное поле под очки пришлось бы пересобирать во всех, а забытая
    /// ссылка молчит. Одно поле — ноль пересборок.
    /// </summary>
    public sealed class ResultRow : MonoBehaviour
    {
        [SerializeField] private Image badge;
        [SerializeField] private TMP_Text placeText;
        [SerializeField] private TMP_Text nameText;

        /// <param name="detail">Хвост строки после имени, уже с разметкой колонок. Пусто — только имя.</param>
        public void Set(int place, string playerName, string detail = null)
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
                nameText.text = string.IsNullOrEmpty(detail) ? playerName : playerName + detail;
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
