using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>Short diagonal steps for slow pivots. Planted paws retain world position AND heading.</summary>
    internal sealed class CircusBearTurnSteps
    {
        private const float MaximumTravelSpeed = .65f;
        private const float MaximumWalkingSpeed = 2.2f;
        private const float SettlingTravelSpeed = .15f;
        private const float TurningFootRadius = 1.5f;
        private const float MinimumTurnRate = 8f;
        private const float MaximumPredictionRate = 150f;
        private const float StepSeconds = .28f;
        private const float MinimumStepSeconds = .10f;
        private const float DegreesPerStep = 15.4f;
        private const float PredictionSteps = 1.5f;
        private const float StepHeight = .095f;
        private const float BlendSeconds = .14f;
        private const float SettleDistance = .06f;
        private const float SettleAngle = 4f;
        private const float WeightShiftDegrees = .55f;
        private const float MaximumPawSwingSpeed = 7.5f;
        private const float SmoothStepPeakSpeed = 1.5f;

        private sealed class Foot
        {
            internal CircusBearLeg Leg;
            internal Vector3 Rest, Anchor, From, To;
            internal Quaternion RestRotation, Rotation, FromRotation, ToRotation;
            internal Quaternion UpperRotation, LowerRotation;
            internal Vector3 UpperScale, LowerScale, PawScale, LowerPosition, PawPosition, BendHint;

            internal void SetSupportPose(float blend)
            {
                // The walk clip has its own elbow swing. Do not run that swing
                // underneath a second step: it twists the knee around a planted paw.
                Leg.Upper.localRotation = Quaternion.Slerp(Leg.Upper.localRotation, UpperRotation, blend);
                Leg.Lower.localRotation = Quaternion.Slerp(Leg.Lower.localRotation, LowerRotation, blend);
                Leg.Upper.localScale = Vector3.Lerp(Leg.Upper.localScale, UpperScale, blend);
                Leg.Lower.localScale = Vector3.Lerp(Leg.Lower.localScale, LowerScale, blend);
                Leg.Paw.localScale = Vector3.Lerp(Leg.Paw.localScale, PawScale, blend);
                Leg.Lower.localPosition = Vector3.Lerp(Leg.Lower.localPosition, LowerPosition, blend);
                Leg.Paw.localPosition = Vector3.Lerp(Leg.Paw.localPosition, PawPosition, blend);
            }
        }

        private readonly Foot[] feet = new Foot[4];
        private readonly List<Transform> supportParents = new List<Transform>(12);
        private readonly List<Vector3> parentScales = new List<Vector3>(12);
        private bool known, stepping;
        private int pair;
        private float phase, weight, stepDuration = StepSeconds;

        internal void Bind(int index, CircusBearLeg leg, Transform root)
        {
            if (leg.Paw == null || leg.Upper == null || leg.Lower == null) return;
            for (Transform parent = leg.Upper.parent; parent != null && parent != root; parent = parent.parent)
                if (!supportParents.Contains(parent))
                { supportParents.Add(parent); parentScales.Add(parent.localScale); }
            Vector3 axis = (leg.Paw.position - leg.Upper.position).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(leg.Lower.position - leg.Upper.position, axis).normalized;
            feet[index] = new Foot { Leg = leg, Rest = root.InverseTransformPoint(leg.Paw.position),
                RestRotation = Quaternion.Inverse(root.rotation) * leg.Paw.rotation,
                UpperRotation = leg.Upper.localRotation, LowerRotation = leg.Lower.localRotation,
                UpperScale = leg.Upper.localScale, LowerScale = leg.Lower.localScale, PawScale = leg.Paw.localScale,
                LowerPosition = leg.Lower.localPosition, PawPosition = leg.Paw.localPosition,
                BendHint = root.InverseTransformDirection(bend) };
        }

        internal void Reset() { known = false; stepping = false; phase = 0; weight = 0; pair = 0; }

        internal void Evaluate(Transform root, Vector3 velocity, float turnRate, float dt,
            bool allowed, Transform chest, Transform lumbar)
        {
            if (!allowed) { Reset(); return; }
            float travelSpeed = velocity.magnitude;
            bool slow = travelSpeed < MaximumTravelSpeed;
            bool tightArc = travelSpeed < MaximumWalkingSpeed &&
                Mathf.Abs(turnRate) * Mathf.Deg2Rad * TurningFootRadius > travelSpeed;
            bool turning = (slow || tightArc) && Mathf.Abs(turnRate) > MinimumTurnRate;
            if (!known)
            {
                if (!turning) return;
                foreach (Foot foot in feet)
                    if (foot != null) { foot.Anchor = foot.Leg.Paw.position; foot.Rotation = foot.Leg.Paw.rotation; }
                known = true;
            }
            bool settle = false;
            if (travelSpeed < SettlingTravelSpeed && !turning)
                foreach (Foot foot in feet)
                    if (foot != null && (Vector3.Distance(foot.Anchor, root.TransformPoint(foot.Rest)) > SettleDistance ||
                        Quaternion.Angle(foot.Rotation, root.rotation * foot.RestRotation) > SettleAngle)) settle = true;
            if (!stepping && (turning || settle))
            {
                // Fore-left/hind-right alternate with the other diagonal. No foot
                // remains anchored through a whole turn and no anchor is snapped loose.
                stepping = true; phase = 0;
                float prediction = Mathf.Clamp(turnRate, -MaximumPredictionRate, MaximumPredictionRate);
                stepDuration = turning ? Mathf.Clamp(DegreesPerStep / Mathf.Abs(prediction), MinimumStepSeconds, StepSeconds) : StepSeconds;
                float lead = stepDuration * PredictionSteps;
                float longestStep = 0;
                for (int i = 0; i < feet.Length; i++)
                {
                    Foot foot = feet[i];
                    if (foot == null || !InPair(i)) continue;
                    foot.From = foot.Anchor; foot.FromRotation = foot.Rotation;
                    // A packet correction must not predict an unreachable quarter-turn step.
                    Quaternion ahead = Quaternion.AngleAxis(turning ? prediction * lead : 0, Vector3.up);
                    foot.To = root.position + ahead * root.TransformVector(foot.Rest) + velocity * lead;
                    foot.ToRotation = ahead * root.rotation * foot.RestRotation;
                    longestStep = Mathf.Max(longestStep, Vector3.Distance(foot.From, foot.To));
                }
                // Entry from a walking pose may leave one foot at the back of its
                // stride. Give that first swing time to finish instead of snapping forward.
                stepDuration = Mathf.Max(stepDuration, SmoothStepPeakSpeed * longestStep / MaximumPawSwingSpeed);
            }
            bool support = turning || settle || (stepping && travelSpeed < SettlingTravelSpeed);
            weight = Mathf.MoveTowards(weight, support ? 1 : 0, dt / BlendSeconds);
            if (weight <= 0) { Reset(); return; }
            // Idle breathing scales the chest and compensates at the upper legs.
            // Keep the entire load-bearing chain rigid while its paws are planted.
            for (int i = 0; i < supportParents.Count; i++)
                supportParents[i].localScale = Vector3.Lerp(supportParents[i].localScale, parentScales[i], weight);
            if (stepping) phase = Mathf.Min(1, phase + dt / stepDuration);
            float swing = Mathf.SmoothStep(0, 1, phase);
            float lift = StepHeight * Mathf.Sin(Mathf.PI * phase);
            float shift = (pair == 0 ? 1 : -1) * Mathf.Sin(Mathf.PI * phase) * WeightShiftDegrees * weight;
            if (chest != null) chest.rotation = Quaternion.AngleAxis(shift, root.forward) * chest.rotation;
            if (lumbar != null) lumbar.rotation = Quaternion.AngleAxis(-shift * .5f, root.forward) * lumbar.rotation;
            for (int i = 0; i < feet.Length; i++)
            {
                Foot foot = feet[i];
                if (foot == null) continue;
                Vector3 target = foot.Anchor;
                Quaternion rotation = foot.Rotation;
                if (stepping && InPair(i))
                {
                    target = Vector3.Lerp(foot.From, foot.To, swing) + Vector3.up * lift;
                    rotation = Quaternion.Slerp(foot.FromRotation, foot.ToRotation, swing);
                    if (phase >= 1) { foot.Anchor = foot.To; foot.Rotation = foot.ToRotation; }
                }
                Vector3 animatedPaw = foot.Leg.Paw.position;
                Quaternion animatedRotation = foot.Leg.Paw.rotation;
                foot.SetSupportPose(weight);
                foot.Leg.Solve(Vector3.Lerp(animatedPaw, target, weight),
                    Quaternion.Slerp(animatedRotation, rotation, weight), root.TransformDirection(foot.BendHint));
            }
            if (stepping && phase >= 1) { stepping = false; pair = 1 - pair; }
        }

        private bool InPair(int index) => pair == 0 ? index == 0 || index == 3 : index == 1 || index == 2;
    }
}
