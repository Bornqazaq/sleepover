using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Core.UI;
using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>Ball, paddles, local timing hint and spatial ticks all follow the same visible flight.</summary>
    public sealed class PingPongPresentation : MonoBehaviour
    {
        public const string PaddleSound = "pingpong.paddle";
        public const string BounceSound = "pingpong.bounce";
        [SerializeField] private PingPongTable table;
        [SerializeField] private PingPongCamera localCamera;
        [SerializeField] private Transform ball;
        [SerializeField] private Transform leftPaddle;
        [SerializeField] private Transform rightPaddle;
        [SerializeField] private Renderer leftCue;
        [SerializeField] private Renderer rightCue;
        [SerializeField] private MinigameAudioPlayer audioPlayer;
        private MaterialPropertyBlock cueProperties;
        private GUIStyle titleStyle, hintStyle;
        private uint soundedHit, soundedBounce;
        private double feedbackUntil;
        private string feedback;
        private bool successfulFeedback;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly Color ReadyColor = new Color(.50f, .92f, .60f);
        private static readonly Color WaitColor = new Color(.95f, .70f, .34f);
        private static readonly Color BadColor = new Color(.95f, .43f, .34f);
        private const float FeedbackSeconds = .65f;
        private const float SwingSeconds = .24f;
        private const float AudioCatchup = .2f;

        private void Awake() => cueProperties = new MaterialPropertyBlock();

        public void ShowAttempt(bool success, bool early)
        {
            feedback = success ? "ЕСТЬ!" : early ? "РАНОВАТО" : "ПОЗДНО";
            successfulFeedback = success;
            feedbackUntil = Time.unscaledTimeAsDouble + FeedbackSeconds;
        }

        private void LateUpdate()
        {
            PingPongState state = table.DisplayState;
            double now = NetworkClock.Now;
            PingPongFlight flight = PingPongRules.VisibleFlight(state, now);
            bool playing = state.Phase != PingPongPhase.Idle && flight.Valid;
            if (playing)
            {
                Vector3 p = PingPongRules.Position(flight, now);
                bool visible = p.y >= PingPongRules.BallRadius && now >= flight.StartsAt;
                ball.gameObject.SetActive(visible);
                ball.localPosition = p;
            }
            else
            {
                ball.gameObject.SetActive(true);
                ball.localPosition = new Vector3(-.54f, PingPongRules.TableHeight + PingPongRules.BallRadius, .38f);
            }
            Paddle(leftPaddle, 0, flight, now, playing);
            Paddle(rightPaddle, 1, flight, now, playing);
            Cue(leftCue, 0, flight, now, state.Phase);
            Cue(rightCue, 1, flight, now, state.Phase);
            if (playing) Sounds(flight, now);
        }

        private static void Paddle(Transform paddle, byte side, PingPongFlight flight, double now, bool playing)
        {
            if (!playing)
            {
                paddle.localPosition = new Vector3(side == 0 ? -.83f : .83f, .91f, side == 0 ? .36f : -.39f);
                paddle.localRotation = Quaternion.Euler(0, 0, -90);
                return;
            }
            Vector3 point = flight.Target == side ? flight.To : flight.From;
            float sinceStrike = (float)(now - flight.StartsAt);
            float swing = flight.Target != side && sinceStrike >= 0 && sinceStrike < SwingSeconds
                ? Mathf.Sin(sinceStrike / SwingSeconds * Mathf.PI) : 0;
            float sign = side == 0 ? -1 : 1;
            point.x += sign * (.09f - swing * .15f);
            paddle.localPosition = Vector3.Lerp(paddle.localPosition, point, 1 - Mathf.Exp(-22 * Time.deltaTime));
            paddle.localRotation = Quaternion.Euler(0, sign * (10 + swing * 32), sign * -12);
        }

        private void Cue(Renderer cue, byte side, PingPongFlight flight, double now, PingPongPhase phase)
        {
            bool visible = phase == PingPongPhase.Playing && flight.Target == side;
            cue.enabled = visible;
            if (!visible) return;
            bool ready = PingPongRules.InWindow(flight, now);
            Color color = ready ? ReadyColor : WaitColor;
            cueProperties.SetColor(BaseColor, color);
            cueProperties.SetColor(EmissionColor, color * (ready ? .6f : .15f));
            cue.SetPropertyBlock(cueProperties);
            Transform marker = cue.transform;
            marker.localPosition = new Vector3(side == 0 ? -1.23f : 1.23f, .882f, flight.To.z);
            float size = ready ? .24f : .15f;
            marker.localScale = new Vector3(size, .004f, size);
        }

        private void Sounds(PingPongFlight flight, double now)
        {
            if (audioPlayer == null) return;
            if (soundedHit != flight.Id && now >= flight.StartsAt)
            {
                soundedHit = flight.Id;
                if (now < flight.StartsAt + AudioCatchup)
                    audioPlayer.PlayAt(PaddleSound, transform.TransformPoint(flight.From));
            }
            double bounceAt = flight.StartsAt + flight.Duration * PingPongRules.BounceFraction;
            if (soundedBounce != flight.Id && now >= bounceAt)
            {
                soundedBounce = flight.Id;
                if (now < bounceAt + AudioCatchup)
                    audioPlayer.PlayAt(BounceSound, transform.TransformPoint(PingPongRules.Position(flight, bounceAt)));
            }
        }

        private void OnGUI()
        {
            int side = table.LocalSide;
            if (side < 0 || (localCamera != null && !localCamera.IsActive) ||
                (PauseScreen.Current != null && PauseScreen.Current.IsPaused)) return;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter,
                    fontSize = 22, fontStyle = FontStyle.Bold };
                hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
                hintStyle.normal.textColor = new Color(.91f, .88f, .80f);
            }
            double now = NetworkClock.Now;
            PingPongState state = table.DisplayState;
            PingPongFlight flight = PingPongRules.VisibleFlight(state, now);
            bool incoming = state.Phase == PingPongPhase.Playing && flight.Target == side;
            bool ready = incoming && PingPongRules.InWindow(flight, now);
            bool hasFeedback = Time.unscaledTimeAsDouble < feedbackUntil;
            string caption = hasFeedback ? feedback : state.Phase == PingPongPhase.Missed ? "НОВАЯ ПОДАЧА…" :
                now < flight.StartsAt ? "ГОТОВЬСЯ" : ready ? "ЛКМ — ОТБЕЙ!" : incoming ? "ЛОВИ МОМЕНТ" : "ОБРАТНЫЙ МЯЧ";
            Color accent = hasFeedback ? successfulFeedback ? ReadyColor : BadColor : ready ? ReadyColor : WaitColor;
            const float width = 390, height = 112;
            float scale = Mathf.Clamp(Screen.height / 900f, .8f, 1.4f);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float x = (Screen.width / scale - width) * .5f;
            float y = Screen.height / scale - height - 26;
            Draw(new Rect(x, y, width, height), new Color(.06f, .10f, .10f, .91f));
            titleStyle.normal.textColor = accent;
            GUI.Label(new Rect(x, y + 12, width, 29), caption, titleStyle);
            Rect bar = new Rect(x + 34, y + 51, width - 68, 9);
            Draw(bar, new Color(.20f, .25f, .23f));
            if (incoming)
            {
                float windowStart = PingPongRules.TimingWindowStart(flight);
                Draw(new Rect(bar.x + bar.width * windowStart, bar.y, bar.width * (1 - windowStart), bar.height), ReadyColor * .55f);
                float progress = PingPongRules.TimingProgress(flight, now);
                Draw(new Rect(bar.x + bar.width * progress - 2.5f, bar.y - 3, 5, 15), accent);
            }
            GUI.Label(new Rect(x, y + 73, width, 23), "ЛКМ — удар   ·   Зажми E — отойти", hintStyle);
            GUI.matrix = old;
        }

        private static void Draw(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
