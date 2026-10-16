using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>Server-only fixed-step shooter. Clients render this state and send intentions.</summary>
    public static class StarshipRules
    {
        public const int Columns = 6, Rows = 3, EnemyCount = Columns * Rows, ProjectileCount = 3;
        public const uint FullFormation = (1U << EnemyCount) - 1;
        public const float ShipY = -.53f, ShipLimit = .91f, ShipSpeed = 2.6f;
        public const float EnemyWidth = .115f, EnemyHeight = .075f, ShotSpeed = 2.5f;
        private const float Top = .61f, Bottom = -.64f, FormationLimit = .22f, Descent = .035f;
        private const float ShotInterval = .22f, ShipHalfWidth = .055f, ShipHalfHeight = .035f;
        private const float ProjectileRadius = .009f, ShieldSeconds = 1.5f, MaximumStep = .05f;

        public static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public static float FormationSpeed(int wave) => Mathf.Min(.12f + .022f * (wave - 1), .32f);
        public static float BombSpeed(int wave) => Mathf.Min(.55f + .04f * (wave - 1), .95f);
        public static Vector2 EnemyPosition(StarshipState s, int i) => new Vector2(
            (i % Columns - (Columns - 1) * .5f) * .27f + s.FormationX, .4f - i / Columns * .145f + s.FormationY);

        public static StarshipState NewGame(uint session, bool occupied) => new StarshipState
        {
            Session = session, Phase = occupied ? StarshipPhase.Ready : StarshipPhase.Attract,
            Lives = 3, Wave = 1, Enemies = FullFormation, Direction = 1, BombCooldown = 1.2f
        };
        public static void Start(ref StarshipState s)
        {
            if (s.Phase == StarshipPhase.GameOver) s = NewGame(s.Session + 1, true);
            if (s.Phase != StarshipPhase.Ready) return;
            s.Phase = StarshipPhase.Playing; s.Shield = ShieldSeconds;
            Emit(ref s, StarshipEvent.Start);
        }
        public static void Step(ref StarshipState s, float target, bool firing, float dt)
        {
            if (!IsFinite(dt) || dt <= 0 || !IsFinite(target)) return;
            dt = Mathf.Min(dt, MaximumStep);
            if (s.Phase != StarshipPhase.Playing && s.Phase != StarshipPhase.Ready) return;
            s.Ship = Mathf.MoveTowards(s.Ship, Mathf.Clamp(target, -ShipLimit, ShipLimit), ShipSpeed * dt);
            if (s.Phase != StarshipPhase.Playing) return;
            s.Shield = Mathf.Max(0, s.Shield - dt);
            s.FireCooldown = Mathf.Max(0, s.FireCooldown - dt);
            if (firing && s.FireCooldown <= 0)
                for (int i = 0; i < ProjectileCount; i++)
                    if ((s.Shots & (1 << i)) == 0)
                    {
                        s.Shots |= (byte)(1 << i); s.SetProjectile(false, i, new Vector2(s.Ship, ShipY + .05f));
                        s.FireCooldown = ShotInterval; s.ShotsFired++; Emit(ref s, StarshipEvent.Shot); break;
                    }
            s.FormationX += s.Direction * FormationSpeed(s.Wave) * dt;
            if (Mathf.Abs(s.FormationX) >= FormationLimit)
            {
                s.FormationX = Mathf.Clamp(s.FormationX, -FormationLimit, FormationLimit);
                s.Direction = -s.Direction; s.FormationY -= Descent;
            }
            for (int i = 0; i < ProjectileCount; i++)
            {
                if ((s.Shots & (1 << i)) == 0) continue;
                Vector2 old = s.Projectile(false, i), next = old + Vector2.up * (ShotSpeed * dt);
                s.SetProjectile(false, i, next);
                for (int e = EnemyCount - 1; e >= 0; e--)
                {
                    if ((s.Enemies & (1U << e)) == 0) continue;
                    Vector2 enemy = EnemyPosition(s, e);
                    if (Mathf.Abs(next.x - enemy.x) > EnemyWidth * .5f + ProjectileRadius ||
                        next.y < enemy.y - EnemyHeight * .5f || old.y > enemy.y + EnemyHeight * .5f) continue;
                    s.Enemies &= ~(1U << e); s.Shots &= (byte)~(1 << i);
                    s.Score += (Rows - e / Columns) * 10; Emit(ref s, StarshipEvent.Hit); break;
                }
                if (next.y > Top) s.Shots &= (byte)~(1 << i);
            }
            if (s.Enemies == 0)
            {
                s.Wave++; s.Enemies = FullFormation; ResetField(ref s);
                s.Phase = StarshipPhase.Ready; Emit(ref s, StarshipEvent.Wave); return;
            }
            s.BombCooldown -= dt;
            if (s.BombCooldown <= 0)
            {
                DropBomb(ref s);
                s.BombCooldown = Mathf.Max(.45f, 1.1f - .065f * (s.Wave - 1));
            }
            for (int i = 0; i < ProjectileCount; i++)
            {
                if ((s.Bombs & (1 << i)) == 0) continue;
                Vector2 old = s.Projectile(true, i), next = old + Vector2.down * (BombSpeed(s.Wave) * dt);
                s.SetProjectile(true, i, next);
                if (s.Shield <= 0 && Mathf.Abs(next.x - s.Ship) <= ShipHalfWidth + ProjectileRadius &&
                    next.y <= ShipY + ShipHalfHeight && old.y >= ShipY - ShipHalfHeight)
                { LoseLife(ref s); return; }
                if (next.y < Bottom) s.Bombs &= (byte)~(1 << i);
            }
            for (int e = 0; e < EnemyCount; e++)
                if ((s.Enemies & (1U << e)) != 0 && EnemyPosition(s, e).y <= ShipY + EnemyHeight)
                { LoseLife(ref s); return; }
        }
        private static void DropBomb(ref StarshipState s)
        {
            int slot = -1;
            for (int i = 0; i < ProjectileCount; i++) if ((s.Bombs & (1 << i)) == 0) { slot = i; break; }
            if (slot < 0) return;
            // Nearest live column aims at the player's current lane; the falling shot remains dodgeable.
            int chosen = -1; float best = float.MaxValue;
            for (int c = 0; c < Columns; c++)
                for (int r = Rows - 1; r >= 0; r--)
                {
                    int e = r * Columns + c;
                    if ((s.Enemies & (1U << e)) == 0) continue;
                    float distance = Mathf.Abs(EnemyPosition(s, e).x - s.Ship);
                    if (distance < best) { best = distance; chosen = e; }
                    break;
                }
            if (chosen < 0) return;
            s.Bombs |= (byte)(1 << slot); s.SetProjectile(true, slot, EnemyPosition(s, chosen));
        }
        private static void LoseLife(ref StarshipState s)
        {
            s.Lives--; ResetField(ref s);
            s.Phase = s.Lives > 0 ? StarshipPhase.Ready : StarshipPhase.GameOver;
            Emit(ref s, s.Lives > 0 ? StarshipEvent.LifeLost : StarshipEvent.GameOver);
        }
        private static void ResetField(ref StarshipState s)
        {
            s.Shots = s.Bombs = 0; s.FormationX = s.FormationY = s.Ship = 0;
            s.Direction = 1; s.FireCooldown = 0; s.BombCooldown = 1.2f; s.Shield = 0;
        }
        private static void Emit(ref StarshipState s, StarshipEvent e) { s.Event = e; s.EventId++; }
    }
}
