using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Networking
{
    /// <summary>
    /// Сетевая половина мини-игры: вешается на тот же объект, что контроллер.
    /// Реплицирует фазу и время раунда через NetworkVariable, итоговые места —
    /// через RPC (это событие, а не состояние). Правила игры остаются обычным
    /// MonoBehaviour и работают без моста, когда сцену открывают напрямую.
    /// </summary>
    public sealed class NetworkMinigameBridge : NetworkBehaviour, IMinigameNetworkBridge
    {
        [Tooltip("Сколько раз в секунду сервер рассылает время раунда")]
        [Range(1f, 30f)]
        [SerializeField] private float timeSyncRate = 10f;

        private readonly NetworkVariable<MinigamePhase> phase =
            new NetworkVariable<MinigamePhase>(MinigamePhase.Idle);

        private readonly NetworkVariable<float> roundRemaining = new NetworkVariable<float>(0f);
        private readonly NetworkVariable<float> roundDuration = new NetworkVariable<float>(0f);

        /// <summary>Переиспользуемый контейнер под приехавшие места (без аллокаций в раунде).</summary>
        private readonly MinigameResults incoming = new MinigameResults();

        private IMinigameNetworkTarget target;
        private float nextSyncTime;

        public bool HasAuthority => !IsSpawned || IsServer;

        /// <summary>
        /// NetworkManager запущен ещё в Boot-сцене, поэтому ответ готов сразу —
        /// до того как этот объект успеет заспавниться.
        /// </summary>
        public bool IsNetworkSession =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private void Awake()
        {
            target = GetComponent<IMinigameNetworkTarget>();
            if (target == null)
            {
                Debug.LogError($"{name}: NetworkMinigameBridge не нашёл контроллер мини-игры на своём объекте", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            phase.OnValueChanged += OnPhaseChanged;
            roundRemaining.OnValueChanged += OnRoundTimeChanged;

            // Подключились в середине раунда — догоняем текущую фазу.
            if (!IsServer && phase.Value != MinigamePhase.Idle)
            {
                target?.ApplyPhase(phase.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            phase.OnValueChanged -= OnPhaseChanged;
            roundRemaining.OnValueChanged -= OnRoundTimeChanged;

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || target == null)
            {
                return;
            }

            // Время шлём с фиксированной частотой: каждый кадр — это трафик
            // впустую, HUD всё равно показывает целые секунды.
            if (Time.time < nextSyncTime)
            {
                return;
            }

            nextSyncTime = Time.time + 1f / Mathf.Max(1f, timeSyncRate);

            if (target.TryGetRoundTime(out float remaining, out float duration))
            {
                roundRemaining.Value = remaining;
                roundDuration.Value = duration;
            }
        }

        // ========== СЕРВЕР ПУБЛИКУЕТ ==========

        public void PublishPhase(MinigamePhase newPhase)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            phase.Value = newPhase;
        }

        /// <summary>
        /// Игрок нажал «Выход» посреди раунда. Намерение уходит на сервер, а
        /// решает он: правила выхода — те же, что при дисконнекте, и считать
        /// их вправе только авторитет.
        ///
        /// Отправителя берём из <c>RpcParams</c>, а не из сообщения: номер в
        /// сообщении клиент подделал бы и выбил из раунда чужого.
        /// </summary>
        public void RequestLeaveRound()
        {
            if (!IsSpawned)
            {
                return;
            }

            LeaveRoundRpc();
        }

        [Rpc(SendTo.Server)]
        private void LeaveRoundRpc(RpcParams rpcParams = default)
        {
            target?.ApplyLeaveRound((int)rpcParams.Receive.SenderClientId);
        }

        public void PublishResults(MinigameResults results)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            var entries = results.Entries;
            var ids = new int[entries.Count];
            var places = new int[entries.Count];

            for (int i = 0; i < entries.Count; i++)
            {
                ids[i] = entries[i].PlayerId;
                places[i] = entries[i].Place;
            }

            ApplyResultsRpc(ids, places);
        }

        // ========== КЛИЕНТ ПРИМЕНЯЕТ ==========

        private void OnPhaseChanged(MinigamePhase previous, MinigamePhase current)
        {
            // У сервера контроллер уже в этой фазе — ApplyPhase там ничего не сделает.
            target?.ApplyPhase(current);
        }

        private void OnRoundTimeChanged(float previous, float current)
        {
            target?.ApplyRoundTime(current, roundDuration.Value);
        }

        /// <summary>Хост уже показал итоги локально, поэтому шлём только остальным.</summary>
        [Rpc(SendTo.NotServer)]
        private void ApplyResultsRpc(int[] ids, int[] places)
        {
            incoming.Clear();
            for (int i = 0; i < ids.Length && i < places.Length; i++)
            {
                incoming.Add(ids[i], places[i]);
            }

            target?.ApplyResults(incoming);
        }
    }
}
