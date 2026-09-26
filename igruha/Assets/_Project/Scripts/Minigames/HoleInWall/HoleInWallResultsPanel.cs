using Igruha.Core.Minigame;
using Igruha.Core.Session;
using TMPro;
using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>Подпись подиума. Карточкой итогов владеет общий RoundResultsView.</summary>
    public sealed class HoleInWallResultsPanel : MonoBehaviour
    {
        [SerializeField] private HoleInWallMinigame game;
        [SerializeField] private TMP_Text platformCaption;

        private void OnEnable()
        {
            if (game == null) return;
            game.ResultsReported += Show;
            game.FinalStandingsReported += ShowStandings;
        }
        private void OnDisable()
        {
            if (game == null) return;
            game.ResultsReported -= Show;
            game.FinalStandingsReported -= ShowStandings;
        }
        private void Show(MinigameResults results)
        {
            if (platformCaption == null) return;
            platformCaption.text = "ПОБЕДИТЕЛИ";
            foreach (var entry in results.Entries)
            {
                var track = game.TrackOf(entry.PlayerId);
                if (entry.Place != 1 || track == null) continue;
                platformCaption.text = $"ПОБЕДИТЕЛИ\n<size=55%>ДОРОЖКА {track.Index + 1:00}</size>";
                break;
            }
        }
        private void ShowStandings(SessionStandings standings)
        {
            if (platformCaption != null) platformCaption.text = standings.IsTie ? "НИЧЬЯ\n<size=55%>ЗА КОРОНУ</size>" : "ИТОГИ\n<size=55%>КАТКИ</size>";
        }
    }
}
