using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Igruha.Core.Session;

namespace Igruha.Networking
{
    /// <summary>
    /// Экран входа в игру: создать комнату или подключиться к другу по адресу.
    ///
    /// <b>Зачем он есть.</b> До него роль и адрес задавались только аргументами
    /// запуска (<c>--client --host 100.98.180.3</c>). Для стенда это правильно,
    /// для живой катки — нет: друг получает архив, открывает игру двойным
    /// щелчком и никаких аргументов не передаёт. Без этого экрана любой билд
    /// поднимается хостом, и четыре человека сидят в четырёх пустых комнатах.
    ///
    /// <b>Почему IMGUI.</b> Ровно по той же причине, что и
    /// <see cref="BootStatusScreen"/>: сцена Boot пустая — камера, свет и
    /// NetworkManager, — ни холста, ни шрифтов, ни единого ассета интерфейса в
    /// ней нет, а экран обязан работать раньше всего остального.
    ///
    /// <b>Кого он не трогает.</b> Стенд автопрогона и Multiplayer Play Mode
    /// проходят мимо: если роль задана аргументом запуска или дело происходит в
    /// редакторе, экран не показывается вовсе и всё работает как прежде.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkConnectScreen : MonoBehaviour
    {
        private const string AddressKey = "igruha.net.lastHost";
        private const string PortKey = "igruha.net.lastPort";

        /// <summary>Где лежит имя, введённое на входе. Его забирает <see cref="CharacterSelectionManager"/> при спавне.</summary>
        internal const string NameKey = "igruha.player.name";

        /// <summary>Диапазон, который Tailscale раздаёт своим машинам (100.64.0.0/10).</summary>
        private const byte TailscaleFirstOctet = 100;
        private const byte TailscaleSecondLow = 64;
        private const byte TailscaleSecondHigh = 127;

        private static readonly Color BackgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);
        private static readonly Color TitleColor = new Color(0.93f, 0.94f, 0.96f, 1f);
        private static readonly Color HintColor = new Color(0.68f, 0.71f, 0.76f, 1f);
        private static readonly Color AccentColor = new Color(0.98f, 0.42f, 0.66f, 1f);

        /// <summary>Выбор сделан: роль, адрес и порт. Адрес у хоста не используется.</summary>
        public event Action<NetworkStartRole, string, ushort> Chosen;

        /// <summary>Поля экрана по порядку обхода табуляцией.</summary>
        private enum Field { Name = 0, Address = 1, Port = 2 }

        private static readonly Field[] Order = { Field.Name, Field.Address, Field.Port };

        private string address = "";
        private string port = "7777";
        private string playerName = "";
        private string error = "";
        private bool visible = true;
        private string ownAddresses = "";

        private Field focus = Field.Name;
        private float caretPhase;

        private GUIStyle titleStyle;
        private GUIStyle hintStyle;
        private GUIStyle fieldStyle;
        private GUIStyle fieldTextStyle;
        private GUIStyle buttonStyle;
        private GUIStyle labelStyle;

        /// <summary>
        /// Спрашивать ли человека.
        ///
        /// Нет — в редакторе (там роль решает Multiplayer Play Mode), у
        /// инстанса без картинки и у любого запуска с нашими аргументами:
        /// стенд задаёт очередь игр и число участников, а хост стенда при этом
        /// не передаёт ни <c>--client</c>, ни <c>--host</c> — по одним только
        /// сетевым аргументам его не отличить от живого запуска, и экран
        /// перехватывал его, оставляя стенд ждать нажатия, которого некому
        /// сделать.
        /// </summary>
        public static bool ShouldAsk()
        {
            if (Application.isBatchMode || Application.isEditor) return false;
            return !LaunchArguments.HasOwnArguments();
        }

        private void Awake()
        {
            address = PlayerPrefs.GetString(AddressKey, "");
            port = PlayerPrefs.GetString(PortKey, "7777");
            playerName = PlayerPrefs.GetString(NameKey, "");
            ownAddresses = DescribeOwnAddresses();
        }

        /// <summary>Убрать экран: выбор сделан, дальше говорит <see cref="BootStatusScreen"/>.</summary>
        public void Hide() => visible = false;

        /// <summary>
        /// Буквы приходят из Input System, а не из IMGUI.
        ///
        /// В проекте включён только новый ввод (<c>activeInputHandler: 1</c>), и
        /// в этом режиме <c>GUI.TextField</c> клавиатуру не получает вовсе:
        /// поле рисуется, курсор мигает, а набрать в нём нельзя ничего.
        /// Переключать проект на «оба ввода» ради одного экрана — глобальная
        /// правка настроек перед живой каткой, поэтому поля здесь свои:
        /// рисуются как поля, а буквы берут из <see cref="Keyboard.onTextInput"/>.
        /// </summary>
        /// <summary>На чью клавиатуру подписаны. Устройство появляется не сразу и может смениться.</summary>
        private Keyboard subscribed;

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == subscribed) return;

            Unsubscribe();
            if (keyboard == null) return;

            keyboard.onTextInput += OnTextInput;
            subscribed = keyboard;
        }

        private void Unsubscribe()
        {
            if (subscribed == null) return;

            subscribed.onTextInput -= OnTextInput;
            subscribed = null;
        }

        private void OnTextInput(char symbol)
        {
            if (!visible) return;

            // Управляющие символы приходят сюда же. Возврат каретки и табуляция
            // разобраны как команды в Update, а здесь их надо отбросить, иначе
            // они попадут в имя буквами.
            if (char.IsControl(symbol)) return;

            switch (focus)
            {
                case Field.Name when playerName.Length < 16:
                    playerName += symbol;
                    break;
                case Field.Address when address.Length < 64:
                    address += symbol;
                    break;
                case Field.Port when port.Length < 5 && char.IsDigit(symbol):
                    port += symbol;
                    break;
            }
        }

        private void Update()
        {
            if (!visible) return;

            caretPhase += Time.unscaledDeltaTime;

            // Клавиатура на старте игры может быть ещё не найдена, а при
            // переподключении — смениться на другое устройство. Проверяем
            // каждый кадр: цена — сравнение ссылок.
            Subscribe();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.escapeKey.wasPressedThisFrame) Application.Quit();

            if (keyboard.backspaceKey.wasPressedThisFrame) Backspace();

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                bool back = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                int next = (System.Array.IndexOf(Order, focus) + (back ? Order.Length - 1 : 1)) % Order.Length;
                focus = Order[next];
            }

            // Enter подключает, если адрес введён, и создаёт комнату, если поле
            // пустое: у хоста вводить нечего, и лишнего щелчка мышью он не делает.
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                if (string.IsNullOrWhiteSpace(address)) Choose(NetworkStartRole.Host);
                else Choose(NetworkStartRole.Client);
            }
        }

        private void Backspace()
        {
            switch (focus)
            {
                case Field.Name when playerName.Length > 0:
                    playerName = playerName[..^1];
                    break;
                case Field.Address when address.Length > 0:
                    address = address[..^1];
                    break;
                case Field.Port when port.Length > 0:
                    port = port[..^1];
                    break;
            }
        }

        /// <summary>
        /// Поле ввода: рамка, текст и мигающая палочка у активного.
        /// Щелчок по рамке делает поле активным.
        /// </summary>
        private void DrawField(Rect rect, Field field, string value)
        {
            bool active = focus == field;

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                focus = field;
                caretPhase = 0f;
                Event.current.Use();
            }

            GUI.Box(rect, GUIContent.none, fieldStyle);

            bool caretOn = active && Mathf.Repeat(caretPhase, 1f) < 0.55f;
            string shown = value + (caretOn ? "|" : "");

            fieldTextStyle.normal.textColor = active ? TitleColor : HintColor;
            GUI.Label(new Rect(rect.x + rect.height * 0.35f, rect.y, rect.width, rect.height), shown, fieldTextStyle);
        }

        private void Choose(NetworkStartRole role)
        {
            if (!ushort.TryParse(port, out ushort parsedPort) || parsedPort == 0)
            {
                error = "Порт — число от 1 до 65535. По умолчанию 7777.";
                return;
            }

            string target = address == null ? "" : address.Trim();
            if (role == NetworkStartRole.Client && !IsUsableAddress(target))
            {
                error = "Адрес хоста не похож на адрес. Спроси его у того, кто создал комнату.";
                return;
            }

            PlayerPrefs.SetString(AddressKey, target);
            PlayerPrefs.SetString(PortKey, parsedPort.ToString());
            PlayerPrefs.SetString(NameKey, (playerName ?? "").Trim());
            PlayerPrefs.Save();

            error = "";
            visible = false;

            // Строка нужна на живой катке: по ней видно, кто под каким именем
            // вошёл и куда стучался, когда кто-то «не подключается».
            Debug.Log(role == NetworkStartRole.Host
                ? $"🎮 Экран входа: создаю комнату на порту {parsedPort}, имя «{playerName}»"
                : $"🎮 Экран входа: подключаюсь к {target}:{parsedPort}, имя «{playerName}»");

            Chosen?.Invoke(role, target, parsedPort);
        }

        /// <summary>
        /// Годится ли строка как адрес. Проверка нарочно мягкая: IP, имя машины
        /// Tailscale и обычное доменное имя одинаково законны, а отличить опечатку
        /// от рабочего имени можно только попыткой подключения.
        /// </summary>
        private static bool IsUsableAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (value.Contains(" ")) return false;
            return IPAddress.TryParse(value, out _) || value.Contains(".");
        }

        private void OnGUI()
        {
            if (!visible) return;

            EnsureStyles();

            Color previous = GUI.color;
            GUI.color = BackgroundColor;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;

            float margin = Screen.width * 0.08f;
            float width = Mathf.Min(Screen.width - margin * 2f, Screen.height * 1.1f);
            float left = (Screen.width - width) * 0.5f;
            float row = Mathf.Max(34f, Screen.height * 0.062f);
            float gap = row * 0.32f;
            float y = Screen.height * 0.14f;

            GUI.Label(new Rect(left, y, width, row * 1.6f), "КОМНАТА", titleStyle);
            y += row * 1.7f;

            GUI.Label(new Rect(left, y, width, row), "Как тебя зовут", labelStyle);
            y += row * 0.8f;
            DrawField(new Rect(left, y, width, row), Field.Name, playerName);
            y += row + gap;

            GUI.Label(new Rect(left, y, width, row), "Адрес друга, который создал комнату", labelStyle);
            y += row * 0.8f;

            float portWidth = width * 0.24f;
            DrawField(new Rect(left, y, width - portWidth - gap, row), Field.Address, address);
            DrawField(new Rect(left + width - portWidth, y, portWidth, row), Field.Port, port);
            y += row + gap * 1.4f;

            float buttonWidth = (width - gap) * 0.5f;
            if (GUI.Button(new Rect(left, y, buttonWidth, row * 1.25f), "ПОДКЛЮЧИТЬСЯ", buttonStyle))
            {
                Choose(NetworkStartRole.Client);
            }

            if (GUI.Button(new Rect(left + buttonWidth + gap, y, buttonWidth, row * 1.25f), "СОЗДАТЬ КОМНАТУ", buttonStyle))
            {
                Choose(NetworkStartRole.Host);
            }

            y += row * 1.25f + gap;

            if (!string.IsNullOrEmpty(error))
            {
                Color errorPrevious = hintStyle.normal.textColor;
                hintStyle.normal.textColor = AccentColor;
                GUI.Label(new Rect(left, y, width, row * 2f), error, hintStyle);
                hintStyle.normal.textColor = errorPrevious;
                y += row * 1.4f;
            }

            GUI.Label(
                new Rect(left, y, width, Screen.height * 0.3f),
                "Комнату создаёт кто-то один — остальные вводят его адрес.\n"
                + "Enter — подключиться, с пустым адресом — создать комнату.\n"
                + "Tab — следующее поле, Esc — выйти.\n\n"
                + ownAddresses,
                hintStyle);
        }

        /// <summary>
        /// Свои адреса — их хозяин комнаты диктует друзьям.
        ///
        /// Адрес Tailscale показывается первым и назван прямо: играем через него,
        /// и именно он работает из чужой квартиры, а домашний 192.168.x.x —
        /// только внутри одной квартиры.
        /// </summary>
        private static string DescribeOwnAddresses()
        {
            var tailscale = new List<string>();
            var local = new List<string>();

            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (UnicastIPAddressInformation info in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                        string value = info.Address.ToString();
                        if (IsTailscale(info.Address)) tailscale.Add(value);
                        else local.Add(value);
                    }
                }
            }
            catch (Exception e)
            {
                return $"Свой адрес определить не вышло: {e.Message}";
            }

            var text = new StringBuilder("Твой адрес, если комнату создаёшь ты:\n");
            if (tailscale.Count > 0) text.Append("  Tailscale: ").Append(string.Join(", ", tailscale)).Append('\n');
            if (local.Count > 0) text.Append("  Эта сеть: ").Append(string.Join(", ", local)).Append('\n');
            if (tailscale.Count == 0) text.Append("  Tailscale не подключён — друзья из других квартир не достучатся.\n");

            return text.ToString();
        }

        /// <summary>Адрес из диапазона Tailscale — 100.64.0.0/10.</summary>
        private static bool IsTailscale(IPAddress value)
        {
            byte[] bytes = value.GetAddressBytes();
            return bytes.Length == 4
                   && bytes[0] == TailscaleFirstOctet
                   && bytes[1] >= TailscaleSecondLow
                   && bytes[1] <= TailscaleSecondHigh;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;

            int titleSize = Mathf.Max(28, Mathf.RoundToInt(Screen.height * 0.055f));
            int bodySize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.024f));

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = titleSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = TitleColor },
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = bodySize,
                normal = { textColor = HintColor },
            };

            hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = bodySize,
                wordWrap = true,
                normal = { textColor = HintColor },
            };

            fieldStyle = new GUIStyle(GUI.skin.textField);

            fieldTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(bodySize * 1.25f),
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = TitleColor },
            };

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(bodySize * 1.2f),
                fontStyle = FontStyle.Bold,
            };
        }
    }
}
