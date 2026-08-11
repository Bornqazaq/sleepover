using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;
using Igruha.Core.Minigame;
using Igruha.Core.Session;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Общий HUD мини-игры: таймер раунда + панель результатов.
    /// Текст таймера обновляется только при смене секунды (без аллокаций в кадре).
    /// </summary>
    public sealed class RoundHud : MonoBehaviour
    {
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private GameObject resultsPanel;
        [SerializeField] private TMP_Text resultsText;

        private RoundTimer timer;
        private int lastShownSeconds = -1;

        public void Bind(RoundTimer roundTimer)
        {
            timer = roundTimer;
            if (resultsPanel != null)
            {
                resultsPanel.SetActive(false);
            }
        }

        private void Update()
        {
            if (timer == null || timerText == null)
            {
                return;
            }

            int seconds = Mathf.CeilToInt(timer.Remaining);
            if (seconds == lastShownSeconds)
            {
                return;
            }

            lastShownSeconds = seconds;
            int minutes = seconds / 60;
            timerText.text = $"{minutes}:{seconds % 60:00}";
        }

        public void ShowResults(MinigameResults results, IReadOnlyList<SessionPlayer> players)
        {
            if (resultsPanel == null || resultsText == null)
            {
                return;
            }

            var sb = new StringBuilder("Результаты раунда:\n");
            IReadOnlyList<MinigameResults.PlayerResult> entries = results.Entries;
            for (int place = 1; place <= entries.Count; place++)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Place != place)
                    {
                        continue;
                    }

                    string playerName = FindName(players, entries[i].PlayerId);
                    sb.AppendLine($"{place} место — {playerName}");
                }
            }

            resultsText.text = sb.ToString();
            resultsPanel.SetActive(true);
        }

        private static string FindName(IReadOnlyList<SessionPlayer> players, int playerId)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Id == playerId)
                {
                    return players[i].DisplayName;
                }
            }

            return $"Игрок {playerId + 1}";
        }
    }
}
