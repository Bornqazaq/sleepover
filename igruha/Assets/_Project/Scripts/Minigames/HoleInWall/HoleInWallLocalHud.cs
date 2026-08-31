using System.Text;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Core.UI;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Строка состояния своей дорожки: какая стена едет, сколько очков набрано
    /// и сколько осталось до удара.
    ///
    /// Без неё игрок не знает ни своего счёта, ни номера стены. Тот же класс
    /// поломок уже ловили трижды — у «Верю / не верю» 25.08 и 28.08, у «Рейса
    /// на память» 31.08: <c>RoundHud.statusText</c> был пуст в сцене,
    /// и <c>ShowStatus</c> молча ничего не делал.
    /// </summary>
    /// <remarks>
    /// Строка пересобирается только на смене значения, а не каждый кадр:
    /// в кадровом цикле это была бы аллокация на ровном месте. Меняется
    /// не чаще раза в секунду — по округлённому остатку до удара.
    /// </remarks>
    public sealed class HoleInWallLocalHud : MonoBehaviour
    {
        [SerializeField] private HoleInWallMinigame game;
        [SerializeField] private RoundHud hud;

        private readonly StringBuilder builder = new StringBuilder(96);
        private int shownWall = -1;
        private int shownScore = -1;
        private int shownSeconds = -1;
        private bool shownSolo;
        private bool statusVisible;

        private void Update()
        {
            if (game == null || hud == null)
            {
                return;
            }

            if (game.Phase != MinigamePhase.Round)
            {
                HideStatus();
                return;
            }

            HoleInWallTrack track = LocalTrack();
            if (track == null)
            {
                HideStatus();
                return;
            }

            int wall = game.CurrentWallNumber;
            int seconds = Mathf.CeilToInt(game.SecondsToHit);

            if (statusVisible && wall == shownWall && track.Score == shownScore &&
                seconds == shownSeconds && track.Solo == shownSolo)
            {
                return;
            }

            shownWall = wall;
            shownScore = track.Score;
            shownSeconds = seconds;
            shownSolo = track.Solo;
            statusVisible = true;

            builder.Clear();
            builder.Append("Стена ").Append(wall).Append('/').Append(game.WallCount);
            builder.Append("   ·   Очки: ").Append(track.Score);
            builder.Append("   ·   Удар через ").Append(seconds).Append(" с");

            if (track.Solo)
            {
                builder.Append("   ·   Ты один — стена едет быстрее");
            }

            hud.ShowStatus(builder.ToString());
        }

        private void HideStatus()
        {
            if (!statusVisible)
            {
                return;
            }

            statusVisible = false;
            shownWall = -1;
            shownScore = -1;
            shownSeconds = -1;
            hud.HideStatus();
        }

        /// <summary>Дорожка этой машины. Пусто — своего персонажа в раунде нет.</summary>
        private HoleInWallTrack LocalTrack()
        {
            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            return local != null ? game.TrackOf(local.Id) : null;
        }
    }
}
