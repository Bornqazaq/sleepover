using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Мозг катки (минимум, без сети): список игроков, текущая мини-игра,
    /// приём результатов, начисление очков по формуле «очки = число_игроков − место»
    /// (GDD 3.1). Случайная выборка игр, ничьи, тай-брейк, rematch — позже;
    /// структура для них открыта. При переходе на NGO счёт станет
    /// NetworkVariable, начисление останется только на сервере.
    /// </summary>
    public sealed class SessionManager : MonoBehaviour
    {
        public static SessionManager Instance { get; private set; }

        public event Action ScoresChanged;

        private readonly List<SessionPlayer> players = new List<SessionPlayer>(8);
        private IMinigame currentMinigame;

        public IReadOnlyList<SessionPlayer> Players => players;
        public IMinigame CurrentMinigame => currentMinigame;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            DetachMinigame();
        }

        public void ClearPlayers()
        {
            players.Clear();
            ScoresChanged?.Invoke();
        }

        public void RegisterPlayer(SessionPlayer player)
        {
            players.Add(player);
        }

        public SessionPlayer FindPlayer(int playerId)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Id == playerId)
                {
                    return players[i];
                }
            }

            return null;
        }

        /// <summary>Привязать текущую мини-игру: её результаты начислят очки.</summary>
        public void AttachMinigame(IMinigame minigame)
        {
            DetachMinigame();
            currentMinigame = minigame;
            if (currentMinigame != null)
            {
                currentMinigame.ResultsReported += HandleResults;
            }
        }

        private void DetachMinigame()
        {
            if (currentMinigame != null)
            {
                currentMinigame.ResultsReported -= HandleResults;
                currentMinigame = null;
            }
        }

        private void HandleResults(MinigameResults results)
        {
            int playerCount = players.Count;
            IReadOnlyList<MinigameResults.PlayerResult> entries = results.Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                SessionPlayer player = FindPlayer(entries[i].PlayerId);
                if (player == null)
                {
                    continue;
                }

                player.Score += Mathf.Max(0, playerCount - entries[i].Place);
            }

            ScoresChanged?.Invoke();
        }
    }
}
