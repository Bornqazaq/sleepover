using TMPro;
using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Одна грань мирового табло. Хранит ссылки на свои текстовые поля,
    /// чтобы <see cref="WorldScoreboard"/> писал в них напрямую, без поиска
    /// по иерархии в рантайме.
    /// </summary>
    public sealed class WorldScoreboardFace : MonoBehaviour
    {
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text subtitle;
        [Tooltip("Подписи строк — имена игроков")]
        [SerializeField] private TMP_Text[] rowLabels = System.Array.Empty<TMP_Text>();
        [Tooltip("Значения строк — результат, ошибки")]
        [SerializeField] private TMP_Text[] rowValues = System.Array.Empty<TMP_Text>();

        [Tooltip("Подложка, которая обрезается по последней занятой строке. Пусто — не трогать высоту (так у мировых граней)")]
        [SerializeField] private RectTransform autoHeightPanel;

        [Tooltip("Отступ подложки под последней строкой, пикселей канваса")]
        [SerializeField] private float autoHeightPadding = 12f;

        public int RowCapacity => Mathf.Min(rowLabels.Length, rowValues.Length);

        public void SetTitle(string text) => Assign(title, text);

        public void SetSubtitle(string text) => Assign(subtitle, text);

        public void SetRow(int index, string label, string value)
        {
            if (index < 0 || index >= RowCapacity)
            {
                return;
            }

            rowLabels[index].gameObject.SetActive(true);
            rowValues[index].gameObject.SetActive(true);
            Assign(rowLabels[index], label);
            Assign(rowValues[index], value);
        }

        public void HideRowsFrom(int index)
        {
            for (int i = Mathf.Max(0, index); i < RowCapacity; i++)
            {
                rowLabels[i].gameObject.SetActive(false);
                rowValues[i].gameObject.SetActive(false);
            }

            FitPanel(Mathf.Clamp(index, 0, RowCapacity));
        }

        /// <summary>
        /// Обрезать подложку по последней занятой строке.
        ///
        /// Нужно только экранной грани: её панель рассчитана на восемь строк,
        /// а лобби бывает и на двоих — тогда нижняя половина оставалась пустым
        /// тёмным прямоугольником в четверть экрана. Мировые грани панель
        /// не задают вовсе, и для них метод — пустой проход.
        ///
        /// Высота считается по фактическому низу последней строки, а не по
        /// числу строк на высоту строки: раскладку задаёт билдер, и второй
        /// её копии здесь заводить не нужно.
        /// </summary>
        private void FitPanel(int visibleRows)
        {
            if (autoHeightPanel == null)
            {
                return;
            }

            RectTransform last = visibleRows > 0 ? rowValues[visibleRows - 1].rectTransform : SubtitleRect();
            if (last == null)
            {
                return;
            }

            // Строки прижаты к верху панели, поэтому anchoredPosition.y у них
            // отрицательный и растёт вниз по модулю.
            float bottom = -last.anchoredPosition.y + last.rect.height * last.pivot.y;
            autoHeightPanel.sizeDelta = new Vector2(autoHeightPanel.sizeDelta.x, bottom + autoHeightPadding);
        }

        private RectTransform SubtitleRect()
        {
            if (subtitle != null)
            {
                return subtitle.rectTransform;
            }

            return title != null ? title.rectTransform : null;
        }

        /// <summary>
        /// Присваивание с проверкой на совпадение. Без неё TMP пересобирает
        /// меш на каждое присваивание, даже если текст не изменился, —
        /// а табло обновляется у восьми игроков сразу.
        /// </summary>
        private static void Assign(TMP_Text field, string text)
        {
            if (field == null || field.text == text)
            {
                return;
            }

            field.text = text;
        }
    }
}
