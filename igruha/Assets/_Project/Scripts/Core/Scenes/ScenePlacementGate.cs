using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.Core.Scenes
{
    /// <summary>
    /// Шлюз расстановки: «сцену догрузили все, и общая раскладка по точкам
    /// спавна уже сделана — можно начинать мини-игру».
    ///
    /// Заведён затем, что расстановкой занимаются двое и раньше они не
    /// договаривались. Общий размещатель (<c>NetworkScenePlayerPlacer</c>)
    /// работает по событию NGO «сцену догрузил последний клиент», а мини-игра
    /// сажает людей по своим местам — в клетки, кресла, на платформы — через
    /// секунду после того, как у неё собрался состав. На одной машине сцена
    /// грузится из кеша быстрее секунды, и порядок выходил правильный; на
    /// шести живых машинах загрузка дольше, и общий телепорт прилетал
    /// <b>после</b> рассадки и выдёргивал всех с их мест. Отсюда «часть людей
    /// стоит вне клеток рядом с медведем» и «кто-то улетел за арену».
    ///
    /// Порядок теперь задан явно: сначала общая раскладка, потом мини-игра.
    ///
    /// Сети нет (сцену открыли прямо из редактора) — ждать нечего и шлюз
    /// открыт всегда: там расстановкой занимается сам <c>PlayerSpawner</c>.
    /// </summary>
    public static class ScenePlacementGate
    {
        private static bool opened;

        /// <summary>Шлюз открыт настоящим сетевым событием, а не отсутствием сети.</summary>
        public static bool OpenedByNetwork => opened;

        /// <summary>
        /// Можно начинать мини-игру.
        ///
        /// Без сети — сразу: события загрузки там никто не пришлёт.
        /// </summary>
        public static bool IsOpen => opened || !IsNetworkSession;

        private static bool IsNetworkSession =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        /// <summary>Общая раскладка по точкам сделана. Зовёт размещатель на каждой машине.</summary>
        public static void Open() => opened = true;

        /// <summary>
        /// Новая сцена — шлюз закрыт до её события загрузки.
        ///
        /// Подписка живёт здесь, а не в размещателе: закрывать шлюз обязана
        /// каждая машина, а размещатель расставляет только на сервере.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            // Статика переживает выход из плей-мода при выключенной
            // перезагрузке домена — начинаем с закрытого шлюза явно.
            opened = false;

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
            {
                opened = false;
            }
        }
    }
}
