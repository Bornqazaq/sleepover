using System.Text;
using TMPro;
using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Табло дорожки — подфаза 4.3. Показывает то же, что строка HUD, но
    /// в мире и по дорожке: номер стены и счёт этой пары.
    ///
    /// Зачем оно, если строка HUD уже есть. HUD показывает <b>свою</b> дорожку
    /// и только её — а половина удовольствия игры в том, чтобы видеть, как
    /// идут соседи (спека, раздел 7). Табло висит над каждой дорожкой, и счёт
    /// соседней пары читается из кадра, не отвлекая от стены.
    ///
    /// <b>Своего состояния у табло нет.</b> Оно читает уже посчитанное
    /// сервером: <see cref="HoleInWallTrack.Score"/> приезжает строкой
    /// состояния (<see cref="HoleInWallTrackNetState"/>), номер стены считает
    /// контроллер из общих часов. Ни своего RPC, ни своей переменной здесь
    /// нет и быть не должно — это витрина, а не источник истины.
    /// </summary>
    /// <remarks>
    /// Строки пересобираются только на смене значения, а не каждый кадр:
    /// табло четыре, и в кадровом цикле это была бы аллокация на ровном месте
    /// у каждого. Та же оговорка, что у <see cref="HoleInWallLocalHud"/>.
    /// </remarks>
    public sealed class HoleInWallScoreboard : MonoBehaviour
    {
        /// <summary>Что стоит на табло, пока раунд не идёт.</summary>
        private const string Dash = "—";

        [Tooltip("Контроллер игры: у него номер текущей стены и их общее число")]
        [SerializeField] private HoleInWallMinigame game;

        [Tooltip("Дорожка, чей счёт показывает это табло")]
        [SerializeField] private HoleInWallTrack track;

        [Tooltip("Верхняя строка: номер стены из скольких")]
        [SerializeField] private TextMeshPro wallLine;

        [Tooltip("Нижняя строка: счёт дорожки")]
        [SerializeField] private TextMeshPro scoreLine;

        private readonly StringBuilder builder = new StringBuilder(24);

        private int shownWall = int.MinValue;
        private int shownWallCount = int.MinValue;
        private int shownScore = int.MinValue;

        private void Update()
        {
            if (game == null || track == null)
            {
                return;
            }

            bool live = game.Phase == MinigamePhase.Round && track.Active;
            int wall = live ? game.CurrentWallNumber : 0;
            int wallCount = game.WallCount;
            int score = live ? track.Score : -1;

            if (wall == shownWall && wallCount == shownWallCount && score == shownScore)
            {
                return;
            }

            shownWall = wall;
            shownWallCount = wallCount;
            shownScore = score;

            ShowWall(live, wall, wallCount);
            ShowScore(live, score);
        }

        private void ShowWall(bool live, int wall, int wallCount)
        {
            if (wallLine == null)
            {
                return;
            }

            builder.Clear();
            builder.Append("СТЕНА ");

            if (live)
            {
                builder.Append(wall).Append('/').Append(wallCount);
            }
            else
            {
                builder.Append(Dash);
            }

            wallLine.text = builder.ToString();
        }

        private void ShowScore(bool live, int score)
        {
            if (scoreLine == null)
            {
                return;
            }

            // Счёт — одной цифрой во всю высоту табло: его читают боковым
            // зрением, стоя лицом к своей стене, и слово «очки» рядом с ним
            // отняло бы у цифры ровно ту высоту, ради которой табло и висит.
            scoreLine.text = live ? score.ToString() : Dash;
        }
    }
}
