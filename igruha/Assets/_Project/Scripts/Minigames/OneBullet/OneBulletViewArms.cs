using System;
using UnityEngine;

namespace Igruha.Minigames.OneBullet
{
    /// <summary>Lightweight arm-only copy of the selected character. Never drives the network avatar.</summary>
    public sealed class OneBulletViewArms : MonoBehaviour
    {
        [Serializable]
        public sealed class Finger
        {
            [SerializeField] private Transform bone;
            [SerializeField] private Vector3 curlAxis;
            [SerializeField] private int digit, joint;
            public int Digit => digit;
            public int Joint => joint;
            private Quaternion rest;
            public void Cache() => rest = bone.localRotation;
            public void Curl(float degrees) => bone.localRotation = rest * Quaternion.AngleAxis(degrees, curlAxis);
        }
        [Serializable]
        public sealed class Arm
        {
            [SerializeField] private Transform upper, lower, hand;
            public Transform Upper => upper;
            public Transform Lower => lower;
            public Transform Hand => hand;
            [SerializeField] private Quaternion palmCorrection;
            public Quaternion PalmCorrection => palmCorrection;
            [SerializeField] private Finger[] fingers;
            public Finger[] Fingers => fingers;
            [SerializeField] private bool separateIndex;
            public bool SeparateIndex => separateIndex;
            [SerializeField] private float handReach;
            public float HandReach => handReach;
            [NonSerialized] public float upperLength, lowerLength;
            public void Cache()
            {
                upperLength = Vector3.Distance(upper.position, lower.position);
                lowerLength = Vector3.Distance(lower.position, hand.position);
                foreach (var finger in fingers) finger.Cache();
            }
        }
        [SerializeField] private Arm left, right;
        public const float NormalizedArmLength = .64f;
        private const float HandReach = .14f, ReachMargin = .002f;
        private const float FistThumb = 35f, GripThumb = 25f, TriggerBase = 24f, TriggerTip = 44f;
        private static readonly Vector3 FistCurl = new Vector3(72f, 88f, 60f);
        private static readonly Vector3 GripCurl = new Vector3(58f, 80f, 52f);
        private static readonly Vector3 RightShoulder = new Vector3(.25f, -.43f, -.08f);
        private static readonly Vector3 LeftShoulder = new Vector3(-.25f, -.43f, -.08f);
        private static readonly Vector3 RightPole = new Vector3(.60f, -.65f, .10f);
        private static readonly Vector3 LeftPole = new Vector3(-.60f, -.65f, .10f);
        private void Awake()
        {
            float length = Vector3.Distance(right.Upper.position, right.Lower.position) + Vector3.Distance(right.Lower.position, right.Hand.position);
            transform.localScale *= NormalizedArmLength / Mathf.Max(.01f, length);
            FitHand(left); FitHand(right);
            left.Cache(); right.Cache();
        }
        private static void FitHand(Arm arm)
        {
            arm.Hand.localScale *= HandReach / Mathf.Max(.01f, arm.HandReach * arm.Hand.lossyScale.x);
        }
        public void Pose(Transform space, Vector3 leftWrist, Quaternion leftPalm, Vector3 rightWrist, Quaternion rightPalm, float armed, float support)
        {
            Solve(left, space, LeftShoulder, LeftPole, leftWrist, leftPalm);
            Solve(right, space, RightShoulder, RightPole, rightWrist, rightPalm);
            Curl(left, support, false); Curl(right, armed, true);
        }
        private static void Solve(Arm arm, Transform space, Vector3 shoulder, Vector3 pole, Vector3 wrist, Quaternion palm)
        {
            Vector3 start = space.TransformPoint(shoulder), target = space.TransformPoint(wrist);
            Vector3 delta = target - start;
            float distance = Mathf.Clamp(delta.magnitude, ReachMargin, arm.upperLength + arm.lowerLength - ReachMargin);
            Vector3 direction = delta.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(space.TransformPoint(pole) - start, direction).normalized;
            float along = (arm.upperLength * arm.upperLength - arm.lowerLength * arm.lowerLength + distance * distance) / (2 * distance);
            float height = Mathf.Sqrt(Mathf.Max(0, arm.upperLength * arm.upperLength - along * along));
            Vector3 elbow = start + direction * along + bend * height;
            arm.Upper.position = start;
            arm.Upper.rotation = Quaternion.FromToRotation(arm.Lower.position - start, elbow - start) * arm.Upper.rotation;
            arm.Lower.rotation = Quaternion.FromToRotation(arm.Hand.position - arm.Lower.position, target - arm.Lower.position) * arm.Lower.rotation;
            arm.Hand.rotation = space.rotation * palm * arm.PalmCorrection;
        }
        private static void Curl(Arm arm, float grip, bool triggerHand)
        {
            foreach (var finger in arm.Fingers)
            {
                float fist = finger.Digit == 0 ? FistThumb : FistCurl[finger.Joint];
                float hold = finger.Digit == 0 ? GripThumb : GripCurl[finger.Joint];
                if (triggerHand && arm.SeparateIndex && finger.Digit == 1) hold = finger.Joint == 0 ? TriggerBase : TriggerTip;
                finger.Curl(Mathf.Lerp(fist, hold, grip));
            }
        }
    }
}
