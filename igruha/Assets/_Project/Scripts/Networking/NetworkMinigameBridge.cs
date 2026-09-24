using System.Collections.Generic;
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
    public sealed class NetworkMinigameBridge : NetworkBehaviour, IMinigameNetworkBridge, ITutorialNetworkBridge
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
        private ITutorialNetworkTarget tutorialTarget;
        private NetworkList<TutorialParticipant> tutorialParticipants;
        private readonly List<TutorialParticipant> tutorialMirror = new List<TutorialParticipant>();
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
            tutorialTarget = GetComponent<ITutorialNetworkTarget>();
            tutorialParticipants = new NetworkList<TutorialParticipant>();
            if (target == null)
            {
                Debug.LogError($"{name}: NetworkMinigameBridge не нашёл контроллер мини-игры на своём объекте", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            tutorialParticipants.OnListChanged += OnTutorialChanged;
            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback += OnTutorialParticipantDisconnected;
                if (tutorialTarget != null) PublishTutorialReadiness(tutorialTarget.TutorialParticipants);
            }
            else ApplyTutorialSnapshot();
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
            tutorialParticipants.OnListChanged -= OnTutorialChanged;
            if (IsServer) NetworkManager.OnClientDisconnectCallback -= OnTutorialParticipantDisconnected;
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

        public void PublishTutorialReadiness(IReadOnlyList<TutorialParticipant> participants)
        {
            if (!IsSpawned || !IsServer) return;
            // Один снимок состава; дальше меняются только изменившиеся строки.
            if (tutorialParticipants.Count != participants.Count)
            {
                tutorialParticipants.Clear();
                for (int i = 0; i < participants.Count; i++) tutorialParticipants.Add(participants[i]);
                return;
            }
            for (int i = 0; i < participants.Count; i++)
                if (!tutorialParticipants[i].Equals(participants[i])) tutorialParticipants[i] = participants[i];
        }

        public void RequestTutorialReady(bool ready)
        {
            if (IsSpawned) TutorialReadyServerRpc(ready);
        }

        [ServerRpc(RequireOwnership = false)]
        private void TutorialReadyServerRpc(bool ready, ServerRpcParams rpc = default)
        {
            // Номер игрока не приходит в полезной нагрузке: его задаёт транспорт.
            tutorialTarget?.SetTutorialReady((int)rpc.Receive.SenderClientId, ready);
        }

        public void RequestPracticeRestart()
        {
            if (IsSpawned) RestartPracticeServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RestartPracticeServerRpc(ServerRpcParams rpc = default)
        {
            tutorialTarget?.RestartPractice((int)rpc.Receive.SenderClientId);
        }

        private void OnTutorialParticipantDisconnected(ulong clientId) =>
            tutorialTarget?.RemoveTutorialParticipant((int)clientId);

        private void OnTutorialChanged(NetworkListEvent<TutorialParticipant> change)
        {
            if (!IsServer) ApplyTutorialSnapshot();
        }

        private void ApplyTutorialSnapshot()
        {
            tutorialMirror.Clear();
            for (int i = 0; i < tutorialParticipants.Count; i++) tutorialMirror.Add(tutorialParticipants[i]);
            tutorialTarget?.ApplyTutorialReadiness(tutorialMirror);
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

        /// <summary>
        /// Очки едут вместе с местами. Сумма катки лежит и в NetworkList
        /// ростера, но его дельта уходит отдельным сообщением в конце тика и
        /// может приехать после этого RPC — экран итогов на клиенте показал
        /// бы прошлую сумму. Один пакет — одна картинка у всех.
        /// </summary>
        public void PublishResults(MinigameResults results, bool seriesFinal)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            var entries = results.Entries;
            var ids = new int[entries.Count];
            var places = new int[entries.Count];
            var points = new int[entries.Count];
            var totals = new int[entries.Count];

            for (int i = 0; i < entries.Count; i++)
            {
                ids[i] = entries[i].PlayerId;
                places[i] = entries[i].Place;
                points[i] = entries[i].Points;
                totals[i] = entries[i].Total;
            }

            ApplyResultsRpc(ids, places, points, totals, results.PlayerCount, results.CountsTowardSession, seriesFinal);
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
        private void ApplyResultsRpc(int[] ids, int[] places, int[] points, int[] totals, int playerCount,
                                     bool countsTowardSession, bool seriesFinal)
        {
            incoming.Reset();
            incoming.PlayerCount = playerCount;
            incoming.CountsTowardSession = countsTowardSession;
            int count = Mathf.Min(Mathf.Min(ids.Length, places.Length), Mathf.Min(points.Length, totals.Length));
            for (int i = 0; i < count; i++)
            {
                incoming.Add(ids[i], places[i], points[i], totals[i]);
            }

            target?.ApplyResults(incoming, seriesFinal);
        }
    }
}
