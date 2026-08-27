using System;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Штабель тары в стартовой зоне команды. Бесконечный, но выдаёт по одной.
    ///
    /// <b>Правило одной бутыли (спека 5.1).</b> У команды в любой момент
    /// существует ровно одна бутыль, и штабель не выдаёт новую, пока прежняя
    /// жива. Без этого появляется вырожденная тактика: расплескали половину,
    /// бросили, побежали за свежей — и вся цена ошибки исчезает.
    ///
    /// Новая становится доступна сразу, как только прежняя перестала
    /// существовать: слита в бак и улетела в пропасть — мгновенно, опустела и
    /// брошена — через отсчёт, который ведёт сама бутыль.
    ///
    /// Взятие — удержание E, а не нажатие: полторы секунды у штабеля это
    /// половина цены ошибки, и пропускать их нельзя.
    ///
    /// <b>Сеть.</b> Полторы секунды отсчитывает сервер: тара — это ходка, а
    /// ходка — счёт. Удержание у клиента идёт своим чередом ради полосы над
    /// штабелем, но выдать бутыль его отсчёт не может — он только шлёт серверу
    /// «держу» и «отпустил» через <see cref="HoldRelay"/>.
    /// </summary>
    public sealed class BottleStack : MonoBehaviour, IHoldInteractable
    {
        [SerializeField] private CarryItemConfig config;
        [Tooltip("Префаб бутыли")]
        [SerializeField] private WaterBottle bottlePrefab;
        [Tooltip("Откуда бутыль появляется. Пусто — из этого же объекта")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Что показывает, что штабель готов выдать. Гаснет, пока команда несёт свою бутыль")]
        [SerializeField] private GameObject readyIndicator;
        [Tooltip("Подсказка над штабелем")]
        [SerializeField] private string prompt = "Взять бутыль (держать E)";
        [Tooltip("С какого расстояния можно браться, м. Запас над радиусом взаимодействия: у сервера позиция клиента отстаёт")]
        [SerializeField] private float takeReach = 2.7f;

        /// <summary>Штабель выдал бутыль. По этому событию правила её донастраивают.</summary>
        public event Action<WaterBottle> BottleTaken;

        private TeamSide team = TeamSide.None;
        private int handleCount = 1;
        private float voidLevel = float.NegativeInfinity;

        private WaterBottle liveBottle;
        private PlayerController holder;
        private float holdTimer;

        public string InteractionPrompt => prompt;

        /// <summary>Живая бутыль команды. Null — команда осталась без тары, штабель готов выдать.</summary>
        public WaterBottle LiveBottle => liveBottle;

        /// <summary>Готов ли штабель выдать новую бутыль прямо сейчас.</summary>
        public bool Ready => liveBottle == null;

        /// <summary>Сколько секунд осталось держать E. Ноль — не держат вовсе.</summary>
        public float HoldRemaining => holder != null ? Mathf.Max(0f, config.TakeSeconds - holdTimer) : 0f;

        /// <summary>Чей штабель.</summary>
        public TeamSide Team => team;

        /// <summary>
        /// Куда уходит намерение «держу E у штабеля», когда решает не эта
        /// машина. Ставят правила раунда; вне сети остаётся пустым, и тогда
        /// отсчёт здесь же и решает.
        /// </summary>
        public Action<TeamSide, bool> HoldRelay { get; set; }

        private void Awake()
        {
            if (spawnPoint == null)
            {
                spawnPoint = transform;
            }

            if (bottlePrefab == null)
            {
                Debug.LogError($"{name}: BottleStack без префаба бутыли — выдавать нечего.", this);
                enabled = false;
            }
        }

        /// <summary>Подключить штабель к команде. Зовут правила раунда на старте.</summary>
        public void Configure(CarryItemConfig gameConfig, TeamSide side, int teamSize, float voidY)
        {
            config = gameConfig;
            team = side;
            handleCount = Mathf.Max(1, teamSize);
            voidLevel = voidY;
            ApplyReadyVisual();
        }

        /// <summary>Сколько рук у выдаваемых бутылей. Меняется, когда команда теряет игрока.</summary>
        public void SetTeamSize(int teamSize)
        {
            handleCount = Mathf.Max(1, teamSize);
            liveBottle?.Carry.SetHandleCount(handleCount);
        }

        public bool CanInteract(PlayerController player)
        {
            if (!enabled || player == null || !Ready)
            {
                return false;
            }

            // Дотягивается ли. Проверка здесь, а не только в радиусе поиска:
            // по сети намерение приходит от клиента, и верить ему в дистанции
            // нельзя — иначе тару берут через всю арену.
            if ((player.transform.position - spawnPoint.position).sqrMagnitude > takeReach * takeReach)
            {
                return false;
            }

            // Штабель свой: чужой у нашего штабеля тары не возьмёт.
            return TeamFilter == null || TeamFilter(player);
        }

        /// <summary>Кому разрешено брать. Ставят правила раунда — это проверка своей команды.</summary>
        public Predicate<PlayerController> TeamFilter { get; set; }

        /// <summary>
        /// Разовое нажатие удерживаемому интерактиву не адресуют — это делает
        /// <c>PlayerInteractor</c>. Метод остаётся пустым по контракту
        /// <see cref="IInteractable"/>.
        /// </summary>
        public void Interact(PlayerController player) { }

        /// <summary>
        /// Единственная точка входа удержания: держат — идёт отсчёт, отпустили —
        /// сбрасывается. Зовёт мотор владельца у себя и сервер — за него,
        /// получив намерение.
        /// </summary>
        public void HoldChanged(PlayerController player, bool held)
        {
            if (!held)
            {
                if (holder == player)
                {
                    holder = null;
                    holdTimer = 0f;
                    RelayHold(player, false);
                }

                return;
            }

            if (!CanInteract(player))
            {
                return;
            }

            holder = player;
            holdTimer = 0f;
            RelayHold(player, true);
        }

        /// <summary>
        /// Переслать намерение серверу. Только со своей машины и только за
        /// своего: чужую копию персонажа ведёт её владелец, и её удержание
        /// отправит он сам.
        /// </summary>
        private void RelayHold(PlayerController player, bool held)
        {
            if (HoldRelay == null || WorldAuthority.HasAuthority)
            {
                return;
            }

            NetworkObject playerObject = player != null ? player.GetComponent<NetworkObject>() : null;
            if (playerObject == null || !playerObject.IsSpawned || !playerObject.IsOwner)
            {
                return;
            }

            HoldRelay(team, held);
        }

        private void Update()
        {
            ApplyReadyVisual();

            if (holder == null)
            {
                return;
            }

            // Отошёл, был сбит или команда успела взять тару другим человеком —
            // отсчёт обнуляется, а не доигрывается сам собой.
            if (!Ready || holder.IsKnockedDown)
            {
                PlayerController dropped = holder;
                holder = null;
                holdTimer = 0f;
                RelayHold(dropped, false);
                return;
            }

            holdTimer += Time.deltaTime;
            if (holdTimer < config.TakeSeconds)
            {
                return;
            }

            // Выдаёт сервер. У клиента отсчёт досчитан и стоит на месте: полоса
            // над штабелем полная, а бутыль появится, когда её объявят.
            if (!WorldAuthority.HasAuthority)
            {
                holdTimer = config.TakeSeconds;
                return;
            }

            PlayerController taker = holder;
            holder = null;
            holdTimer = 0f;
            Dispense(taker);
        }

        /// <summary>
        /// Выдать бутыль. Единственная точка появления тары, и решает её только
        /// авторитет: в сетевой катке он же её и спавнит.
        /// </summary>
        public WaterBottle Dispense(PlayerController taker)
        {
            if (!WorldAuthority.HasAuthority || !Ready)
            {
                return null;
            }

            WaterBottle bottle = Instantiate(bottlePrefab, spawnPoint.position, spawnPoint.rotation);
            bottle.name = $"Bottle_{team}";
            bottle.Initialize(config, team, handleCount, voidLevel);

            AdoptBottle(bottle);

            if (WorldAuthority.IsNetworkSession && bottle.TryGetComponent(out NetworkObject netObject))
            {
                netObject.Spawn();
            }

            BottleTaken?.Invoke(bottle);

            // Взявший сразу берётся за ручку: полторы секунды у штабеля он уже
            // отстоял, и заставлять его нажимать E второй раз незачем.
            if (taker != null)
            {
                bottle.Carry.TryGrab(taker);
            }

            return bottle;
        }

        /// <summary>
        /// Записать бутыль как живую тару команды. Зовёт и сам штабель при
        /// выдаче, и правила раунда — за бутыль, приехавшую из сети: правило
        /// одной бутыли должно читаться на каждой машине, иначе клиент видит
        /// готовый штабель там, где у сервера тара уже в руках.
        /// </summary>
        public void AdoptBottle(WaterBottle bottle)
        {
            if (bottle == null || liveBottle == bottle)
            {
                return;
            }

            liveBottle = bottle;
            bottle.Gone += OnBottleGone;
            ApplyReadyVisual();
        }

        private void OnBottleGone(WaterBottle bottle)
        {
            if (liveBottle != bottle)
            {
                return;
            }

            bottle.Gone -= OnBottleGone;
            liveBottle = null;
            ApplyReadyVisual();
        }

        /// <summary>Убрать живую бутыль команды — конец раунда.</summary>
        public void ClearLiveBottle()
        {
            holder = null;
            holdTimer = 0f;

            if (liveBottle != null)
            {
                liveBottle.Gone -= OnBottleGone;
                liveBottle = null;
            }
        }

        private void ApplyReadyVisual()
        {
            if (readyIndicator != null && readyIndicator.activeSelf != Ready)
            {
                readyIndicator.SetActive(Ready);
            }
        }
    }
}
