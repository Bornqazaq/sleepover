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
        private const string Dash = "ОЖИДАНИЕ";

        [Tooltip("Контроллер игры: у него номер текущей стены и их общее число")]
        [SerializeField] private HoleInWallMinigame game;

        [Tooltip("Дорожка, чей счёт показывает это табло")]
        [SerializeField] private HoleInWallTrack track;

        [Tooltip("Нижняя строка: номер стены из скольких")]
        [SerializeField] private TextMeshPro wallLine;

        [Tooltip("Крупное число: счёт дорожки")]
        [SerializeField] private TextMeshPro scoreLine;

        [SerializeField] private Renderer[] progressLights = System.Array.Empty<Renderer>();
        [SerializeField] private Color accentColor = Color.cyan;
        private MaterialPropertyBlock lightProperties;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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

            bool live = game.Phase.IsGameplay() && track.Active;
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
            ShowProgress(live, wall);
        }

        private void ShowProgress(bool live, int wall)
        {
            if (lightProperties == null) lightProperties = new MaterialPropertyBlock();
            for (int i = 0; i < progressLights.Length; i++)
            {
                if (progressLights[i] == null) continue;
                Color color = !live || i >= wall ? new Color(.13f,.19f,.25f) :
                    i == wall - 1 ? accentColor : new Color(.75f,.84f,.87f);
                lightProperties.SetColor(BaseColorId, color);
                progressLights[i].SetPropertyBlock(lightProperties);
            }
        }

        private void ShowWall(bool live, int wall, int wallCount)
        {
            if (wallLine == null)
            {
                return;
            }

            builder.Clear();
            if (live)
            {
                builder.Append("СТЕНА ").Append(wall).Append(" / ").Append(wallCount);
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

            // Два разряда удерживают ширину счёта при смене значения.
            scoreLine.text = live ? score.ToString("00") : "00";
        }
    }
}
