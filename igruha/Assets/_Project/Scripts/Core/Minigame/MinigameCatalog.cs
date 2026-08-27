using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Список всех мини-игр проекта — то, из чего игроки выбирают на экране
    /// приставки в хабе.
    ///
    /// Раньше набор задавался расстановкой предметов-якорей по хабу: сколько
    /// якорей поставили, столько игр и существовало. Это разъезжалось —
    /// готовая игра без якоря была недоступна, а якорь без игры притворялся
    /// входом. Теперь список один и лежит здесь, а хаб про состав игр не знает
    /// вовсе.
    ///
    /// Игра без имени сцены остаётся в списке, но показывается серой: так
    /// видно, что игра в работе, и не приходится держать пустые заглушки.
    /// </summary>
    [CreateAssetMenu(fileName = "MinigameCatalog", menuName = "Igruha/Minigame Catalog")]
    public sealed class MinigameCatalog : ScriptableObject
    {
        [Tooltip("Порядок здесь — порядок на экране приставки")]
        [SerializeField] private List<MinigameDefinition> games = new List<MinigameDefinition>();

        public IReadOnlyList<MinigameDefinition> Games => games;

        /// <summary>Игра под этим номером. Пусто — номер за пределами списка.</summary>
        public MinigameDefinition Get(int index) =>
            index >= 0 && index < games.Count ? games[index] : null;

        /// <summary>Готова к запуску: есть конфиг и имя сцены в Build Settings.</summary>
        public bool IsPlayable(int index)
        {
            MinigameDefinition game = Get(index);
            return game != null && !string.IsNullOrEmpty(game.SceneName);
        }

        /// <summary>
        /// Номер игры по имени её сцены. Нужен автопрогону: он получает
        /// очередь именами сцен, как они записаны в Build Settings.
        /// Минус один — такой игры в каталоге нет.
        /// </summary>
        public int IndexOfScene(string sceneName)
        {
            for (int i = 0; i < games.Count; i++)
            {
                if (games[i] != null &&
                    string.Equals(games[i].SceneName, sceneName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Первая готовая игра начиная с этого номера по кругу. Минус один — готовых нет вовсе.</summary>
        public int NextPlayable(int from, int step)
        {
            int count = games.Count;
            if (count == 0)
            {
                return -1;
            }

            for (int i = 1; i <= count; i++)
            {
                int index = (((from + step * i) % count) + count) % count;
                if (IsPlayable(index))
                {
                    return index;
                }
            }

            return IsPlayable(from) ? from : -1;
        }
    }
}
