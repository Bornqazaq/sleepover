using System;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>
    /// Красная кнопка на тумбе — единственное действие игрока во всей игре.
    /// Отсчёт идёт, пока кнопку **держат**: нажал и держишь — время пошло,
    /// отпустил — встало. Отпустив однажды, в этом подраунде уже не начнёшь
    /// заново.
    ///
    /// Держать, а не нажимать дважды, — потому что это физически та самая
    /// большая красная кнопка: её продавливают и не отпускают. Заодно исчезает
    /// кривой случай «а что если нажать третий раз».
    ///
    /// Вокруг этой кнопки собрана вся честность игры: свой результат нельзя
    /// увидеть в момент нажатия, а чужой — подсмотреть по соседней клетке.
    /// Поэтому подсветка чужой кнопки загорается на «старт» и держится
    /// до конца стадии отмера, гаснув у всех одновременно: видно, кто начал,
    /// но не видно, кто и когда остановился. Гаснущая на «стоп» подсветка
    /// была бы прямым читом на типе «Потолок» — достаточно остановиться
    /// на полсекунды раньше того, у кого уже погасло.
    ///
    /// Сама кнопка ничего не ранжирует: она отдаёт наверх замер интервала,
    /// а что он значит, решают правила игры.
    /// </summary>
    public sealed class CageButton : MonoBehaviour, IHoldInteractable
    {
        public enum ButtonState
        {
            /// <summary>Отсчёт не запускали.</summary>
            Idle,
            /// <summary>Кнопку держат, отсчёт идёт.</summary>
            Running,
            /// <summary>Кнопку отпустили, в этом подраунде она больше не работает.</summary>
            Stopped
        }

        [Tooltip("Лампа кнопки — ей меняется цвет")]
        [SerializeField] private Renderer lamp;
        [Tooltip("Цвет погашенной кнопки")]
        [SerializeField] private Color idleColor = new Color(0.35f, 0.05f, 0.05f);
        [Tooltip("Цвет своей кнопки, пока идёт отсчёт")]
        [SerializeField] private Color ownRunningColor = new Color(1f, 0.15f, 0.1f);
        [Tooltip("Цвет чужой кнопки, пока её хозяин отсчитывает")]
        [SerializeField] private Color neighbourRunningColor = new Color(0.9f, 0.45f, 0.1f);
        [Tooltip("Частота пульсации своей кнопки, Гц")]
        [SerializeField] private float pulseSpeed = 3f;
        [Tooltip("Глубина пульсации, 0..1")]
        [SerializeField] private float pulseAmount = 0.3f;
        [Tooltip("Лампа в клетке. Горит, пока идёт отсчёт: саму кнопку заслоняет персонаж, а свет виден боковым зрением")]
        [SerializeField] private Light beacon;
        [Tooltip("Яркость лампы на пике пульсации")]
        [SerializeField] private float beaconIntensity = 1.8f;

        /// <summary>Хозяин кнопки нажал «старт».</summary>
        public event Action<CageButton> Started;

        /// <summary>Хозяин нажал «стоп». Второй аргумент — отмеренный интервал, с.</summary>
        public event Action<CageButton, float> Stopped;

        /// <summary>
        /// Владелец нажал или отпустил, и решать исход должен не он. Аргументы —
        /// «держит», метка момента на общих часах и длительность удержания
        /// по монотонным часам этой машины (на нажатии — ноль). Поднимается
        /// только на машине хозяина кнопки в сетевой катке: у авторитета исход
        /// считается сразу.
        /// </summary>
        public event Action<CageButton, bool, double, float> HoldIntent;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock block;
        private PlayerController owner;
        private double startedAt;
        private float measured;

        /// <summary>
        /// Момент нажатия по монотонным часам этой машины. Ноль — не держат.
        /// </summary>
        private double heldSinceRealtime;

        /// <summary>Потолок замера: длина окна отмера, с. Ставит контроллер при открытии окна.</summary>
        private float measureCap = float.MaxValue;

        /// <summary>Открыта ли стадия отмера. Ставит контроллер игры.</summary>
        public bool WindowOpen { get; private set; }

        /// <summary>Показывать ли подсветку чужих кнопок. Флаг плейтеста neighbourButtonLights.</summary>
        public bool NeighbourLightsEnabled { get; set; } = true;

        /// <summary>Видит ли эту кнопку её хозяин. У чужих клеток — false.</summary>
        public bool ViewedByOwner { get; set; }

        /// <summary>
        /// Горит ли чужая кнопка по данным сервера. Своя зажигается сама:
        /// хозяину круг через сеть добавил бы задержку к единственному
        /// признаку «мой отсчёт пошёл». В соло не используется — там состояние
        /// каждой кнопки считается на этой же машине.
        /// </summary>
        public bool NetworkLit { get; set; }

        public ButtonState State { get; private set; } = ButtonState.Idle;

        /// <summary>
        /// Признак «эта кнопка горит» для чужой клетки: в сети его приносит
        /// сервер, в соло считает эта же машина.
        /// </summary>
        private bool NeighbourLit =>
            WorldAuthority.IsNetworkSession ? NetworkLit : State != ButtonState.Idle;

        /// <summary>Отмеренный интервал последнего подраунда, с. До «стопа» — ноль.</summary>
        public float Measured => measured;

        /// <summary>Успел ли хозяин закрыть отсчёт в этом подраунде.</summary>
        public bool Completed => State == ButtonState.Stopped;

        public string InteractionPrompt => State == ButtonState.Idle ? "Держать кнопку" : "Отпустить";

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            ApplyColor(idleColor);
        }

        /// <summary>Кому принадлежит кнопка. Чужие нажать её не могут.</summary>
        public void SetOwner(PlayerController player)
        {
            owner = player;
        }

        /// <summary>
        /// Открыть окно отмера: кнопка снова с нуля. Зовётся на старте стадии
        /// отмера каждого подраунда.
        /// </summary>
        public void OpenWindow(float windowSeconds)
        {
            measureCap = Mathf.Max(0f, windowSeconds);
            WindowOpen = true;
            NetworkLit = false;
            State = ButtonState.Idle;
            measured = 0f;
            startedAt = 0d;
            heldSinceRealtime = 0d;
            ApplyColor(idleColor);
        }

        /// <summary>
        /// Закрыть окно. Подсветка гаснет у всех одновременно — в этом и смысл:
        /// момент «стопа» соседа так и остаётся невидимым.
        /// </summary>
        public void CloseWindow()
        {
            WindowOpen = false;
            NetworkLit = false;

            // Кто так и не отпустил кнопку до конца окна — не завершил замер.
            // Гасим удержание здесь, иначе позднее отпускание досчитало бы
            // интервал уже после того, как результаты подраунда посчитаны.
            if (State == ButtonState.Running)
            {
                State = ButtonState.Idle;
                measured = 0f;
            }

            ApplyColor(idleColor);
        }

        public bool CanInteract(PlayerController player)
        {
            return WindowOpen && State != ButtonState.Stopped && player != null && player == owner;
        }

        /// <summary>Разового нажатия у этой кнопки нет — она удерживаемая.</summary>
        public void Interact(PlayerController player) { }

        /// <summary>
        /// Единственная точка входа: удержание началось или кончилось.
        /// Зовётся на машине хозяина кнопки — там, где нажали.
        ///
        /// В сетевой катке отсюда уходит только **намерение**, а исход считает
        /// сервер. Наружу идут две разные величины, и путать их нельзя:
        ///
        /// - **метка на общих часах** — «когда нажали». Нужна серверу, чтобы
        ///   проверить, что нажатие попало в окно отмера, а не пришло из
        ///   прошлого. Мерить по времени прибытия RPC нельзя: ошибка была бы
        ///   размером с джиттер, а порог ничьей на двоих — 50 мс;
        /// - **длительность по монотонным часам этой машины** — «сколько
        ///   держали». Именно она и есть замер.
        ///
        /// Раньше замером служила разность двух меток на общих часах, и это
        /// оказалось неверно: у клиента общие часы — это оценка серверного
        /// времени, которую NGO **подводит толчками**. Подводка, попавшая между
        /// нажатием и отпусканием, целиком уезжала в результат. Замерено
        /// 19.08 на канале с задержкой 150 мс: удержания 3.0 с и 8.1 с
        /// разошлись с настоящими на −59 и +34 мс при пороге 30, причём
        /// в обе стороны и без всякой связи с длиной. Монотонные часы
        /// никто не подводит, и эта ошибка исчезает целиком.
        /// </summary>
        public void HoldChanged(PlayerController player, bool held)
        {
            double stamp = NetworkClock.Now;
            float heldSeconds = TrackLocalHold(held);

            // Авторитет здесь же — считаем сразу, круг через сеть к самому себе
            // ничего не проверил бы и только добавил шаг отправки.
            if (WorldAuthority.HasAuthority)
            {
                ApplyHold(player, held, stamp, heldSeconds);
                return;
            }

            if (player != owner)
            {
                return;
            }

            // Своя лампа зажигается сразу: это единственный признак «мой отсчёт
            // пошёл», и ждать подтверждения сервера тут нельзя. На исход эта
            // догадка не влияет — замер придёт от сервера.
            if (held && State == ButtonState.Idle && WindowOpen)
            {
                State = ButtonState.Running;
            }
            else if (!held && State == ButtonState.Running)
            {
                State = ButtonState.Stopped;
            }

            HoldIntent?.Invoke(this, held, stamp, heldSeconds);
        }

        /// <summary>
        /// Засечь удержание по монотонным часам этой машины и вернуть его
        /// длительность на отпускании. На нажатии возвращает ноль.
        ///
        /// <see cref="Time.realtimeSinceStartupAsDouble"/>, а не время сцены:
        /// нужны часы, которые никто не подводит и не масштабирует.
        /// </summary>
        private float TrackLocalHold(bool held)
        {
            double realtime = Time.realtimeSinceStartupAsDouble;

            if (held)
            {
                heldSinceRealtime = realtime;
                return 0f;
            }

            if (heldSinceRealtime <= 0d)
            {
                return 0f;
            }

            float seconds = (float)(realtime - heldSinceRealtime);
            heldSinceRealtime = 0d;
            return seconds;
        }

        /// <summary>
        /// Исход нажатия: у авторитета — из <see cref="HoldChanged"/>, в сети —
        /// от сервера по проверенной метке клиента.
        ///
        /// Метка решает, засчитано ли нажатие вообще, а замером служит
        /// <paramref name="heldSeconds"/> — длительность с монотонных часов
        /// той машины, где держали кнопку, уже проверенная сервером на
        /// правдоподобие. Зажимается длиной окна отмера.
        /// </summary>
        public void ApplyHold(PlayerController player, bool held, double stamp, float heldSeconds)
        {
            if (held)
            {
                if (!CanInteract(player) || State != ButtonState.Idle)
                {
                    return;
                }

                State = ButtonState.Running;
                startedAt = stamp;
                Started?.Invoke(this);
                return;
            }

            // Отпускание принимаем и без CanInteract: игрок мог отойти или окно
            // могло закрыться, но отсчёт всё равно обязан закрыться корректно.
            if (State != ButtonState.Running || player != owner)
            {
                return;
            }

            State = ButtonState.Stopped;
            measured = Mathf.Clamp(heldSeconds, 0f, measureCap);
            Stopped?.Invoke(this, measured);
        }

        /// <summary>
        /// Сколько идёт отсчёт прямо сейчас. Наружу отдаётся только под флагом
        /// debugShowTimer: увидеть это в игре — значит увидеть свой результат
        /// до стадии показа, а на этом держится весь смысл игры.
        /// </summary>
        public float DebugElapsed => State == ButtonState.Running ? (float)(NetworkClock.Now - startedAt) : measured;

        private void Update()
        {
            if (!WindowOpen)
            {
                return;
            }

            // Пульсирует только своя кнопка и только пока идёт отсчёт: это
            // единственный признак «мой отсчёт пошёл», других в игре нет.
            if (ViewedByOwner)
            {
                if (State == ButtonState.Running)
                {
                    float pulse = 1f - pulseAmount * 0.5f * (1f + Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f));
                    ApplyColor(ownRunningColor * pulse);
                }

                return;
            }

            if (!NeighbourLightsEnabled)
            {
                return;
            }

            // Чужая горит ровно и не гаснет на «стоп». В сетевой катке нажатие
            // соседа на этой машине не исполняется вовсе, поэтому источник —
            // серверное состояние, а не локальное.
            ApplyColor(NeighbourLit ? neighbourRunningColor : idleColor);
        }

        private void ApplyColor(Color color)
        {
            if (lamp != null)
            {
                lamp.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                lamp.SetPropertyBlock(block);
            }

            if (beacon == null)
            {
                return;
            }

            // Лампа горит ровно тогда же, когда светится кнопка, и тем же
            // цветом. Смысл в том, что кнопку заслоняет спина персонажа,
            // а свет в клетке виден и краем глаза.
            bool lit = (State != ButtonState.Idle || NeighbourLit) && WindowOpen;
            beacon.enabled = lit;
            if (lit)
            {
                beacon.color = color;
                beacon.intensity = beaconIntensity * Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            }
        }
    }
}
