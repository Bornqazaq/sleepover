using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Локальное табло катки для тестовых сцен без сети: список игроков,
    /// начисление очков по формуле <see cref="SessionScoring"/> и журнал
    /// раундов. В сетевой катке его подменяет NetworkSessionManager — эта
    /// реализация молча уходит на второй план (см. SessionScoreboard).
    ///
    /// Журнал и чемпионы лежат в <see cref="LocalPartyProfile"/>: табло
    /// живёт в сцене и пересоздаётся на каждой мини-игре, а катка — нет.
    /// </summary>
    public sealed class SessionManager : MonoBehaviour, ISessionScoreboard, ISessionScoreReset
    {
        public static SessionManager Instance { get; private set; }

        public event Action ScoresChanged;

        private readonly List<SessionPlayer> players = new List<SessionPlayer>(8);
        private readonly SpecialRoleHistory specialRoles = new SpecialRoleHistory();
        private readonly SessionStandings standings = new SessionStandings();

        public IReadOnlyList<SessionPlayer> Players => players;

        /// <summary>В локальном тесте живой игрок — первый заспавненный.</summary>
        public SessionPlayer LocalPlayer => players.Count > 0 ? players[0] : null;

        /// <summary>Без сети решает эта машина — оспаривать некому.</summary>
        public bool HasAuthority => true;

        public IReadOnlyList<SessionRoundRecord> History => LocalPartyProfile.History;

        public int RoundsPlayed => LocalPartyProfile.RoundsPlayed;

        public IReadOnlyList<int> Champions => LocalPartyProfile.Champions;

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
            if (results == null)
            {
                return;
            }

            int playerCount = SessionScoring.PlayerCountFor(results, players.Count);
            int round = LocalPartyProfile.RoundsPlayed + 1;
            bool anyAwarded = false;
            IReadOnlyList<MinigameResults.PlayerResult> entries = results.Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                SessionPlayer player = FindPlayer(entries[i].PlayerId);
                if (player == null)
                {
                    continue;
                }

                int points = SessionScoring.PointsFor(entries[i].Place, playerCount);
                player.Score += points;
                results.SetAward(i, points, player.Score);
                LocalPartyProfile.History.Add(
                    new SessionRoundRecord(round, results.GameKey, player.Id, entries[i].Place, points));
                anyAwarded = true;

                Debug.Log($"⭐ {player.DisplayName}: место {entries[i].Place} из {playerCount}, +{points}, всего {player.Score}");
            }

            if (anyAwarded)
            {
                LocalPartyProfile.RoundsPlayed = round;
            }

            foreach (var player in players) LocalPartyProfile.Save(player);
            ScoresChanged?.Invoke();
        }

        /// <summary>Новая серия: счёт, журнал и чемпионы — с чистого листа.</summary>
        public void ResetScores()
        {
            foreach (var player in players) { player.Score = 0; LocalPartyProfile.Save(player); }
            LocalPartyProfile.History.Clear();
            LocalPartyProfile.Champions.Clear();
            LocalPartyProfile.RoundsPlayed = 0;
            ScoresChanged?.Invoke();
        }

        public bool IsChampion(int playerId) => LocalPartyProfile.Champions.Contains(playerId);

        public void CompleteSeries()
        {
            standings.Rebuild(players, LocalPartyProfile.History);
            LocalPartyProfile.Champions.Clear();
            IReadOnlyList<SessionStandings.Entry> table = standings.Entries;
            for (int i = 0; i < table.Count; i++)
            {
                if (standings.IsLeader(table[i].PlayerId))
                {
                    LocalPartyProfile.Champions.Add(table[i].PlayerId);
                }
            }

            ScoresChanged?.Invoke();
        }

        // ========== ОСОБЫЕ РОЛИ ==========

        public bool HasPlayedSpecialRole(int playerId, string roleKey) =>
            specialRoles.HasPlayed(roleKey, playerId);

        public void MarkSpecialRole(int playerId, string roleKey)
        {
            if (Igruha.Core.Minigame.MinigameControllerBase.Current?.IsPractice != true) specialRoles.Mark(roleKey, playerId);
        }

        /// <summary>
        /// История переживает смену мини-игр: ClearPlayers чистит ростер сцены,
        /// а память о ролях остаётся на всю катку, как и счёт.
        /// </summary>
        public int PickSpecialRole(string roleKey) => specialRoles.Pick(roleKey, players, Igruha.Core.Minigame.MinigameControllerBase.Current?.IsPractice != true);
    }
}
