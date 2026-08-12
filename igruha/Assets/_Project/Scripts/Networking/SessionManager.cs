using Unity.Netcode;
using UnityEngine;

namespace Igruha.Networking
{
    /// <summary>
    /// Управляет состоянием игровой сессии (раунды, счёт, фаза).
    ///
    /// Паттерн по CLAUDE.md 3.2:
    /// - NetworkVariable для состояния (synchronized to all clients)
    /// - ServerRpc для действий (client requests, server validates and applies)
    /// - НЕ использовать RPC для передачи состояния (только NetworkVariable)
    ///
    /// Это шаблон для всех эпиков (мини-игры, хаб, etc.) — копировать паттерн.
    /// </summary>
    public class SessionManager : NetworkBehaviour
    {
        // ========== GAME STATE (NetworkVariable) ==========

        /// <summary>
        /// Текущий раунд (0-indexed).
        /// Изменяется только на сервере, синхронизируется всем.
        /// </summary>
        public NetworkVariable<int> CurrentRound = new NetworkVariable<int>(
            value: 0,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// Текущая фаза игры (Lobby, Playing, Results, etc.)
        /// </summary>
        public NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(
            value: GamePhase.Lobby,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// Оставшееся время текущего раунда (в секундах).
        /// </summary>
        public NetworkVariable<float> RoundTimeRemaining = new NetworkVariable<float>(
            value: 0f,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// Счёт игроков: индекс = ClientId, значение = очки.
        /// Максимум 8 игроков (по правилам игры).
        /// </summary>
        private NetworkVariable<int>[] PlayerScores = new NetworkVariable<int>[8];

        // ========== INITIALIZATION ==========

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Инициализировать массив счётов
            for (int i = 0; i < PlayerScores.Length; i++)
            {
                PlayerScores[i] = new NetworkVariable<int>(0);
            }

            // Подписаться на изменения состояния
            CurrentRound.OnValueChanged += OnRoundChanged;
            Phase.OnValueChanged += OnPhaseChanged;
            RoundTimeRemaining.OnValueChanged += OnTimeChanged;

            if (IsServer)
            {
                Debug.Log("✅ SessionManager (SERVER): State management ready");
            }
            else
            {
                Debug.Log("📡 SessionManager (CLIENT): Listening to state changes");
            }
        }

        public override void OnNetworkDespawn()
        {
            CurrentRound.OnValueChanged -= OnRoundChanged;
            Phase.OnValueChanged -= OnPhaseChanged;
            RoundTimeRemaining.OnValueChanged -= OnTimeChanged;

            base.OnNetworkDespawn();
        }

        // ========== SERVER-SIDE STATE CHANGES ==========

        /// <summary>
        /// Сервер: начать новый раунд.
        /// </summary>
        public void StartNewRound()
        {
            if (!IsServer)
                return;

            CurrentRound.Value++;
            Phase.Value = GamePhase.Playing;
            RoundTimeRemaining.Value = 60f; // 60 сек раунд

            Debug.Log($"🎮 [SERVER] Round {CurrentRound.Value} started");
        }

        /// <summary>
        /// Сервер: завершить раунд и перейти к результатам.
        /// </summary>
        public void EndRound()
        {
            if (!IsServer)
                return;

            Phase.Value = GamePhase.Results;
            RoundTimeRemaining.Value = 0f;

            Debug.Log($"🎮 [SERVER] Round {CurrentRound.Value} ended");
        }

        /// <summary>
        /// Сервер: добавить очки игроку.
        /// </summary>
        public void AddScore(int playerIndex, int points)
        {
            if (!IsServer || playerIndex < 0 || playerIndex >= 8)
                return;

            PlayerScores[playerIndex].Value += points;

            Debug.Log($"⭐ [SERVER] Player {playerIndex} +{points} points (total: {PlayerScores[playerIndex].Value})");
        }

        /// <summary>
        /// Обновить оставшееся время (вызывается из Update на сервере).
        /// </summary>
        public void UpdateRoundTimer(float deltaTime)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing)
                return;

            RoundTimeRemaining.Value -= deltaTime;
            if (RoundTimeRemaining.Value <= 0f)
            {
                RoundTimeRemaining.Value = 0f;
                EndRound();
            }
        }

        // ========== CLIENT-SIDE OBSERVERS (подписка на изменения) ==========

        private void OnRoundChanged(int oldValue, int newValue)
        {
            Debug.Log($"📡 [CLIENT] Round changed: {oldValue} → {newValue}");
            // Обновить UI: показать номер раунда
            // UIManager.Instance.UpdateRoundDisplay(newValue);
        }

        private void OnPhaseChanged(GamePhase oldValue, GamePhase newValue)
        {
            Debug.Log($"📡 [CLIENT] Phase changed: {oldValue} → {newValue}");
            // Обновить UI: показать фазу (Lobby/Playing/Results)
            // UIManager.Instance.UpdatePhaseDisplay(newValue);
        }

        private void OnTimeChanged(float oldValue, float newValue)
        {
            // Это может вызваться часто (каждый frame), поэтому не логировать
            // Debug.Log($"Time: {newValue:F1}s");
            // UIManager.Instance.UpdateTimerDisplay(newValue);
        }

        // ========== PUBLIC GETTERS (читать состояние) ==========

        /// <summary>
        /// Получить счёт игрока.
        /// </summary>
        public int GetPlayerScore(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= 8)
                return 0;

            return PlayerScores[playerIndex].Value;
        }

        /// <summary>
        /// Получить текущую фазу игры.
        /// </summary>
        public GamePhase GetCurrentPhase() => Phase.Value;

        /// <summary>
        /// Получить оставшееся время раунда.
        /// </summary>
        public float GetTimeRemaining() => RoundTimeRemaining.Value;

        // ========== EXAMPLE: ClientRpc для событий (не для состояния!) ==========

        /// <summary>
        /// ClientRpc: оповестить всех о победителе раунда.
        /// Это СОБЫТИЕ, не состояние — поэтому используем RPC, не NetworkVariable.
        /// </summary>
        [ClientRpc]
        public void AnnounceWinnerClientRpc(int winnerIndex, string winnerName)
        {
            Debug.Log($"🎉 {winnerName} won the round!");
            // UIManager.Instance.ShowWinnerBanner(winnerName);
        }
    }

    /// <summary>
    /// Фаза игровой сессии.
    /// Может быть синхронизирована через NetworkVariable.
    /// </summary>
    public enum GamePhase
    {
        Lobby = 0,      // Ожидание игроков
        Playing = 1,    // Идёт раунд
        Results = 2,    // Показываем результаты
        Waiting = 3     // Между раундами
    }
}
