using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Player;

namespace Igruha.Core.Hub
{
    /// <summary>
    /// Телевизор в хабе как единственная точка входа в мини-игры: подошёл,
    /// нажал — включилась приставка.
    ///
    /// Вешается на телевизор, а не на отдельный предмет у каждой игры.
    /// Предметы-якори по всему хабу отменены: набор игр теперь живёт в
    /// каталоге (<see cref="Igruha.Core.Minigame.MinigameCatalog"/>), а не в
    /// расстановке реквизита.
    ///
    /// Включает только хост. Это не техническое ограничение, а правило: экран
    /// один на комнату, и решать, когда он зажжётся, должен один человек.
    /// Остальным честно пишем это в подсказке, чтобы не жали в пустоту.
    ///
    /// Взаимодействие помечено местным (<see cref="ILocalInteraction"/>):
    /// нажатие само по себе ничего в общем состоянии не меняет, а решение
    /// уходит на сервер уже из <see cref="ConsoleMenu"/>.
    /// </summary>
    public sealed class ConsoleTerminal : MonoBehaviour, IInteractable, ILocalInteraction
    {
        // Подсказка говорит «зажми», потому что действие Interact сидит на Hold
        // (0.4 с) — замороженная привязка, см. igruha/CLAUDE.md, раздел 0.
        // Обычные IInteractable вроде этого срабатывают через
        // WasPerformedThisFrame, а он при Hold молчит до конца удержания:
        // короткое нажатие E не сделает ничего. Формулировка «E — включить»
        // это скрывала, и на плейтесте 17.08 (IGR-328) человек решил, что
        // сломана игра. Тогда цена была «не запустилась одна игра из девяти»,
        // теперь телевизор — единственный вход, и цена выросла до всей игры.

        [SerializeField] private ConsoleMenu menu;
        [Tooltip("Как называется предмет в подсказке")]
        [SerializeField] private string deviceName = "Приставка";

        public string InteractionPrompt
        {
            get
            {
                if (menu != null && menu.IsOpen)
                {
                    return $"{deviceName} — включена";
                }

                return CanStartHere
                    ? $"Зажми E — включить: {deviceName}"
                    : $"{deviceName} — включает хост";
            }
        }

        /// <summary>Эта машина вправе включить приставку: одиночная сцена или хост.</summary>
        private static bool CanStartHere
        {
            get
            {
                Unity.Netcode.NetworkManager network = Unity.Netcode.NetworkManager.Singleton;
                return network == null || !network.IsListening || network.IsServer;
            }
        }

        // Подсказку показываем всем, включая клиентов: «включает хост» — это
        // тоже ответ, и он полезнее молчания.
        public bool CanInteract(PlayerController player) => true;

        public void Interact(PlayerController player)
        {
            if (menu == null || !CanStartHere)
            {
                return;
            }

            menu.RequestOpen();
        }
    }
}
