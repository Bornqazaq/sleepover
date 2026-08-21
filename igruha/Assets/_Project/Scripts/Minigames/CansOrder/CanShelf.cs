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
    /// <b>Схема — обмен курсором, а не перенос банки в руке.</b> По ряду ходит
    /// подсветка, первое нажатие E отмечает банку, второе на другой — меняет их
    /// местами. Банки в руке не существует как состояния вообще.
    ///
    /// Так сделано после плейтеста 21.08, где прежняя схема оказалась
    /// неиграбельной, и по трём отдельным причинам сразу:
    /// — слот выбирался положением тела, то есть попадать надо было телом
    ///   с точностью ±24 см (слоты идут через 47 см) под таймером в 10 с;
    /// — какой слот на прицеле, <b>не показывалось ничем</b>: поле было,
    ///   подсветки не было, и выбор шёл вслепую;
    /// — взял не ту банку — сначала верни, потом бери другую, то есть цена
    ///   ошибки два лишних нажатия там, где их и так не хватало.
    /// Обмен снимает все три: промахнуться некуда, видно всё, а ошибка стоит
    /// одного нажатия по другой банке.
    ///
    /// <b>Курсор ездит от мыши, а не от A/D.</b> Клавиши движения потребовали бы
    /// заблокировать ходьбу, а <c>PlayerEmoteAbility</c> не открывает колесо при
    /// <c>MovementLocked</c> — Tab молча умер бы на 10 секунд из каждых 16.
    /// Колесо насмешек в замороженном списке ровно из-за такой истории
    /// (STATE, раздел 2c), второй раз наступать не будем. Мышь в окне
    /// выставления ничем не занята: камера в этой стадии стоит на полке.
    ///
    /// Отсюда инвариант, который стал строже прежнего: <b>на полке всегда
    /// корректная перестановка всех банок</b>, в любой момент времени, без
    /// оговорки «минус одна в руке». Неполной расстановки больше не бывает,
    /// поэтому подтвердить нельзя не вовремя, а не «нельзя с банкой в руке».
    ///
    /// <b>Полка — <see cref="ILocalInteraction"/> намеренно.</b> Что стоит на полке
    /// у соседа, разобрать с другой клетки невозможно, значит чужая полка не несёт
    /// информации и возить её по сети незачем (спека 10.2). Перестановка идёт
    /// локально и без задержки; по сети за круг уходит один пакет — подтверждённая
    /// расстановка, и отправляет её кнопка, а не полка.
    /// </summary>
    public sealed class CanShelf : MonoBehaviour, IInteractable, ILocalInteraction
    {
        private const string PromptMark = "Выбрать банку";
        private const string PromptSwap = "Поменять местами";
        private const string PromptUnmark = "Отменить выбор";
        private const string PromptConfirm = "Подтвердить расстановку";

        /// <summary>Курсор ни на чём — полка не строилась или закрыта.</summary>
        private const int NoCell = -1;

        [Header("Геометрия")]
        [Tooltip("Точка, от которой раскладываются слоты: верх доски полки. Слоты идут вдоль её локальной оси X")]
        [SerializeField] private Transform slotsRoot;
        [Tooltip("Длина ряда слотов, м. Полка 3 ШП = 2.16 м, ряд чуть короче — по краям остаются поля")]
        [SerializeField] private float slotSpan = 1.9f;
        [Tooltip("Диаметр банки, м")]
        [SerializeField] private float canDiameter = 0.14f;
        [Tooltip("Высота банки, м")]
        [SerializeField] private float canHeight = 0.24f;

        [Header("Курсор")]
        [Tooltip("Сколько мышь должна пройти, чтобы курсор перескочил на соседнюю банку. Меньше — резче")]
        [SerializeField] private float mouseStepPixels = 60f;
        [Tooltip("На сколько метров перед собой игрок «указывает». Задаёт только СТАРТОВЫЙ слот курсора, когда окно открывается")]
        [SerializeField] private float slotPickReach = 0.55f;

        /// <summary>Расстановка на полке изменилась: банки поменялись местами.</summary>
        public event Action<CanShelf> ArrangementChanged;

        private readonly List<Transform> slots = new List<Transform>(8);
        private readonly List<Can> occupants = new List<Can>(8);
        private readonly List<Can> cans = new List<Can>(8);

        private PlayerController owner;
        private PlayerInputReader reader;
        private CanConfirmButton button;

        private bool active = true;
        private int cursorCell = NoCell;
        private int markedSlot = NoCell;
        private float cursorAccum;

        /// <summary>
        /// Открыто ли окно выставления. Вне его полка не отзывается: круг уже
        /// разобран, и переставлять банки поздно.
        /// </summary>
        public bool Active
        {
            get => active;
            set
            {
                if (active == value)
                {
                    return;
                }

                active = value;

                if (active)
                {
                    // Курсор встаёт туда, где игрок стоит: он и так подошёл
                    // к нужному краю полки, и первое нажатие достаётся даром.
                    cursorCell = NearestSlotToOwner();
                    markedSlot = NoCell;
                    cursorAccum = 0f;
                }
                else
                {
                    cursorCell = NoCell;
                    markedSlot = NoCell;
                }

                RefreshHighlights();
            }
        }

        /// <summary>Сколько слотов на полке. Равно числу банок в задании.</summary>
        public int SlotCount => slots.Count;

        /// <summary>
        /// Доска полки — та точка, вокруг которой стоят слоты. Корень самой
        /// полки для прицела не годится: он сидит в центре клетки, метром
        /// с лишним позади доски и почти на метр ниже её.
        /// </summary>
        public Transform Board => slotsRoot;

        /// <summary>
        /// Полка держит ввод: курсор жив, и нажатие E принадлежит ей, даже
        /// когда игрок стоит вплотную к кнопке. Иначе <c>PlayerInteractor</c>
        /// выбирал бы между двумя интерактивами по расстоянию, и E делал бы
        /// то одно, то другое при неподвижном курсоре.
        /// </summary>
        public bool OwnsInput => active && owner != null && slots.Count > 0;

        /// <summary>Индекс ячейки «подтвердить» — сразу за последней банкой.</summary>
        private int ConfirmCell => slots.Count;

        /// <summary>Последняя ячейка ряда: с кнопкой или без неё.</summary>
        private int LastCell => button != null ? ConfirmCell : slots.Count - 1;

        public string InteractionPrompt
        {
            get
            {
                if (cursorCell == NoCell)
                {
                    return PromptMark;
                }

                if (cursorCell == ConfirmCell)
                {
                    return PromptConfirm;
                }

                if (markedSlot == NoCell)
                {
                    return PromptMark;
                }

                return markedSlot == cursorCell ? PromptUnmark : PromptSwap;
            }
        }

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
        public void SetOwner(PlayerController player)
        {
            owner = player;
            // Ридер нужен только ради мыши, и только у того, кем управляют
            // с этой машины: у болванок и чужих игроков курсора нет вовсе.
            reader = player != null ? player.GetComponent<PlayerInputReader>() : null;
        }

        /// <summary>
        /// Кнопка подтверждения этой же клетки. Она становится последней
        /// ячейкой того же ряда, и до неё не надо идти ногами: ряд читается
        /// как «пять банок и кнопка», курсор доезжает до неё за пару движений.
        /// </summary>
        public void SetButton(CanConfirmButton ownerButton) => button = ownerButton;

        public bool CanInteract(PlayerController player)
        {
            return active && player != null && player == owner && slots.Count > 0;
        }

        public void Interact(PlayerController player)
        {
            if (!CanInteract(player))
            {
                return;
            }

            if (cursorCell == NoCell)
            {
                cursorCell = NearestSlotToOwner();
                RefreshHighlights();
                return;
            }

            if (cursorCell == ConfirmCell)
            {
                // Подтверждение остаётся за кнопкой: у неё вся проверка окна
                // и единственное событие, на которое подписан контроллер.
                // Полка только доносит до неё нажатие.
                button?.Interact(player);
                return;
            }

            if (markedSlot == NoCell)
            {
                markedSlot = cursorCell;
                RefreshHighlights();
                return;
            }

            if (markedSlot == cursorCell)
            {
                markedSlot = NoCell;
                RefreshHighlights();
                return;
            }

            Swap(markedSlot, cursorCell);
            markedSlot = NoCell;
            RefreshHighlights();
            RaiseChanged();
        }

        /// <summary>
        /// Курсор от мыши. Идёт в <c>Update</c>, а не в обработчике ввода:
        /// <c>LookDelta</c> копится ридером за кадр, и читать его надо там же,
        /// где живёт остальной визуал.
        /// </summary>
        private void Update()
        {
            if (!active || reader == null || !reader.LocallyControlled || slots.Count == 0)
            {
                return;
            }

            if (cursorCell == NoCell)
            {
                cursorCell = NearestSlotToOwner();
                RefreshHighlights();
            }

            cursorAccum += reader.LookDelta.x;

            float step = Mathf.Max(1f, mouseStepPixels);
            while (Mathf.Abs(cursorAccum) >= step)
            {
                int direction = cursorAccum > 0f ? 1 : -1;
                cursorAccum -= direction * step;

                int next = Mathf.Clamp(cursorCell + direction, 0, LastCell);
                if (next == cursorCell)
                {
                    // Упёрлись в край ряда: гасим остаток, иначе поведённая
                    // в сторону мышь копит долг и курсор потом прыгает.
                    cursorAccum = 0f;
                    break;
                }

                cursorCell = next;
                RefreshHighlights();
            }
        }

        /// <summary>
        /// Текущая расстановка по слотам. Отказывает только если полки нет:
        /// неполной расстановки в этой схеме не существует.
        /// </summary>
        public bool TryGetArrangement(List<int> into)
        {
            if (into == null)
            {
                return false;
            }

            into.Clear();
            if (slots.Count == 0)
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

            // Отметку сбрасываем: она указывала на слот, а под ним теперь
            // другая банка, и обмен получился бы не тот, который выбирали.
            markedSlot = NoCell;
            RefreshHighlights();
            RaiseChanged();
        }

        /// <summary>
        /// Снять полку с игрока: курсор погашен, владельца больше нет.
        ///
        /// Зовётся в конце раунда, и это не косметика: персонаж переезжает
        /// между сценами живым, и незакрытое состояние уедет в хаб вместе
        /// с ним (спека 10.5). Банка при этом никуда не цепляется — в этой
        /// схеме она физически не может оказаться вне слота.
        /// </summary>
        public void Release()
        {
            Active = false;
            owner = null;
            reader = null;
        }

        /// <summary>Поменять местами содержимое двух слотов.</summary>
        private void Swap(int a, int b)
        {
            Can first = occupants[a];
            Can second = occupants[b];

            if (first != null)
            {
                PlaceInSlot(first, b);
            }

            if (second != null)
            {
                PlaceInSlot(second, a);
            }
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
        /// Развесить подсветку заново. Одной точкой, а не правкой соседних
        /// банок при каждом сдвиге: состояний три, и любое рассогласование
        /// оставляет на полке две поднятые банки без причины.
        /// </summary>
        private void RefreshHighlights()
        {
            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] == null)
                {
                    continue;
                }

                Can.Highlight state = i == markedSlot
                    ? Can.Highlight.Marked
                    : i == cursorCell ? Can.Highlight.Cursor : Can.Highlight.None;

                occupants[i].SetHighlight(state);
            }

            button?.SetCursorHighlight(active && cursorCell == ConfirmCell);
        }

        /// <summary>
        /// Слот, ближайший к точке перед игроком. Нужен ровно один раз —
        /// когда открывается окно и курсор надо куда-то поставить. Дальше
        /// курсор живёт от мыши и от положения тела не зависит.
        /// </summary>
        private int NearestSlotToOwner()
        {
            if (slots.Count == 0)
            {
                return NoCell;
            }

            if (owner == null)
            {
                return 0;
            }

            Vector3 aim = owner.transform.position + owner.Facing * slotPickReach;
            int best = 0;
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
            cursorCell = NoCell;
            markedSlot = NoCell;
        }

        private void RaiseChanged() => ArrangementChanged?.Invoke(this);
    }
}
