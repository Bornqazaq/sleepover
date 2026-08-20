using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Player;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Полка с банками — единственное физическое действие игрока во всей игре.
    /// Всё остальное правила надстраивают поверх него.
    ///
    /// Три действия, все на E, и какое именно сработает — решает слот, на который
    /// игрок смотрит:
    /// — слот занят, руки пусты   → взять банку, слот пустеет;
    /// — слот пуст, в руке банка  → поставить, рука освобождается;
    /// — слот занят, в руке банка → поменять местами.
    ///
    /// Отсюда следует свойство, на которое опирается вся дальнейшая логика:
    /// <b>на полке всегда корректная перестановка всех банок, минус не более
    /// одной в руке.</b> Невалидной расстановки не существует, поэтому серверу
    /// в 9.8 нечего проверять, кроме самого факта перестановки, и «подтвердить»
    /// никогда не отваливается по правилам.
    ///
    /// <b>Полка — <see cref="ILocalInteraction"/> намеренно.</b> Что стоит на полке
    /// у соседа, разобрать с другой клетки невозможно, значит чужая полка не несёт
    /// информации и возить её по сети незачем (спека 10.2). Перестановка идёт
    /// локально и без задержки; по сети за круг уходит один пакет — подтверждённая
    /// расстановка, и отправляет её кнопка, а не полка.
    /// </summary>
    public sealed class CanShelf : MonoBehaviour, IInteractable, ILocalInteraction
    {
        private const string PromptTake = "Взять банку";
        private const string PromptPlace = "Поставить банку";
        private const string PromptSwap = "Поменять местами";

        [Header("Геометрия")]
        [Tooltip("Точка, от которой раскладываются слоты: верх доски полки. Слоты идут вдоль её локальной оси X")]
        [SerializeField] private Transform slotsRoot;
        [Tooltip("Длина ряда слотов, м. Полка 3 ШП = 2.16 м, ряд чуть короче — по краям остаются поля")]
        [SerializeField] private float slotSpan = 1.9f;
        [Tooltip("Диаметр банки, м")]
        [SerializeField] private float canDiameter = 0.14f;
        [Tooltip("Высота банки, м")]
        [SerializeField] private float canHeight = 0.24f;

        [Header("Рука")]
        [Tooltip("Куда прицепляется взятая банка — смещение от корня персонажа, м. Правее и выше центра, чтобы её было видно перед собой")]
        [SerializeField] private Vector3 handOffset = new Vector3(0.3f, 1.05f, 0.4f);

        [Header("Прицеливание")]
        [Tooltip("На сколько метров перед собой игрок «указывает», выбирая слот. Слоты идут через ~0.4 м, и слот выбирается тем, куда игрок смотрит и где стоит")]
        [SerializeField] private float slotPickReach = 0.55f;

        /// <summary>Расстановка на полке изменилась: взяли, поставили или поменяли местами.</summary>
        public event Action<CanShelf> ArrangementChanged;

        private readonly List<Transform> slots = new List<Transform>(8);
        private readonly List<Can> occupants = new List<Can>(8);
        private readonly List<Can> cans = new List<Can>(8);

        private PlayerController owner;
        private Can held;
        private string prompt = PromptTake;
        private int aimedSlot = -1;

        /// <summary>
        /// Открыто ли окно выставления. Вне его полка не отзывается: круг уже
        /// разобран, и переставлять банки поздно.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>Сколько слотов на полке. Равно числу банок в задании.</summary>
        public int SlotCount => slots.Count;

        /// <summary>В руке банка. Пока это так, подтверждать расстановку нельзя — она неполная.</summary>
        public bool HandBusy => held != null;

        public string InteractionPrompt => prompt;

        /// <summary>
        /// Построить полку под задание из <paramref name="canCount"/> банок.
        /// Число банок берётся из таблицы конфига и может отличаться от круга
        /// к кругу, поэтому слоты и банки создаются здесь, а не лежат в префабе.
        /// </summary>
        public void Build(CansOrderConfig config, int canCount)
        {
            if (config == null || slotsRoot == null)
            {
                Debug.LogError($"{name}: полке не задан конфиг или корень слотов — строить нечем", this);
                return;
            }

            Clear();

            int count = Mathf.Clamp(canCount, 1, config.PaletteSize);
            for (int i = 0; i < count; i++)
            {
                var slotGo = new GameObject($"Slot_{i + 1}");
                Transform slot = slotGo.transform;
                slot.SetParent(slotsRoot, false);
                slot.localPosition = new Vector3(SlotLocalX(i, count), canHeight * 0.5f, 0f);
                slots.Add(slot);

                Can can = BuildCan(config, i);
                cans.Add(can);
                occupants.Add(can);
                PlaceInSlot(can, i);
            }

            RaiseChanged();
        }

        /// <summary>Кому эта полка принадлежит. Чужую полку тронуть нельзя.</summary>
        public void SetOwner(PlayerController player) => owner = player;

        public bool CanInteract(PlayerController player)
        {
            if (!Active || player == null || player != owner || slots.Count == 0)
            {
                return false;
            }

            aimedSlot = ResolveSlot(player);
            if (aimedSlot < 0)
            {
                return false;
            }

            bool slotBusy = occupants[aimedSlot] != null;

            if (held == null)
            {
                // Пустой слот пустыми руками — делать нечего, и подсказку
                // показывать не за что.
                if (!slotBusy)
                {
                    return false;
                }

                prompt = PromptTake;
                return true;
            }

            prompt = slotBusy ? PromptSwap : PromptPlace;
            return true;
        }

        public void Interact(PlayerController player)
        {
            // Слот уже выбран в CanInteract — PlayerInteractor всегда зовёт её
            // перед Interact, и на своей же машине, тем же кадром.
            if (!CanInteract(player))
            {
                return;
            }

            int slot = aimedSlot;
            Can standing = occupants[slot];

            if (held == null)
            {
                TakeToHand(standing);
                RaiseChanged();
                return;
            }

            // Обмен и постановка — один и тот же путь: та, что стояла, уходит
            // в руку, та, что в руке, встаёт в слот. Когда слот пуст, «та, что
            // стояла» — просто ничто, и рука освобождается.
            Can incoming = held;
            held = null;
            occupants[slot] = null;

            if (standing != null)
            {
                TakeToHand(standing);
            }

            PlaceInSlot(incoming, slot);
            RaiseChanged();
        }

        /// <summary>
        /// Текущая расстановка по слотам. Возвращает <c>false</c>, если банка
        /// в руке: расстановка неполная, и подтверждать её нечем.
        /// </summary>
        public bool TryGetArrangement(List<int> into)
        {
            if (into == null)
            {
                return false;
            }

            into.Clear();
            if (held != null || slots.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] == null)
                {
                    into.Clear();
                    return false;
                }

                into.Add(occupants[i].CanId);
            }

            return true;
        }

        /// <summary>
        /// Разложить банки в заданном порядке. Стартовую расстановку раунда
        /// назначает сервер — у каждого игрока свою, иначе первый круг дал бы
        /// один отклик на всех вместо восьми.
        /// </summary>
        public void SetArrangement(IReadOnlyList<int> canIds)
        {
            if (canIds == null || canIds.Count != slots.Count)
            {
                Debug.LogWarning($"{name}: расстановка из {(canIds == null ? 0 : canIds.Count)} банок " +
                                 $"не ложится на {slots.Count} слотов — полка оставлена как была", this);
                return;
            }

            ReturnHeldCan();

            for (int i = 0; i < occupants.Count; i++)
            {
                occupants[i] = null;
            }

            for (int i = 0; i < canIds.Count; i++)
            {
                Can can = FindCan(canIds[i]);
                if (can == null)
                {
                    Debug.LogError($"{name}: банки с идентификатором {canIds[i]} на полке нет", this);
                    continue;
                }

                PlaceInSlot(can, i);
            }

            RaiseChanged();
        }

        /// <summary>
        /// Вернуть банку из руки в свободный слот и отцепить от игрока.
        ///
        /// Зовётся в конце раунда, и это не косметика: банка кинематическая
        /// и прицеплена к персонажу, а персонаж переезжает между сценами живым —
        /// незакрытая рука уедет в хаб вместе с ним (спека 10.5).
        /// </summary>
        public void ReturnHeldCan()
        {
            if (held == null)
            {
                return;
            }

            Can can = held;
            held = null;

            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] == null)
                {
                    PlaceInSlot(can, i);
                    RaiseChanged();
                    return;
                }
            }

            // Свободного слота нет — такого при корректной перестановке
            // не бывает, но бросать банку прицепленной к игроку нельзя.
            can.transform.SetParent(slotsRoot, false);
            can.transform.localPosition = Vector3.up * canHeight;
            can.SlotIndex = -1;
            Debug.LogWarning($"{name}: свободного слота под банку из руки не нашлось — положена на полку без слота", this);
        }

        /// <summary>Снять полку с игрока: и рука пуста, и владельца больше нет.</summary>
        public void Release()
        {
            ReturnHeldCan();
            Active = false;
            owner = null;
        }

        /// <summary>
        /// Взять банку в руку. Слот, из которого она вышла, пустеет.
        /// Банка цепляется к персонажу, а не остаётся в мире: выпустить её
        /// нельзя вообще никак, и держать её больше негде.
        /// </summary>
        private void TakeToHand(Can can)
        {
            if (can == null)
            {
                return;
            }

            if (can.SlotIndex >= 0 && can.SlotIndex < occupants.Count)
            {
                occupants[can.SlotIndex] = null;
            }

            can.SlotIndex = -1;
            held = can;

            Transform hand = owner != null ? owner.transform : slotsRoot;
            can.transform.SetParent(hand, false);
            can.transform.localPosition = owner != null ? handOffset : Vector3.up * canHeight;
            can.transform.localRotation = Quaternion.identity;
        }

        private void PlaceInSlot(Can can, int slotIndex)
        {
            occupants[slotIndex] = can;
            can.SlotIndex = slotIndex;
            can.transform.SetParent(slots[slotIndex], false);
            can.transform.localPosition = Vector3.zero;
            can.transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Слот, на который игрок указывает: ближайший к точке перед ним.
        /// Учитывается и где он стоит, и куда смотрит, — вдоль полки слоты идут
        /// через ~0.4 м, и шага вбок хватает, чтобы выбрать соседний.
        /// </summary>
        private int ResolveSlot(PlayerController player)
        {
            Vector3 aim = player.transform.position + player.Facing * slotPickReach;
            int best = -1;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < slots.Count; i++)
            {
                Vector3 delta = slots[i].position - aim;
                delta.y = 0f;
                float sqr = delta.sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            return best;
        }

        private Can FindCan(int canId)
        {
            for (int i = 0; i < cans.Count; i++)
            {
                if (cans[i] != null && cans[i].CanId == canId)
                {
                    return cans[i];
                }
            }

            return null;
        }

        private float SlotLocalX(int index, int count)
        {
            if (count <= 1)
            {
                return 0f;
            }

            return Mathf.Lerp(-slotSpan * 0.5f, slotSpan * 0.5f, (index + 0.5f) / count);
        }

        /// <summary>
        /// Серая заготовка банки: цилиндр без коллайдера. Коллайдера нет
        /// намеренно — банка не должна попадать в выборку
        /// <c>PlayerInteractor</c>: там буфер на 16 коллайдеров, а в клетке
        /// их и так семь, и пять банок вытеснили бы из выборки кнопку.
        /// Взаимодействие идёт через полку, а не через саму банку.
        /// </summary>
        private Can BuildCan(CansOrderConfig config, int canId)
        {
            var root = new GameObject("Can");
            root.transform.SetParent(slotsRoot, false);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(canDiameter, canHeight * 0.5f, canDiameter);

            // Коллайдер примитива сначала гасится и только потом удаляется:
            // Destroy отложен до конца кадра, и без выключения банки один кадр
            // засоряли бы выборку PlayerInteractor — а буфер там на 16, и
            // в клетке уже семь своих коллайдеров.
            var primitiveCollider = visual.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                primitiveCollider.enabled = false;
                Destroy(primitiveCollider);
            }

            var can = root.AddComponent<Can>();
            can.Configure(canId, config.GetCanKind(canId));
            return can;
        }

        private void Clear()
        {
            for (int i = 0; i < cans.Count; i++)
            {
                if (cans[i] != null)
                {
                    Destroy(cans[i].gameObject);
                }
            }

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                {
                    Destroy(slots[i].gameObject);
                }
            }

            cans.Clear();
            slots.Clear();
            occupants.Clear();
            held = null;
        }

        private void RaiseChanged() => ArrangementChanged?.Invoke(this);
    }
}
