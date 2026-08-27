using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
    /// <remarks>
    /// <b>Порядок выполнения важен и потому задан явно.</b> Полка забирает
    /// нажатие E у ридера сама, а <c>PlayerInteractor</c> гасит это же нажатие
    /// безусловно — даже когда ему нечего активировать (у него радиус 1.8 м,
    /// а доска стоит в 1.94 м). При обычном, то есть неопределённом, порядке
    /// апдейтов полка получала бы нажатие через раз: кто первым дошёл до флага,
    /// тот его и снял. Отрицательный порядок ставит полку заведомо раньше.
    /// Вне окна выставления она ввод не трогает вовсе, поэтому на остальные
    /// интерактивы в сцене это не влияет.
    /// </remarks>
    [DefaultExecutionOrder(-100)]
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
        [SerializeField] private float mouseStepPixels = 45f;
        [Tooltip("Пауза между шагами курсора, пока стрелка зажата, с")]
        [SerializeField] private float keyRepeatSeconds = 0.16f;
        [Tooltip("На сколько метров перед собой игрок «указывает». Задаёт только СТАРТОВЫЙ слот курсора, когда окно открывается")]
        [SerializeField] private float slotPickReach = 0.55f;

        /// <summary>Толщина диска-метки (масштаб цилиндра — реальная высота вдвое больше).</summary>
        private const float MarkerThickness = 0.004f;

        /// <summary>На сколько метка приподнята над доской, чтобы не тонуть в ней, м.</summary>
        private const float MarkerLift = 0.012f;

        /// <summary>Во сколько раз метка шире банки.</summary>
        private const float MarkerWidthFactor = 1.9f;

        private static readonly Color MarkerColor = new Color(1f, 0.92f, 0.35f);

        /// <summary>URP красится через _BaseColor, встроенный шейдер — через _Color. Ставим оба, как и банке.</summary>
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

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
        private float keyHoldTimer;
        private Transform cursorMarker;

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

            cursorMarker = BuildCursorMarker();

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

            Apply();
        }

        /// <summary>
        /// Нажатие по ячейке под курсором: отметить, поменять местами, снять
        /// отметку или подтвердить. Отдельно от <see cref="Interact"/>, потому
        /// что в окне выставления полка берёт E сама, а не через
        /// <c>PlayerInteractor</c> — см. <see cref="TakeInteractPress"/>.
        /// </summary>
        private void Apply()
        {
            // Замок стоит здесь, а не только у входов: полка замирает в момент
            // подтверждения, и «замерла» должно значить именно это, чей бы
            // вызов ни пришёл.
            if (!active)
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
                button?.Interact(owner);
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
        /// Курсор от мыши и от стрелок. Идёт в <c>Update</c>, а не в обработчике
        /// ввода: <c>LookDelta</c> копится ридером за кадр, и читать его надо
        /// там же, где живёт остальной визуал.
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

            HoldMouse();
            SwallowPush();
            TakeClick();
            TakeInteractPress();
            TakeConfirmKey();

            if (StepFromArrows())
            {
                return;
            }

            cursorAccum += ReadMouseStep();

            float step = Mathf.Max(1f, mouseStepPixels);
            while (Mathf.Abs(cursorAccum) >= step)
            {
                int direction = cursorAccum > 0f ? 1 : -1;
                cursorAccum -= direction * step;

                if (!MoveCursor(direction))
                {
                    // Упёрлись в край ряда: гасим остаток, иначе поведённая
                    // в сторону мышь копит долг и курсор потом прыгает.
                    cursorAccum = 0f;
                    break;
                }
            }
        }

        /// <summary>
        /// Клик левой кнопкой — то же, что E: отметить, поменять местами,
        /// снять отметку, подтвердить на ячейке кнопки.
        ///
        /// Мышь в этой стадии и так ведёт подсветку, и рука уже на ней —
        /// тянуться к E ради каждого обмена неудобно (плейтест 22.08).
        /// E остаётся: он ничему не мешает и нужен геймпаду.
        ///
        /// Читаем кнопку с устройства, а не действие: в раскладке
        /// <c>&lt;Mouse&gt;/leftButton</c> и <c>&lt;Keyboard&gt;/enter</c> сидят
        /// на одном действии <c>Attack</c> (толчок), а нам эти две кнопки нужны
        /// разными — клик выбирает банку, Enter подтверждает. Раскладка
        /// заморожена, разводим на уровне устройств.
        /// </summary>
        private void TakeClick()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            Apply();
        }

        /// <summary>
        /// Съесть накопленный толчок, пока открыто окно.
        ///
        /// ЛКМ и Enter — это же действие <c>Attack</c>, то есть импульс-толчок.
        /// В клетке толкать некого, но нажатие копится в ридере и выстрелило бы
        /// анимацией на ровном месте, а после окна — ещё и в чужую спину.
        /// Гасим здесь, а не в способности: способность общая на все мини-игры.
        /// </summary>
        private void SwallowPush()
        {
            if (reader.PushPressed)
            {
                reader.ConsumePush();
            }
        }

        /// <summary>
        /// Смещение мыши за кадр — прямо с устройства, а не через
        /// <c>PlayerInputReader.LookDelta</c>.
        ///
        /// Причина замерена 22.08: в окне выставления действие <c>Look</c>
        /// стоит <b>выключенным</b> (<c>enabled=False</c>), и ридер честно
        /// отдаёт нули. Гасит его сама камера: <c>ThirdPersonCameraRig.OnDisable</c>
        /// зовёт <c>lookAction.action.Disable()</c> в обход счётчика ссылок
        /// <c>PlayerInputReader</c>, а риг в этой стадии выключается ради
        /// фикс-камеры на полке. То есть подсветку нечем было вести вообще:
        /// курсор стоял на месте, что бы игрок ни делал мышью.
        ///
        /// Чинить это в риге нельзя — камера заморожена (igruha/CLAUDE.md,
        /// раздел 0), а <c>PlayerInputReader</c> — общий Core. Мышь здесь
        /// читается так же, как стрелки: напрямую с устройства, без действий
        /// и без правок в общем коде.
        /// </summary>
        private static float ReadMouseStep()
        {
            Mouse mouse = Mouse.current;
            return mouse != null ? mouse.delta.ReadValue().x : 0f;
        }

        /// <summary>
        /// Подтверждение с клавиши, не доводя курсор до конца ряда.
        ///
        /// На плейтесте 22.08: «пока наводишься на кнопку — теряешь время
        /// и не успеваешь». Дорога до ячейки кнопки стоит секунд из тех же,
        /// в которые надо ещё и думать.
        ///
        /// Клавишу читаем с устройства: в раскладке Enter сидит на действии
        /// <c>Attack</c> вместе с ЛКМ, а нам они нужны разными — клик выбирает
        /// банку, Enter подтверждает. Сам толчок в окне глушится
        /// (см. <see cref="SwallowPush"/>). Ячейка кнопки в конце ряда
        /// остаётся: она нужна геймпаду.
        /// </summary>
        private void TakeConfirmKey()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || button == null)
            {
                return;
            }

            if (!keyboard.enterKey.wasPressedThisFrame && !keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                return;
            }

            button.Interact(owner);
        }

        /// <summary>
        /// Забрать нажатие E у ридера напрямую, не спрашивая
        /// <c>PlayerInteractor</c>.
        ///
        /// Иначе полка недостижима с того самого места, куда игра ставит
        /// игрока: доска стоит в 1.94 м от него, а радиус поиска интерактивов
        /// — 1.8 м. То есть на старте круга E не делал ровно ничего, и это
        /// вылезло на плейтесте 22.08 вместе с застрявшим курсором.
        ///
        /// Подгонять радиус или спавн нельзя: радиус общий на все мини-игры,
        /// а спавн держит дистанцию, на которой камера не встаёт в затылок.
        /// Да и по смыслу дистанция здесь лишняя — курсор уже выбрал ячейку,
        /// ходить ногами в окне выставления не нужно вовсе (спека 4).
        ///
        /// Двойного срабатывания не будет: нажатие снимается
        /// <c>ConsumeInteract</c>, и если игрок стоит вплотную и первым успел
        /// <c>PlayerInteractor</c>, сюда придёт уже пустой флаг — и наоборот.
        /// </summary>
        private void TakeInteractPress()
        {
            if (!reader.InteractPressed)
            {
                return;
            }

            reader.ConsumeInteract();
            Apply();
        }

        /// <summary>
        /// Держать мышь захваченной, пока открыто окно выставления.
        ///
        /// Без этого схема не работает вообще, и это стоило целого плейтеста
        /// 22.08. Полка водит подсветку дельтой мыши, а камера в окне уходит
        /// на фикс-риг: <c>MinigameCameraController</c> при переключении гасит
        /// GameObject орбитального рига, а тот в <c>OnDisable</c> отпускает
        /// курсор. Указатель становится обычным системным, упирается в край
        /// экрана, дельта перестаёт приходить — и подсветка встаёт колом.
        /// Наружу это выглядит как «первое E поднимает банку, а дальше ничего»:
        /// второе нажатие бьёт по той же банке и просто снимает отметку.
        ///
        /// Ставим каждый кадр, а не один раз на открытии: риги включаются
        /// и выключаются по стадиям, и порядок «кто последний тронул курсор»
        /// не гарантирован. Замороженную камеру при этом не трогаем — полка
        /// держит курсор ровно те секунды, что открыто её окно.
        /// </summary>
        private static void HoldMouse()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>
        /// Стрелки влево-вправо как второй способ вести курсор. Клавиатуру
        /// читаем напрямую, а не через действие: раскладка заморожена, новых
        /// биндов в неё не добавляем, а стрелки в игре ничем не заняты.
        /// WASD взять нельзя — они ходят ногами, а блокировать ходьбу ради
        /// полки значит убить колесо насмешек на Tab (STATE, 2c).
        /// </summary>
        private bool StepFromArrows()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            bool left = keyboard.leftArrowKey.isPressed;
            bool right = keyboard.rightArrowKey.isPressed;
            if (left == right)
            {
                keyHoldTimer = 0f;
                return false;
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
            {
                keyHoldTimer = 0f;
            }

            if (keyHoldTimer > 0f)
            {
                keyHoldTimer -= Time.deltaTime;
                return true;
            }

            keyHoldTimer = Mathf.Max(0.05f, keyRepeatSeconds);
            MoveCursor(right ? 1 : -1);
            return true;
        }

        /// <summary>Сдвинуть курсор на соседнюю ячейку. False — упёрлись в край ряда.</summary>
        private bool MoveCursor(int direction)
        {
            int next = Mathf.Clamp(cursorCell + direction, 0, LastCell);
            if (next == cursorCell)
            {
                return false;
            }

            cursorCell = next;
            RefreshHighlights();
            return true;
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
            MoveCursorMarker();
        }

        /// <summary>
        /// Переставить метку под ячейку курсора.
        ///
        /// Одного подъёма банки мало: на плейтесте 22.08 это читалось как
        /// «поднялась банка и всё» — приподнятая банка не говорит, что именно
        /// её сейчас тронет E, особенно когда рядом стоит отмеченная. Плоский
        /// диск под ячейкой однозначен, работает и на ячейке кнопки, и переживёт
        /// замену серых заготовок на модели в арт-фазе.
        /// </summary>
        private void MoveCursorMarker()
        {
            if (cursorMarker == null)
            {
                return;
            }

            if (!active || cursorCell == NoCell)
            {
                cursorMarker.gameObject.SetActive(false);
                return;
            }

            if (cursorCell == ConfirmCell)
            {
                if (button == null)
                {
                    cursorMarker.gameObject.SetActive(false);
                    return;
                }

                cursorMarker.position = button.transform.position + Vector3.up * MarkerLift;
            }
            else
            {
                Vector3 slotLocal = slots[cursorCell].localPosition;
                cursorMarker.localPosition = new Vector3(slotLocal.x, MarkerLift, slotLocal.z);
            }

            cursorMarker.gameObject.SetActive(true);
        }

        /// <summary>
        /// Материал под всё, что полка лепит из примитивов в рантайме.
        ///
        /// <b>Без него билд рисует банки фиолетовыми.</b> <c>CreatePrimitive</c>
        /// вешает встроенный <c>Default-Material</c>, а его шейдер — не из URP:
        /// в редакторе он есть всегда, в сборку не попадает, и на его месте
        /// оказывается заглушка «шейдер потерян». Арена и клетки этим не болеют —
        /// они собраны из настоящих ассетов, и их шейдеры уезжают в билд вместе
        /// с материалами.
        ///
        /// Один общий экземпляр на все полки: цвет всё равно задаётся
        /// через <see cref="MaterialPropertyBlock"/>, копии материала
        /// множили бы вызовы отрисовки на ровном месте.
        /// </summary>
        private static Material blockoutMaterial;

        private static Material BlockoutMaterial
        {
            get
            {
                if (blockoutMaterial != null)
                {
                    return blockoutMaterial;
                }

                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogError("CanShelf: шейдер 'Universal Render Pipeline/Lit' не найден — " +
                                   "банки и метка курсора будут нарисованы заглушкой");
                    return null;
                }

                blockoutMaterial = new Material(shader) { name = "CansOrder Blockout (runtime)" };
                return blockoutMaterial;
            }
        }

        /// <summary>
        /// Диск-метка под ячейкой курсора. Строится вместе с рядом: число банок
        /// берётся из конфига и от круга к кругу может быть разным.
        /// </summary>
        private Transform BuildCursorMarker()
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "CursorMarker";
            marker.transform.SetParent(slotsRoot, false);
            marker.transform.localScale =
                new Vector3(canDiameter * MarkerWidthFactor, MarkerThickness, canDiameter * MarkerWidthFactor);

            // Коллайдер гасим и сносим по той же причине, что и у банки: выборка
            // PlayerInteractor держит 16 коллайдеров, и лишний вытеснил бы кнопку.
            Collider markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                markerCollider.enabled = false;
                Destroy(markerCollider);
            }

            if (marker.TryGetComponent(out Renderer markerRenderer))
            {
                markerRenderer.sharedMaterial = BlockoutMaterial;
                var block = new MaterialPropertyBlock();
                block.SetColor(BaseColorId, MarkerColor);
                block.SetColor(LegacyColorId, MarkerColor);
                markerRenderer.SetPropertyBlock(block);
            }

            marker.SetActive(false);
            return marker.transform;
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

            if (visual.TryGetComponent(out Renderer visualRenderer))
            {
                visualRenderer.sharedMaterial = BlockoutMaterial;
            }

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

            if (cursorMarker != null)
            {
                Destroy(cursorMarker.gameObject);
                cursorMarker = null;
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
