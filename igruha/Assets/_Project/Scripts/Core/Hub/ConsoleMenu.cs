using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Hub
{
    /// <summary>
    /// Экран игровой приставки на телевизоре в хабе — единственный вход во все
    /// мини-игры.
    ///
    /// Работает как настоящая приставка в комнате: включает её хозяин, а
    /// смотрят все. Хост подходит к телевизору, жмёт кнопку — и телевизор
    /// зажигается сразу у всех, у каждого на своей машине. Дальше хост водит
    /// по списку, остальные видят, как ездит подсветка, и запускает игру тоже
    /// он.
    ///
    /// Почему все смотрят один экран, а не выбирают каждый у себя: выбор игры
    /// — это событие в комнате, а не настройка. Отдельный экран у каждого
    /// превратил бы его в голосование, которого в дизайне нет.
    ///
    /// Состояние меню общее и живёт на сервере (<see cref="IConsoleMenuRelay"/>).
    /// Локально не решает никто, даже хост: он тоже ждёт, пока решение
    /// вернётся с сервера. Так у всех, включая его самого, одна и та же
    /// картинка, и рассинхрону взяться неоткуда.
    /// </summary>
    public sealed class ConsoleMenu : MonoBehaviour
    {
        [Header("Данные")]
        [SerializeField] private MinigameCatalog catalog;
        [SerializeField] private MinigameLoader loader;

        [Header("Экран телевизора")]
        [Tooltip("Корень изображения на экране: гаснет, когда приставка выключена")]
        [SerializeField] private GameObject screenRoot;
        [Tooltip("Контейнер карточек. Первая карточка внутри — образец, остальные клонируются с неё")]
        [SerializeField] private RectTransform cardsParent;
        [Tooltip("Подпись выбранной игры под списком")]
        [SerializeField] private TMP_Text captionText;
        [Tooltip("Строка подсказки управления в самом низу экрана")]
        [SerializeField] private TMP_Text hintText;

        [Header("Камера")]
        [Tooltip("Камера всех игроков на время меню смотрит сюда — на телевизор")]
        [SerializeField] private MinigameCameraController cameraController;
        [SerializeField] private Transform tvCameraFocus;

        [Header("Вид карточки")]
        [SerializeField] private Color idleColor = new Color(0.16f, 0.17f, 0.22f, 0.95f);
        [SerializeField] private Color selectedColor = new Color(0.98f, 0.42f, 0.66f, 1f);
        [SerializeField] private Color lockedColor = new Color(0.12f, 0.12f, 0.14f, 0.75f);

        /// <summary>Куда уходят решения хоста в сетевой катке. Пусто — сети нет.</summary>
        public static IConsoleMenuRelay Relay { get; set; }

        /// <summary>
        /// Экран, живущий в текущей сцене. Сетевой слой держится дольше сцены
        /// и находит через это поле, кому применять общее состояние.
        /// </summary>
        public static ConsoleMenu Active { get; private set; }

        /// <summary>Приставка включена — её экран виден всем.</summary>
        public bool IsOpen { get; private set; }

        private readonly List<CardView> cards = new List<CardView>(16);
        private int cursor;
        private bool openedThisFrame;
        private CameraMode restoreMode;
        private Transform restoreTarget;

        /// <summary>Ввод отобран нами. Держим отдельно, чтобы вернуть ровно то, что забрали.</summary>
        private bool controlSuppressed;

        private void Awake()
        {
            Active = this;
            BuildCards();

            if (screenRoot != null)
            {
                screenRoot.SetActive(false);
            }

            // Выбор мог идти ещё до того, как эта сцена собралась.
            Relay?.SyncScreen();
        }

        private void OnDestroy()
        {
            // Сцена меняется вместе с запуском игры, а аватар её переживает:
            // он сетевой и живёт дольше хаба. Не вернув ему ввод здесь, мы
            // высадили бы игрока в мини-игру обездвиженным — и починить это
            // там было бы уже некому.
            if (controlSuppressed)
            {
                SuppressControl(false);
            }

            if (Active == this)
            {
                Active = null;
            }
        }

        /// <summary>
        /// Карточки строятся один раз по каталогу. Образец — первая карточка,
        /// положенная в сцену руками: так вид правится в редакторе, а не
        /// числами в коде.
        /// </summary>
        private void BuildCards()
        {
            if (cardsParent == null || cardsParent.childCount == 0)
            {
                Debug.LogError($"{name}: ConsoleMenu без образца карточки — экран будет пустым", this);
                return;
            }

            GameObject sample = cardsParent.GetChild(0).gameObject;
            int count = catalog != null ? catalog.Games.Count : 0;

            for (int i = 0; i < count; i++)
            {
                GameObject go = i == 0 ? sample : Instantiate(sample, cardsParent);
                go.name = $"Card_{i:00}";
                var card = new CardView(go);
                card.SetTitle(TitleOf(i));
                cards.Add(card);
            }

            // Образец лишний, если каталог пуст: иначе на экране висела бы
            // одна карточка-призрак без игры за ней.
            if (count == 0)
            {
                sample.SetActive(false);
            }
        }

        private string TitleOf(int index)
        {
            MinigameDefinition game = catalog.Get(index);
            if (game == null)
            {
                return "—";
            }

            return catalog.IsPlayable(index)
                ? game.DisplayName
                : game.DisplayName + "\n<size=60%>в работе</size>";
        }

        // ---------- намерения хоста ----------

        /// <summary>
        /// Хост включил приставку у телевизора. Сам экран здесь не зажигается:
        /// решение уходит на сервер и возвращается ко всем разом, включая
        /// самого хоста.
        /// </summary>
        public void RequestOpen()
        {
            if (IsOpen || !HasAuthority)
            {
                return;
            }

            int start = catalog != null ? catalog.NextPlayable(-1, 1) : -1;
            cursor = start < 0 ? 0 : start;

            if (Relay != null)
            {
                Relay.SetCursor(cursor);
                Relay.SetMenuOpen(true);
                return;
            }

            ApplyCursor(cursor);
            ApplyOpen(true);
        }

        /// <summary>Эта машина решает: одиночная сцена или хост сетевой катки.</summary>
        private static bool HasAuthority => Relay == null || Relay.HasAuthority;

        private void Update()
        {
            // Заморозку держим каждый кадр, а не только в момент включения.
            // Персонаж мог ещё не появиться, когда экран зажёгся: у клиента,
            // догружающего хаб, аватар приезжает позже сетевого состояния, и
            // единственная попытка заморозить прошла бы вхолостую.
            if (IsOpen)
            {
                SuppressControl(true);
            }
            else if (controlSuppressed)
            {
                SuppressControl(false);
            }

            if (!IsOpen || !HasAuthority)
            {
                return;
            }

            // Кадр включения пропускаем: то же нажатие не должно сразу
            // выбрать игру, на которой в этот момент оказалась подсветка.
            if (openedThisFrame)
            {
                openedThisFrame = false;
                return;
            }

            int step = ReadStep();
            if (step != 0)
            {
                MoveCursor(step);
                return;
            }

            if (WasConfirmed())
            {
                Launch();
                return;
            }

            if (WasCancelled())
            {
                RequestOpenState(false);
            }
        }

        private static int ReadStep()
        {
            Keyboard keyboard = Keyboard.current;
            int step = 0;

            if (keyboard != null)
            {
                if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
                {
                    step++;
                }

                if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
                {
                    step--;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.dpad.right.wasPressedThisFrame)
                {
                    step++;
                }

                if (gamepad.dpad.left.wasPressedThisFrame)
                {
                    step--;
                }
            }

            return step;
        }

        private static bool WasConfirmed()
        {
            Keyboard keyboard = Keyboard.current;
            bool confirmed = keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);

            Gamepad gamepad = Gamepad.current;
            return confirmed || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
        }

        private static bool WasCancelled()
        {
            Keyboard keyboard = Keyboard.current;
            bool cancelled = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;

            Gamepad gamepad = Gamepad.current;
            return cancelled || (gamepad != null && gamepad.buttonEast.wasPressedThisFrame);
        }

        private void MoveCursor(int step)
        {
            if (catalog == null)
            {
                return;
            }

            // Ходим только по готовым играм: упереться подсветкой в игру,
            // которую нельзя запустить, — тупик без объяснения.
            int next = catalog.NextPlayable(cursor, step);
            if (next < 0 || next == cursor)
            {
                return;
            }

            if (Relay != null)
            {
                Relay.SetCursor(next);
                return;
            }

            ApplyCursor(next);
        }

        private void RequestOpenState(bool open)
        {
            if (Relay != null)
            {
                Relay.SetMenuOpen(open);
                return;
            }

            ApplyOpen(open);
        }

        private void Launch()
        {
            if (catalog == null || !catalog.IsPlayable(cursor))
            {
                return;
            }

            MinigameDefinition game = catalog.Get(cursor);
            Debug.Log($"{name}: приставка запускает «{game.DisplayName}»");

            // Экран гасим у всех до загрузки: сцена сменится, но управление и
            // камеру надо вернуть людям здесь, пока этот объект ещё жив.
            RequestOpenState(false);
            loader?.Load(game);
        }

        // ---------- применение общего состояния ----------

        /// <summary>
        /// Включить или выключить экран у себя. Зовёт сетевой слой у всех
        /// разом — и у клиентов, и у хоста.
        /// </summary>
        public void ApplyOpen(bool open)
        {
            if (IsOpen == open)
            {
                return;
            }

            IsOpen = open;
            openedThisFrame = open;

            if (screenRoot != null)
            {
                screenRoot.SetActive(open);
            }

            if (hintText != null)
            {
                hintText.text = HasAuthority
                    ? "Стрелки — выбор     Enter — играть     Esc — выключить"
                    : "выбирает хост";
            }

            ApplyCamera(open);
            SuppressControl(open);
            Refresh();
        }

        /// <summary>Подсветить игру под этим номером. Зовёт сетевой слой у всех разом.</summary>
        public void ApplyCursor(int index)
        {
            cursor = index;
            Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                bool playable = catalog != null && catalog.IsPlayable(i);
                Color color = !playable ? lockedColor : (i == cursor ? selectedColor : idleColor);
                cards[i].SetColor(color);
            }

            if (captionText != null)
            {
                MinigameDefinition game = catalog != null ? catalog.Get(cursor) : null;
                captionText.text = game != null ? game.DisplayName : string.Empty;
            }
        }

        /// <summary>
        /// Пока идёт выбор, камера каждого смотрит на телевизор, а не на
        /// своего персонажа: экран показывают всем, значит и кадр у всех один.
        /// </summary>
        private void ApplyCamera(bool toTv)
        {
            if (cameraController == null || tvCameraFocus == null)
            {
                return;
            }

            if (toTv)
            {
                restoreMode = cameraController.CurrentMode;
                restoreTarget = cameraController.CurrentTarget;
                cameraController.Apply(CameraMode.Fixed, tvCameraFocus);
                return;
            }

            if (restoreTarget != null)
            {
                cameraController.Apply(restoreMode, restoreTarget);
                return;
            }

            // Возвращать не к чему: экран зажёгся раньше, чем камера этой
            // машины успела найти своего героя. Без страховки игрок остался бы
            // смотреть в выключенный телевизор.
            Transform own = SessionScoreboard.Current?.LocalPlayer?.Avatar != null
                ? SessionScoreboard.Current.LocalPlayer.Avatar.transform
                : null;

            if (own != null)
            {
                cameraController.Apply(CameraMode.ThirdPerson, own);
            }
        }

        /// <summary>
        /// Ввод у всех замирает на время меню — иначе половина комнаты убежит
        /// от телевизора и выбор увидят не все. Ридер, у которого управление
        /// отобрано навсегда (чужая сетевая копия), не будим.
        ///
        /// Флаг ведём отдельно от <see cref="IsOpen"/>: вернуть ввод надо
        /// ровно тому, у кого мы его забрали, и ровно один раз — даже если к
        /// этому моменту экран уже погас другим путём.
        /// </summary>
        private void SuppressControl(bool suppress)
        {
            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            if (local?.Avatar == null || !local.Avatar.TryGetComponent(out PlayerInputReader reader))
            {
                // Персонажа ещё нет. Отпускать нечего, а запрет останется
                // висеть и доедет до аватара, когда тот появится.
                controlSuppressed = suppress && controlSuppressed;
                return;
            }

            if (!reader.LocallyControlled)
            {
                return;
            }

            reader.enabled = !suppress;
            controlSuppressed = suppress;
        }

        /// <summary>
        /// Одна карточка на экране: подложка и подпись. Держим ссылки, а не
        /// ищем компоненты каждый раз — подсветка ездит каждым нажатием.
        /// </summary>
        private readonly struct CardView
        {
            private readonly Image background;
            private readonly TMP_Text title;

            public CardView(GameObject go)
            {
                background = go.GetComponent<Image>();
                title = go.GetComponentInChildren<TMP_Text>();
            }

            public void SetTitle(string text)
            {
                if (title != null)
                {
                    title.text = text;
                }
            }

            public void SetColor(Color color)
            {
                if (background != null)
                {
                    background.color = color;
                }
            }
        }
    }
}
