using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>A large change of heading consumes distance along a circular path.
    /// Choose the free side before starting; a blocked forward arc uses short backward steps.</summary>
    internal sealed class CircusBearArcTurn
    {
        private const float StartAngle = 40f;
        private const float FinishAngle = 7f;
        private const float WalkSpeed = 1.05f;
        private const float BackstepSpeed = .55f;
        private const float Acceleration = 2f;
        private const float WideRadius = 1.3f;
        private const float TightRadius = .85f;
        private const float SampleDegrees = 12f;
        private const float MaximumPlanDegrees = 220f;
        private const float MinimumEscapeArcDegrees = 60f;
        private float side, travelSign, radius, speed, remaining;
        private bool active;

        internal void Reset() { active = false; speed = 0; }

        internal bool Advance(Transform root, Vector3 direction, float dt, float boundary,
            Transform companion, bool pursuing, out Vector3 displacement, out float yaw)
        {
            displacement = Vector3.zero; yaw = 0;
            direction.y = 0;
            if (direction.sqrMagnitude < .0001f) { Reset(); return false; }
            float error = Vector3.SignedAngle(root.forward, direction, Vector3.up);
            // An interception/avoidance waypoint may change sides while walking.
            // Release the old arc instead of completing an unwanted full circle.
            if (active && error * side < 0 && Mathf.Abs(error) < 90) Reset();
            // Running already covers ground while steering. Reserve the slow
            // manoeuvre for an actual reversal, so a fleeing runner stays interceptable.
            float entryAngle = pursuing ? 100f : StartAngle;
            if (Mathf.Abs(error) < (active ? FinishAngle : entryAngle)) { Reset(); return false; }
            if (!active && !Choose(root, error, boundary, companion))
            {
                EscapeAlongRim(root,error,dt,boundary,companion,out displacement,out yaw);
                return true;
            }
            speed = Mathf.MoveTowards(speed, travelSign > 0 ? WalkSpeed : BackstepSpeed, Acceleration * dt);
            yaw = side * Mathf.Min(speed * dt / radius * Mathf.Rad2Deg,
                Mathf.Repeat(error * side, 360));
            displacement = Offset(root, yaw, radius, travelSign);
            if (Clear(root.position + displacement, boundary, companion))
            {
                remaining -= Mathf.Abs(yaw);
                if (remaining <= 0) active = false;
                return true;
            }
            // A companion can enter an arc after it was planned. Never rotate
            // against a blocked translation or project a walking body sideways.
            active = false; speed = 0; displacement = Vector3.zero; yaw = 0;
            Choose(root, error, boundary, companion);
            return true;
        }

        private bool Choose(Transform root, float error, float boundary, Transform companion)
        {
            float best = float.NegativeInfinity;
            for (int backwards = 0; backwards < 2; backwards++)
            for (int tight = 0; tight < 2; tight++)
            for (int opposite = 0; opposite < 2; opposite++)
            {
                float candidateSide = Mathf.Sign(error) * (opposite == 0 ? 1 : -1);
                float candidateTravel = backwards == 0 ? 1 : -1;
                float candidateRadius = tight == 0 ? WideRadius : TightRadius;
                float angle = Mathf.Repeat(error * candidateSide, 360);
                float planned = Mathf.Min(angle, MaximumPlanDegrees);
                float clear = 0;
                for (float sample = Mathf.Min(SampleDegrees, planned); sample <= planned + SampleDegrees; sample += SampleDegrees)
                {
                    float degrees = Mathf.Min(sample, planned);
                    if (!ClearArc(root, candidateSide * degrees, candidateRadius, candidateTravel, boundary, companion)) break;
                    clear = degrees;
                    if (degrees >= planned) break;
                }
                // A tiny feasible slice can alternate forward/backward forever
                // in the same pocket. Open space with rim steps until an arc can
                // make substantial progress or finish the requested heading.
                if (clear < Mathf.Min(MinimumEscapeArcDegrees, planned)) continue;
                // A complete forward arc beats a short route that will stop at
                // the wall. Partial routes are an escape from crowded corners.
                float score = clear / Mathf.Max(SampleDegrees, planned) * 1000 - angle - backwards * 100 - tight * 20;
                if (score <= best) continue;
                best = score; side = candidateSide; travelSign = candidateTravel; radius = candidateRadius;
                remaining = Mathf.Min(clear, 90f);
            }
            active = !float.IsNegativeInfinity(best);
            return active;
        }

        private static Vector3 Offset(Transform root, float degrees, float radius, float travel)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return (root.forward * Mathf.Sin(Mathf.Abs(angle)) +
                root.right * (Mathf.Sign(angle) * (1 - Mathf.Cos(angle)))) * (radius * travel);
        }

        private static bool ClearArc(Transform root,float degrees,float radius,float travel,float boundary,Transform companion)
        {
            Vector3 end=root.position+Offset(root,degrees,radius,travel);
            if(!Clear(end,boundary,companion))return false;
            Vector3 centre=root.position+root.right*(Mathf.Sign(degrees)*radius*travel);
            Vector3 arm=root.position-centre;
            Vector3 outward=new Vector3(centre.x,0,centre.z);
            if(OnArc(arm,outward,degrees) && !Clear(centre+outward.normalized*radius,boundary,null))return false;
            if(companion!=null)
            {
                Vector3 toward=companion.position-centre;toward.y=0;
                if(OnArc(arm,toward,degrees) && !Clear(centre+toward.normalized*radius,boundary,companion))return false;
            }
            return true;
        }

        private static bool OnArc(Vector3 from,Vector3 toward,float degrees)
        {
            if(toward.sqrMagnitude<.000001f)return false;
            float along=Mathf.Repeat(Vector3.SignedAngle(from,toward,Vector3.up)*Mathf.Sign(degrees),360);
            return along<=Mathf.Abs(degrees);
        }

        private static void EscapeAlongRim(Transform root,float error,float dt,float boundary,
            Transform companion,out Vector3 displacement,out float yaw)
        {
            displacement=Vector3.zero;yaw=0;
            Vector3 radial=new Vector3(root.position.x,0,root.position.z);
            if(radial.magnitude<TightRadius)return;
            // At the rim a companion can block the backward leg as well as
            // the forward arc. Short lateral support steps along the rim open
            // space; rotating around the arena centre stays inside the wall.
            float angle=BackstepSpeed*dt/radial.magnitude*Mathf.Rad2Deg;
            for(int attempt=0;attempt<2;attempt++)
            {
                float sign=Mathf.Sign(error)*(attempt==0?1:-1);
                Vector3 next=Quaternion.AngleAxis(sign*angle,Vector3.up)*radial;
                next.y=root.position.y;
                if(!Clear(next,boundary+.00001f,companion))continue;
                displacement=next-root.position;
                yaw=Mathf.Sign(error)*Mathf.Min(Mathf.Abs(error),displacement.magnitude/TightRadius*Mathf.Rad2Deg);
                return;
            }
        }

        private static bool Clear(Vector3 position, float boundary, Transform companion)
        {
            if (new Vector2(position.x, position.z).sqrMagnitude > boundary * boundary) return false;
            if (companion == null) return true;
            Vector3 delta = position - companion.position; delta.y = 0;
            return delta.sqrMagnitude >= PitBear.CompanionClearance * PitBear.CompanionClearance;
        }
    }
}
