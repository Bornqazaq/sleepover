using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Items;
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

        /// <summary>
        /// Один держащий и его отсчёт.
        ///
        /// Держащих <b>несколько</b>, и это не роскошь. Одним полем «кто держит»
        /// двое из одной команды, подошедшие вместе, запирали друг друга
        /// насмерть: второй перебивал первого, первый об этом не узнавал — а
        /// узнать ему неоткуда, удержание уходит на сервер только на переходе
        /// «нажал» / «отпустил». Сброшенный так игрок оставался «держащим» у
        /// себя и молчал до конца раунда. На стенде это стоило команде A всех
        /// шести ходок: ноль тары за 200 секунд при живых болванках.
        ///
        /// Правило одной бутыли от этого не страдает: тару получает тот, кто
        /// первым достоял свои полторы секунды, а остальным отсчёт обнуляет
        /// сам факт, что штабель больше не готов.
        /// </summary>
        private struct Holder
        {
            public PlayerController Player;
            public float Timer;
        }

        private TeamSide team = TeamSide.None;
        private int handleCount = 1;
        private float voidLevel = float.NegativeInfinity;

        private WaterBottle liveBottle;
        private readonly List<Holder> holders = new List<Holder>(MultiCarryObject.MaxHandles);

        public string InteractionPrompt => prompt;

        /// <summary>Живая бутыль команды. Null — команда осталась без тары, штабель готов выдать.</summary>
        public WaterBottle LiveBottle => liveBottle;

        /// <summary>Готов ли штабель выдать новую бутыль прямо сейчас.</summary>
        public bool Ready => liveBottle == null;

        /// <summary>
        /// Сколько секунд осталось держать E тому, кто ближе всех к выдаче.
        /// Ноль — не держит никто.
        /// </summary>
        public float HoldRemaining
        {
            get
            {
                float best = 0f;
                for (int i = 0; i < holders.Count; i++)
                {
                    float left = Mathf.Max(0f, config.TakeSeconds - holders[i].Timer);
                    if (i == 0 || left < best)
                    {
                        best = left;
                    }
                }

                return best;
            }
        }

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
            if (player == null)
            {
                return;
            }

            int index = IndexOfHolder(player);

            if (!held)
            {
                if (index >= 0)
                {
                    holders.RemoveAt(index);
                    RelayHold(player, false);
                }

                return;
            }

            // Уже держит — второй раз отсчёт не сбрасываем: одно и то же
            // намерение может доехать дважды, и обнуление съело бы полсекунды.
            if (index >= 0 || !CanInteract(player))
            {
                return;
            }

            holders.Add(new Holder { Player = player, Timer = 0f });
            RelayHold(player, true);
        }

        private int IndexOfHolder(PlayerController player)
        {
            for (int i = 0; i < holders.Count; i++)
            {
                if (holders[i].Player == player)
                {
                    return i;
                }
            }

            return -1;
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

            if (holders.Count == 0)
            {
                return;
            }

            float delta = Time.deltaTime;

            for (int i = holders.Count - 1; i >= 0; i--)
            {
                Holder entry = holders[i];

                // Ушёл из матча, был сбит или команда успела взять тару другим
                // человеком — отсчёт обнуляется, а не доигрывается сам собой.
                if (entry.Player == null || !Ready || entry.Player.IsKnockedDown)
                {
                    PlayerController dropped = entry.Player;
                    holders.RemoveAt(i);
                    RelayHold(dropped, false);
                    continue;
                }

                entry.Timer += delta;

                if (entry.Timer < config.TakeSeconds)
                {
                    holders[i] = entry;
                    continue;
                }

                // Выдаёт сервер. У клиента отсчёт досчитан и стоит на месте:
                // полоса над штабелем полная, а бутыль появится, когда её
                // объявят.
                if (!WorldAuthority.HasAuthority)
                {
                    entry.Timer = config.TakeSeconds;
                    holders[i] = entry;
                    continue;
                }

                PlayerController taker = entry.Player;
                holders.RemoveAt(i);
                Dispense(taker);
                return;
            }
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
            holders.Clear();

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
