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
