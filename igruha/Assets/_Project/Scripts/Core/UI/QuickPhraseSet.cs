using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Набор готовых реплик: один ассет — один набор под одну роль.
    ///
    /// Живёт в Core, а не в папке мини-игры, намеренно. Голосового чата
    /// в проекте нет, и реплики по кнопке — общая замена разговору: их просит
    /// «Верю / не верю», их же попросит «Крокодил» (EPIC 16) и любая другая
    /// социальная игра. Побочная выгода — снимается модерация в публичных лобби.
    /// </summary>
    [CreateAssetMenu(menuName = "Igruha/Quick Phrase Set", fileName = "QuickPhraseSet")]
    public sealed class QuickPhraseSet : ScriptableObject
    {
        /// <summary>
        /// Больше девяти в набор не влезет: выбор идёт цифрами 1–9, а десятая
        /// клавиша осталась бы без реплики.
        /// </summary>
        public const int MaxPhrases = 9;

        [Tooltip("Кому этот набор — для читаемости в инспекторе, в игре не показывается")]
        [SerializeField] private string roleName = "Роль";

        [Tooltip("Реплики по порядку. Первая — клавиша 1. Не больше девяти")]
        [TextArea]
        [SerializeField] private string[] phrases = System.Array.Empty<string>();

        public string RoleName => roleName;

        public int Count => phrases != null ? Mathf.Min(phrases.Length, MaxPhrases) : 0;

        /// <summary>Реплика по индексу. Индекс вне набора — пустая строка, а не исключение.</summary>
        public string Get(int index)
        {
            if (phrases == null || index < 0 || index >= Count)
            {
                return string.Empty;
            }

            return phrases[index];
        }

        /// <summary>
        /// Индекс принадлежит этому набору. Отдельный метод, потому что тем же
        /// вопросом занимается сервер в фазе 3: у Знающего и Решающего наборы
        /// разной длины, и подмену индекса надо отбивать, а не подрезать.
        /// </summary>
        public bool IsValidIndex(int index) => index >= 0 && index < Count;

        private void OnValidate()
        {
            if (phrases != null && phrases.Length > MaxPhrases)
            {
                Debug.LogWarning($"{name}: в наборе {phrases.Length} реплик, а выбор идёт цифрами 1–{MaxPhrases}. " +
                                 $"Лишние показаны не будут", this);
            }
        }
    }
}
