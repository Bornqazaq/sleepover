using Igruha.Core.Audio;
using TMPro;
using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>The same world-space screen is visible to the player and every bystander.</summary>
    public sealed class ArkanoidPresentation : MonoBehaviour
    {
        public const float PixelsPerUnit = 260f;
        [SerializeField] private ArkanoidStation station;
        [SerializeField] private RectTransform paddle, ball;
        [SerializeField] private GameObject[] bricks;
        [SerializeField] private TMP_Text score, lives, level, title, button, hint;
        [SerializeField] private GameObject panel;
        [SerializeField] private MinigameAudioPlayer audioPlayer;
        private ArkanoidState previous;
        private bool initialized, wasOccupied;
        private float receivedAt;
        private static readonly string[] SoundSlots = { null, "arcade.launch", "arcade.bounce", "arcade.brick",
            "arcade.life", "arcade.level", "arcade.over" };

        private void Update()
        {
            ArkanoidState state = station.State;
            bool occupied = station.Occupant != HubActivityStation.NoOccupant;
            if (!initialized || state.Tick != previous.Tick || state.Session != previous.Session) receivedAt = Time.unscaledTime;
            if (!initialized || state.Score != previous.Score) score.SetText("СЧЁТ {0:00000}", state.Score);
            if (!initialized || state.Lives != previous.Lives) lives.SetText("ЖИЗНИ {0}", state.Lives);
            if (!initialized || state.Level != previous.Level) level.SetText("LV {0}", state.Level);
            if (!initialized || state.Bricks != previous.Bricks)
                for (int i = 0; i < bricks.Length; i++) bricks[i].SetActive((state.Bricks & (1UL << i)) != 0);
            if (!initialized || state.Phase != previous.Phase || occupied != wasOccupied || state.Level != previous.Level)
            {
                panel.SetActive(state.Phase != ArkanoidPhase.Playing);
                if (!occupied)
                {
                    title.text = "АРКАНОИД"; button.text = "ЗАЖМИ E"; hint.text = "РАЗБЕЙ ВСЕ КИРПИЧИ";
                }
                else if (state.Phase == ArkanoidPhase.GameOver)
                {
                    title.text = "ИГРА ОКОНЧЕНА"; button.text = "ЕЩЁ РАЗ"; hint.text = "ЛКМ — НАЧАТЬ";
                }
                else
                {
                    title.text = state.Level > 1 ? "НОВЫЙ УРОВЕНЬ" : "АРКАНОИД";
                    button.text = state.Score == 0 && state.Lives == 3 ? "ИГРАТЬ" : "ПОДАТЬ МЯЧ";
                    hint.text = "ЛКМ — ЗАПУСК";
                }
            }
            float shownPaddle = station.IsLocal ? station.LocalPaddle : state.Paddle;
            paddle.anchoredPosition = new Vector2(shownPaddle, ArkanoidRules.PaddleY) * PixelsPerUnit;
            Vector2 shownBall = state.Ball;
            if (state.Phase == ArkanoidPhase.Ready) shownBall.x = shownPaddle;
            else if (state.Phase == ArkanoidPhase.Playing)
            {
                // Bound prediction to one snapshot interval; never simulate a client-side hit.
                shownBall += state.Velocity * Mathf.Min(Time.unscaledTime - receivedAt, 1f / 30f);
                shownBall.x = Mathf.Clamp(shownBall.x, -ArkanoidRules.HalfWidth + ArkanoidRules.BallRadius,
                    ArkanoidRules.HalfWidth - ArkanoidRules.BallRadius);
                shownBall.y = Mathf.Min(shownBall.y, ArkanoidRules.Top - ArkanoidRules.BallRadius);
            }
            ball.anchoredPosition = shownBall * PixelsPerUnit;
            if (initialized && state.Session == previous.Session && state.EventId != previous.EventId &&
                state.Event != ArkanoidEvent.None && audioPlayer != null)
                audioPlayer.PlayAt(SoundSlots[(int)state.Event], transform.position);
            previous = state;
            wasOccupied = occupied;
            initialized = true;
        }
    }
}
