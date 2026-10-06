using System;
using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    public enum SumoMotion : byte { Guard, Charge, Quick, Heavy, Counter, Parry, Recoil, Brace, Balance, Stumble, Shoulder }
    [Serializable]
    public struct SumoMuscleTrack
    {
        public int Muscle;
        public bool Leg;
        public AnimationCurve Curve;
    }
    [Serializable]
    public sealed class SumoMotionData
    {
        public AnimationClip EditableClip;
        public SumoMuscleTrack[] Tracks;
    }
    /// <summary>Editable humanoid clips plus their runtime muscle curves. No shared Animator Controller is replaced.</summary>
    [CreateAssetMenu(menuName = "Igruha/Sumo Motions")]
    public sealed class SumoMotionLibrary : ScriptableObject
    {
        [SerializeField] private SumoMotionData[] motions;
        public SumoMotionData Get(SumoMotion motion) => motions != null && (int)motion < motions.Length ? motions[(int)motion] : null;
    }
}
