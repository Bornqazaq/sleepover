using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    /// <summary>Независимые колонки: длинное имя не может сдвинуть результат или очки.</summary>
    public sealed class ResultRow : MonoBehaviour
    {
        [SerializeField] private Image badge;
        [SerializeField] private TMP_Text placeText;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private Image background;
        [SerializeField] private Image portrait;
        [SerializeField] private Image accent;
        [SerializeField] private GameObject localMark;
        [SerializeField] private TMP_Text noteText;
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private TMP_Text pointsText;
        [SerializeField] private TMP_Text totalText;

        private static readonly Color RowPaper = new Color(.90f, .93f, .86f);
        private static readonly Color WinnerPaper = new Color(1f, .87f, .57f);

        // Совместимость с предпросмотром старых сцен. Новые итоги используют Show.
        public void Set(int place, string playerName, string detail = null) =>
            Show(place, playerName, null, false, new RoundResultDetail(detail ?? "—"), "", "");

        public void Show(int place, string playerName, Sprite face, bool local,
                         RoundResultDetail detail, string points, string total)
        {
            if (background != null) background.color = place == 1 ? WinnerPaper : RowPaper;
            if (badge != null) badge.color = MinigameUiStyle.Ink;
            if (placeText != null)
            {
                placeText.text = place.ToString("00");
                placeText.color = badge != null ? MinigameUiStyle.Paper : MinigameUiStyle.Ink;
            }
            if (nameText != null)
            {
                nameText.richText = false;
                nameText.text = playerName;
                nameText.color = MinigameUiStyle.Ink;
            }
            if (portrait != null) { portrait.sprite = face; portrait.enabled = face != null; }
            if (localMark != null) localMark.SetActive(local);
            if (accent != null) accent.color = detail.Accent;
            if (noteText != null) { noteText.richText = false; noteText.text = detail.Note; }
            if (valueText != null) valueText.text = detail.Value;
            if (pointsText != null) pointsText.text = points;
            if (totalText != null) totalText.text = total;
            gameObject.SetActive(true);
        }

        public void Clear() => gameObject.SetActive(false);
    }
}
