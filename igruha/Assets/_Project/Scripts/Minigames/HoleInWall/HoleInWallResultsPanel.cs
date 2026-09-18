using System;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>Представление готовых мест сервера; правила и перезапуск остаются в Core.</summary>
    public sealed class HoleInWallResultsPanel : MonoBehaviour
    {
        [Serializable]
        private sealed class Row
        {
            public GameObject root;
            public TMP_Text place;
            public TMP_Text playerName;
            public TMP_Text score;
            public TMP_Text lane;
            public Image background;
            public Image accent;
            public Image portrait;
        }

        [SerializeField] private HoleInWallMinigame game;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text subtitle;
        [SerializeField] private TMP_Text footer;
        [SerializeField] private TMP_Text platformCaption;
        [SerializeField] private CharacterRoster roster;
        [SerializeField] private Sprite[] portraits = Array.Empty<Sprite>();
        [SerializeField] private GameObject roundPlates;
        [SerializeField] private Row[] rows = Array.Empty<Row>();
        [SerializeField] private float displaySeconds = 12f;

        private static readonly Color Winner = new Color(.98f, .88f, .61f);
        private static readonly Color Paper = new Color(.88f, .94f, .89f);
        private float returnAt;
        private int lastSecond = -1;
        private bool shown;
        private bool waitingForHost;

        private void OnEnable()
        {
            game.ResultsReported += Show;
            game.FinalStandingsReported += ShowStandings;
        }

        private void OnDisable()
        {
            game.ResultsReported -= Show;
            game.FinalStandingsReported -= ShowStandings;
        }

        private void Show(MinigameResults results)
        {
            if (rows.Length == 0 || rows[0].root == null) return;
            if (roundPlates != null) roundPlates.SetActive(false);
            var players = SessionScoreboard.Current?.Players;
            int localId = SessionScoreboard.Current?.LocalPlayer?.Id ?? -1;
            int rowIndex = 0;
            bool localWinner = false;
            int firstCount = 0;
            for (int place = 1; place <= results.Entries.Count; place++)
            {
                foreach (var entry in results.Entries)
                {
                    if (entry.Place != place || rowIndex >= rows.Length) continue;
                    Row row = rows[rowIndex++];
                    string playerName = $"Игрок {entry.PlayerId + 1}";
                    if (players != null)
                        foreach (var player in players)
                            if (player.Id == entry.PlayerId) { playerName = player.DisplayName; break; }

                    SessionPlayer participant = null;
                    if (players != null) foreach (var player in players)
                        if (player.Id == entry.PlayerId) { participant = player; break; }
                    int character = CharacterOf(participant);
                    row.portrait.sprite = character >= 0 && character < portraits.Length ? portraits[character] : null;
                    row.portrait.enabled = row.portrait.sprite != null;
                    string characterName = character >= 0 ? roster.Characters[character].DisplayName : string.Empty;
                    bool local = entry.PlayerId == localId;
                    var track = game.TrackOf(entry.PlayerId);
                    row.place.text = place.ToString("00");
                    row.playerName.text = playerName + (local ? "  ·  вы" : string.Empty);
                    row.lane.text = characterName + (track != null ? $"  ·  ДОРОЖКА {track.Index + 1:00}" : "  ·  ВЫШЕЛ ИЗ РАУНДА")
                        + (results.Awarded ? $"  ·  +{entry.Points} ОЧК.  ·  ВСЕГО {entry.Total}" : string.Empty);
                    // Замороженный счёт вышедшего не передаётся клиентам. Не подменяем его нулём.
                    row.score.text = track != null ? $"<b>{track.Score}</b><size=65%> / {game.WallCount}</size>" : "—";
                    row.background.color = place == 1 ? Winner : Paper;
                    row.accent.color = track != null ? HoleInWallPalette.LaneAccent(track.Index) : Color.gray;
                    row.root.SetActive(true);
                    if (place == 1) { firstCount++; localWinner |= local; }
                }
            }
            for (; rowIndex < rows.Length; rowIndex++) rows[rowIndex].root.SetActive(false);

            foreach (var entry in results.Entries)
                if (entry.Place == 1 && game.TrackOf(entry.PlayerId) != null)
                { platformCaption.text = $"ПОБЕДИТЕЛИ\n<size=55%>ДОРОЖКА {game.TrackOf(entry.PlayerId).Index + 1:00}</size>"; break; }
            title.text = localWinner ? "Первое место!" : "Раунд завершён";
            subtitle.text = firstCount > 1 ? "Одинаковый счёт — общее место" : "Каждая пройденная стена — одно очко";
            waitingForHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer;
            returnAt = Time.time + displaySeconds;
            lastSecond = -1;
            shown = true;
            UpdateFooter();
        }

        /// <summary>
        /// Таблица катки после последней игры серии — теми же строками:
        /// место по сумме, имя, очки, отметка чемпиона.
        /// </summary>
        private void ShowStandings(SessionStandings standings)
        {
            if (rows.Length == 0 || rows[0].root == null || standings == null) return;
            var session = SessionScoreboard.Current;
            int localId = session?.LocalPlayer?.Id ?? -1;
            var champions = session?.Champions;
            int rowIndex = 0;
            bool localChampion = false;
            foreach (var entry in standings.Entries)
            {
                if (rowIndex >= rows.Length) break;
                Row row = rows[rowIndex++];
                SessionPlayer participant = session?.FindPlayer(entry.PlayerId);
                string playerName = participant?.DisplayName ?? $"Игрок {entry.PlayerId + 1}";
                bool champion = champions != null && champions.Count > 0 ? ChampionListed(champions, entry.PlayerId) : standings.IsLeader(entry.PlayerId);
                bool local = entry.PlayerId == localId;
                localChampion |= champion && local;
                int character = CharacterOf(participant);
                row.portrait.sprite = character >= 0 && character < portraits.Length ? portraits[character] : null;
                row.portrait.enabled = row.portrait.sprite != null;
                string characterName = character >= 0 ? roster.Characters[character].DisplayName : string.Empty;
                row.place.text = entry.Place.ToString("00");
                row.playerName.text = playerName + (local ? "  ·  вы" : string.Empty);
                row.lane.text = characterName + (champion ? "  ·  ЧЕМПИОН КАТКИ" : $"  ·  ПОБЕД: {entry.Wins}");
                row.score.text = $"<b>{entry.Score}</b><size=65%> очк.</size>";
                row.background.color = champion ? Winner : Paper;
                row.accent.color = champion ? Winner : Color.gray;
                row.root.SetActive(true);
            }
            for (; rowIndex < rows.Length; rowIndex++) rows[rowIndex].root.SetActive(false);

            platformCaption.text = standings.IsTie ? "НИЧЬЯ\n<size=55%>ЗА КОРОНУ</size>" : "ИТОГИ\n<size=55%>КАТКИ</size>";
            title.text = localChampion ? "Ты — чемпион катки!" : "Итоги катки";
            subtitle.text = $"Сыграно {standings.RoundsPlayed} {PartySeries.GamesWord(standings.RoundsPlayed)}" +
                            (standings.IsTie ? "  ·  первое место делят" : string.Empty);
            waitingForHost = false;
            returnAt = Time.time + game.FinalStandingsSeconds;
            lastSecond = -1;
            shown = true;
            UpdateFooter();
        }

        private static bool ChampionListed(System.Collections.Generic.IReadOnlyList<int> champions, int playerId)
        {
            for (int i = 0; i < champions.Count; i++) if (champions[i] == playerId) return true;
            return false;
        }

        private int CharacterOf(SessionPlayer player)
        {
            if (player == null || roster == null) return -1;
            string key = player.Avatar != null ? CutoutShapes.KeyOf(player.Avatar.gameObject) : null;
            for (int i = 0; i < roster.Characters.Count; i++)
                if (key != null && CutoutShapes.KeyOf(roster.Characters[i].Prefab) == key) return i;
            return player.CharacterIndex >= 0 && player.CharacterIndex < roster.Characters.Count ? player.CharacterIndex : -1;
        }

        private void Update()
        {
            if (shown) UpdateFooter();
        }

        private void UpdateFooter()
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(returnAt - Time.time));
            if (lastSecond == seconds) return;
            lastSecond = seconds;
            string destination = PartySeries.Active && !PartySeries.Completed ? "Следующий этап" : "Возврат в хаб";
            footer.text = waitingForHost && !PartySeries.Active
                ? $"Повтор запускает ведущий  ·  {destination} через {seconds} с"
                : $"{destination} через {seconds} с";
        }
    }
}
