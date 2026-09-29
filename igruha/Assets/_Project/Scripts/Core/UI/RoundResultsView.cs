using System;
using System.Collections.Generic;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using TMPro;
using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>Общее представление раунда и серии. Не начисляет очки и не переключает сцены.</summary>
    public sealed class RoundResultsView : MonoBehaviour
    {
        [SerializeField] private TMP_Text eyebrow;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text subtitle;
        [SerializeField] private TMP_Text metricHeader;
        [SerializeField] private TMP_Text pointsHeader;
        [SerializeField] private TMP_Text totalHeader;
        [SerializeField] private TMP_Text footer;
        [SerializeField] private ResultRow[] rows = Array.Empty<ResultRow>();
        [SerializeField] private CharacterRoster roster;
        [SerializeField] private Sprite[] portraits = Array.Empty<Sprite>();

        private MinigameControllerBase game;
        private float transitionAt;
        private string destination;
        private string hostHint;
        private int lastSecond = -1;

        public void Configure(MinigameControllerBase source, float seconds, string next, bool hostOnly)
        {
            game = source;
            transitionAt = Time.time + seconds;
            destination = next;
            hostHint = hostOnly ? "Повтор запускает ведущий  ·  " : string.Empty;
            lastSecond = -1;
            RefreshFooter();
        }

        public void Show(MinigameResults results, IReadOnlyList<SessionPlayer> players)
        {
            int localId = SessionScoreboard.Current?.LocalPlayer?.Id ?? -1;
            int localIndex = results.IndexOf(localId);
            int winners = 0;
            foreach (var entry in results.Entries) if (entry.Place == 1) winners++;
            bool won = localIndex >= 0 && results.Entries[localIndex].Place == 1;
            eyebrow.text = game?.Definition != null ? game.Definition.DisplayName.ToUpperInvariant() + "  /  ИТОГИ" : "ИТОГИ РАУНДА";
            title.text = won ? "Первое место!" : "Раунд завершён";
            subtitle.text = results.AreTeams ? "Общий результат команды — общее место"
                : winners > 1 ? "Одинаковый результат — общее первое место" : "Места определены. Очки за раунд начислены";
            metricHeader.text = results.MetricTitle;
            pointsHeader.text = "ОЧКИ";
            totalHeader.gameObject.SetActive(results.CountsTowardSession);
            int row = 0;
            for (int place = 1; place <= results.Entries.Count; place++)
                foreach (var entry in results.Entries)
                {
                    if (entry.Place != place || row >= rows.Length) continue;
                    var player = FindPlayer(players, entry.PlayerId);
                    var detail = string.IsNullOrEmpty(entry.Detail.Value) ? new RoundResultDetail("—") : entry.Detail;
                    rows[row++].Show(place, NameOf(player, entry.PlayerId), PortraitOf(player), entry.PlayerId == localId,
                        detail, results.Awarded ? "+" + entry.Points : "—",
                        results.CountsTowardSession && results.Awarded ? entry.Total.ToString() : "");
                }
            ClearFrom(row);
        }

        public void ShowStandings(SessionStandings standings, IReadOnlyList<SessionPlayer> players, IReadOnlyList<int> champions)
        {
            int localId = SessionScoreboard.Current?.LocalPlayer?.Id ?? -1;
            eyebrow.text = "ПОЛНАЯ ИГРА  /  ФИНАЛ";
            title.text = Champion(standings, champions, localId) ? "Вы — чемпион катки!" : "Итоги катки";
            subtitle.text = $"Сыграно {standings.RoundsPlayed} {PartySeries.GamesWord(standings.RoundsPlayed)}"
                + (standings.IsTie ? "  ·  ничья за корону" : "  ·  до встречи в хабе");
            metricHeader.text = "ПОБЕДЫ";
            pointsHeader.text = "ВСЕГО";
            totalHeader.gameObject.SetActive(false);
            int row = 0;
            foreach (var entry in standings.Entries)
            {
                if (row >= rows.Length) break;
                var player = FindPlayer(players, entry.PlayerId);
                bool champion = Champion(standings, champions, entry.PlayerId);
                rows[row++].Show(entry.Place, NameOf(player, entry.PlayerId), PortraitOf(player), entry.PlayerId == localId,
                    new RoundResultDetail(entry.Wins.ToString(), champion ? "ЧЕМПИОН КАТКИ" : "Серия завершена"), entry.Score.ToString(), "");
            }
            ClearFrom(row);
        }

        private void ClearFrom(int index)
        {
            for (; index < rows.Length; index++) rows[index].Clear();
        }

        private void Update() => RefreshFooter();

        private void RefreshFooter()
        {
            if (string.IsNullOrEmpty(destination)) return;
            int second = Mathf.Max(0, Mathf.CeilToInt(transitionAt - Time.time));
            if (second == lastSecond) return;
            lastSecond = second;
            footer.text = second > 0 ? $"{hostHint}{destination} через {second} с" : "Переходим дальше…";
        }

        private Sprite PortraitOf(SessionPlayer player)
        {
            if (player == null) return null;
            int index = player.CharacterIndex;
            // В одиночном стенде CharacterIndex может быть не задан; префаб уже выбран спавнером.
            if (index < 0 && player.Avatar != null && roster != null)
                for (int i = 0; i < roster.Characters.Count; i++)
                {
                    var prefab = roster.Characters[i].Prefab;
                    if (prefab != null && player.Avatar.name.StartsWith(prefab.name, StringComparison.Ordinal)) { index = i; break; }
                }
            return index >= 0 && index < portraits.Length ? portraits[index] : null;
        }

        private static string NameOf(SessionPlayer player, int id) => player?.DisplayName ?? $"Игрок {id + 1}";
        private static SessionPlayer FindPlayer(IReadOnlyList<SessionPlayer> players, int id)
        {
            for (int i = 0; i < players.Count; i++) if (players[i].Id == id) return players[i];
            return null;
        }

        private static bool Champion(SessionStandings standings, IReadOnlyList<int> champions, int id)
        {
            if (champions == null || champions.Count == 0) return standings.IsLeader(id);
            for (int i = 0; i < champions.Count; i++) if (champions[i] == id) return true;
            return false;
        }
    }
}
