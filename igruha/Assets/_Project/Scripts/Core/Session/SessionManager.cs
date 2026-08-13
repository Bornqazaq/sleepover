using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Локальное табло катки для тестовых сцен без сети: список игроков и
    /// начисление очков по формуле «очки = число_игроков − место» (GDD 3.1).
    /// В сетевой катке его подменяет NetworkSessionManager — эта реализация
    /// молча уходит на второй план (см. SessionScoreboard).
    /// </summary>
    public sealed class SessionManager : MonoBehaviour, ISessionScoreboard
    {
        public static SessionManager Instance { get; private set; }

        public event Action ScoresChanged;

        private readonly List<SessionPlayer> players = new List<SessionPlayer>(8);

        public IReadOnlyList<SessionPlayer> Players => players;

        /// <summary>В локальном тесте живой игрок — первый заспавненный.</summary>
        public SessionPlayer LocalPlayer => players.Count > 0 ? players[0] : null;

        /// <summary>Без сети решает эта машина — оспаривать некому.</summary>
        public bool HasAuthority => true;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            SessionScoreboard.RegisterLocal(this);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            SessionScoreboard.Unregister(this);
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

        public void ReportResults(MinigameResults results)
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
