using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Items
{
    /// <summary>
    /// Бесконечная кучка расходников: кирпичи, банки краски, снежки. Выдаёт по
    /// одному предмету на E и пополняется мгновенно.
    ///
    /// Отдельно от <see cref="PickupItem"/> потому, что предмет в кучке — не
    /// предмет, а обещание предмета: пока его не взяли, его физически нет, и
    /// синхронизировать нечего. Иначе на арене пришлось бы держать сотню
    /// лежащих кирпичей, каждый со своим телом и сетевым объектом.
    ///
    /// Запас бесконечен, но выданные предметы не копятся бесконечно: дойдя до
    /// потолка, кучка убирает самый старый из тех, что никто не держит. Для
    /// игрока это незаметно — кучка по-прежнему не кончается никогда, — зато
    /// за 200 секунд раунда арена не зарастает мусором.
    /// </summary>
    public sealed class ItemDispenser : MonoBehaviour, IInteractable
    {
        [Tooltip("Что выдаём. Префаб с PickupItem")]
        [SerializeField] private PickupItem itemPrefab;
        [Tooltip("Откуда появляется предмет. Пусто — из этого же объекта")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Подсказка над кучкой")]
        [SerializeField] private string prompt = "Взять кирпич (E)";
        [Tooltip("Сколько выданных предметов держим на арене. Дойдя до потолка, кучка убирает самый старый свободный")]
        [SerializeField] private int maxLiveItems = 24;

        private readonly List<PickupItem> dispensed = new List<PickupItem>(32);

        public string InteractionPrompt => prompt;

        private void Awake()
        {
            if (itemPrefab == null)
            {
                Debug.LogError($"{name}: ItemDispenser без префаба предмета — выдавать нечего.", this);
                enabled = false;
            }

            if (spawnPoint == null)
            {
                spawnPoint = transform;
            }
        }

        public bool CanInteract(PlayerController player)
        {
            if (!enabled || player == null)
            {
                return false;
            }

            // Нести можно ровно один предмет за раз, и не тогда, когда руки
            // заняты чем-то ещё (ручкой бутыли, например).
            PlayerCarryAbility carry = player.GetComponent<PlayerCarryAbility>();
            return carry != null && !carry.IsCarrying && !carry.HandsBlocked;
        }

        /// <summary>
        /// Выдать предмет. По сети сюда приводит серверное взаимодействие
        /// <c>PlayerInteractor</c>, то есть решение уже принял авторитет —
        /// он же и спавнит сетевой объект.
        /// </summary>
        public void Interact(PlayerController player)
        {
            if (!WorldAuthority.HasAuthority || !CanInteract(player))
            {
                return;
            }

            TrimDispensed();

            PickupItem item = Instantiate(itemPrefab, spawnPoint.position, spawnPoint.rotation);
            item.name = $"{itemPrefab.name}_{dispensed.Count + 1}";
            dispensed.Add(item);

            // В сетевой катке предмет обязан быть заспавнен сервером, иначе его
            // не увидит никто, кроме хоста.
            if (WorldAuthority.IsNetworkSession && item.TryGetComponent(out NetworkObject netObject))
            {
                netObject.Spawn();
            }

            player.GetComponent<PlayerCarryAbility>()?.TryPickup(item);
        }

        /// <summary>
        /// Освободить место под новый предмет. Убираем только те, что никто не
        /// держит: выдернуть кирпич из рук на замахе означало бы отнять у игрока
        /// уже сделанное действие.
        /// </summary>
        private void TrimDispensed()
        {
            for (int i = dispensed.Count - 1; i >= 0; i--)
            {
                if (dispensed[i] == null)
                {
                    dispensed.RemoveAt(i);
                }
            }

            while (dispensed.Count >= maxLiveItems)
            {
                int oldestFree = -1;
                for (int i = 0; i < dispensed.Count; i++)
                {
                    if (!dispensed[i].IsHeld)
                    {
                        oldestFree = i;
                        break;
                    }
                }

                // Все выданные в руках — редкий случай на полном лобби. Ждём,
                // пока хоть один бросят: отнимать нельзя, а потолок мягкий.
                if (oldestFree < 0)
                {
                    return;
                }

                PickupItem victim = dispensed[oldestFree];
                dispensed.RemoveAt(oldestFree);

                if (WorldAuthority.IsNetworkSession && victim.TryGetComponent(out NetworkObject netObject) &&
                    netObject.IsSpawned)
                {
                    netObject.Despawn();
                    continue;
                }

                Destroy(victim.gameObject);
            }
        }
    }
}
