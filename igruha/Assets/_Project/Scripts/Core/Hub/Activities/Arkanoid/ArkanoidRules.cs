using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>Deterministic, allocation-free arcade simulation. Only the station authority steps it.</summary>
    public static class ArkanoidRules
    {
        public const int Columns = 8, Rows = 5, BrickCount = Columns * Rows, StartingLives = 3;
        public const ulong AllBricks = (1UL << BrickCount) - 1;
        public const float HalfWidth = 1f, Top = .63f, Bottom = -.64f;
        public const float BallRadius = .023f, PaddleY = -.50f, PaddleWidth = .30f, PaddleHeight = .045f;
        public const float PaddleLimit = HalfWidth - PaddleWidth * .5f, PaddleSpeed = 4f;
        public const float BrickWidth = .214f, BrickHeight = .064f;
        public const float MaxSpeed = 2.4f, SimulationStep = 1f / 240f;
        private const float InitialSpeed = .94f, LevelSpeed = .12f, HitSpeed = .012f;
        private const float ContactEpsilon = .0001f;

        public static Vector2 BrickCenter(int index) => new Vector2(-.84f + index % Columns * .24f,
            .18f + index / Columns * .084f);
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static float Speed(ArkanoidState state) => Mathf.Min(MaxSpeed,
            InitialSpeed + Mathf.Max(0, state.Level - 1) * LevelSpeed + state.Hits * HitSpeed);

        public static ArkanoidState NewGame(uint session, bool occupied)
        {
            var state = new ArkanoidState { Session = session, Phase = occupied ? ArkanoidPhase.Ready : ArkanoidPhase.Attract,
                Bricks = AllBricks, Lives = StartingLives, Level = 1 };
            AttachBall(ref state);
            return state;
        }

        public static void Launch(ref ArkanoidState state)
        {
            if (state.Phase == ArkanoidPhase.GameOver)
            {
                uint eventId = state.EventId, tick = state.Tick;
                state = NewGame(state.Session, true);
                state.EventId = eventId;
                state.Tick = tick;
            }
            if (state.Phase != ArkanoidPhase.Ready) return;
            state.Phase = ArkanoidPhase.Playing;
            state.Velocity = new Vector2(.26f, 1f).normalized * Speed(state);
            Emit(ref state, ArkanoidEvent.Launch);
        }

        public static void Step(ref ArkanoidState state, float target, float delta)
        {
            if (state.Phase == ArkanoidPhase.Attract || !IsFinite(target) || !IsFinite(delta) || delta <= 0) return;
            float remaining = Mathf.Min(delta, .1f);
            target = Mathf.Clamp(target, -PaddleLimit, PaddleLimit);
            while (remaining > .000001f)
            {
                float dt = Mathf.Min(remaining, SimulationStep);
                remaining -= dt;
                state.Paddle = Mathf.MoveTowards(state.Paddle, target, PaddleSpeed * dt);
                if (state.Phase == ArkanoidPhase.Ready) { AttachBall(ref state); continue; }
                if (state.Phase != ArkanoidPhase.Playing) continue;
                MoveBall(ref state, dt);
            }
        }

        private static void AttachBall(ref ArkanoidState state)
        {
            state.Ball = new Vector2(state.Paddle, PaddleY + PaddleHeight * .5f + BallRadius + ContactEpsilon);
            state.Velocity = Vector2.zero;
        }

        private static void MoveBall(ref ArkanoidState state, float dt)
        {
            Vector2 old = state.Ball;
            state.Ball += state.Velocity * dt;
            float side = HalfWidth - BallRadius;
            if (state.Ball.x < -side && state.Velocity.x < 0 || state.Ball.x > side && state.Velocity.x > 0)
            {
                state.Ball.x = Mathf.Clamp(state.Ball.x, -side, side);
                state.Velocity.x = -state.Velocity.x;
                Emit(ref state, ArkanoidEvent.Bounce);
            }
            if (state.Ball.y > Top - BallRadius && state.Velocity.y > 0)
            {
                state.Ball.y = Top - BallRadius;
                state.Velocity.y = -state.Velocity.y;
                Emit(ref state, ArkanoidEvent.Bounce);
            }
            float paddleTop = PaddleY + PaddleHeight * .5f + BallRadius;
            if (state.Velocity.y < 0 && old.y >= paddleTop && state.Ball.y <= paddleTop)
            {
                float crossing = Mathf.InverseLerp(old.y, state.Ball.y, paddleTop);
                float hitX = Mathf.Lerp(old.x, state.Ball.x, crossing);
                if (Mathf.Abs(hitX - state.Paddle) <= PaddleWidth * .5f + BallRadius)
                {
                    float offset = Mathf.Clamp((hitX - state.Paddle) / (PaddleWidth * .5f), -1, 1);
                    // Even the centre gets a small lateral component: no endless vertical corridor.
                    float angle = offset * 62f * Mathf.Deg2Rad;
                    if (Mathf.Abs(angle) < .09f) angle = state.Velocity.x < 0 ? -.09f : .09f;
                    state.Hits++;
                    state.Velocity = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * Speed(state);
                    state.Ball.y = paddleTop + ContactEpsilon;
                    Emit(ref state, ArkanoidEvent.Bounce);
                }
            }
            HitBrick(ref state, old);
            if (state.Ball.y >= Bottom - BallRadius) return;
            state.Lives--;
            state.Phase = state.Lives > 0 ? ArkanoidPhase.Ready : ArkanoidPhase.GameOver;
            AttachBall(ref state);
            Emit(ref state, state.Lives > 0 ? ArkanoidEvent.LifeLost : ArkanoidEvent.GameOver);
        }

        private static void HitBrick(ref ArkanoidState state, Vector2 old)
        {
            Vector2 extent = new Vector2(BrickWidth * .5f + BallRadius, BrickHeight * .5f + BallRadius);
            for (int i = 0; i < BrickCount; i++)
            {
                ulong bit = 1UL << i;
                if ((state.Bricks & bit) == 0) continue;
                Vector2 center = BrickCenter(i);
                Vector2 distance = state.Ball - center;
                if (Mathf.Abs(distance.x) > extent.x || Mathf.Abs(distance.y) > extent.y) continue;
                bool horizontal = old.x < center.x - extent.x || old.x > center.x + extent.x;
                if (horizontal)
                {
                    state.Ball.x = center.x + Mathf.Sign(distance.x) * (extent.x + ContactEpsilon);
                    state.Velocity.x = -state.Velocity.x;
                }
                else
                {
                    state.Ball.y = center.y + Mathf.Sign(distance.y) * (extent.y + ContactEpsilon);
                    state.Velocity.y = -state.Velocity.y;
                }
                state.Bricks &= ~bit;
                state.Score += (1 + i / Columns) * 10;
                state.Hits++;
                state.Velocity = state.Velocity.normalized * Speed(state);
                Emit(ref state, ArkanoidEvent.Brick);
                if (state.Bricks == 0)
                {
                    state.Level++;
                    state.Bricks = AllBricks;
                    state.Phase = ArkanoidPhase.Ready;
                    AttachBall(ref state);
                    Emit(ref state, ArkanoidEvent.Level);
                }
                return;
            }
        }

        private static void Emit(ref ArkanoidState state, ArkanoidEvent kind)
        {
            state.Event = kind;
            state.EventId++;
        }
    }
}
