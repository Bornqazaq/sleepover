using UnityEngine;

namespace Igruha.Core.Audio
{
    /// <summary>Optional physical stereo speakers for the existing scene music player.</summary>
    [DisallowMultipleComponent]
    public sealed class SceneMusicEmitter : MonoBehaviour
    {
        [SerializeField] private Transform leftSpeaker;
        [SerializeField] private Transform rightSpeaker;
        [SerializeField, Min(.1f)] private float minDistance = 3f;
        [SerializeField, Min(.1f)] private float maxDistance = 26f;

        internal bool IsReady => isActiveAndEnabled && leftSpeaker != null && rightSpeaker != null;

        internal void Configure(AudioSource left, AudioSource right)
        {
            ConfigureSpeaker(left);
            ConfigureSpeaker(right);
            Follow(left, right);
        }

        internal void Follow(AudioSource left, AudioSource right)
        {
            if (leftSpeaker != null) left.transform.position = leftSpeaker.position;
            if (rightSpeaker != null) right.transform.position = rightSpeaker.position;
        }

        private void ConfigureSpeaker(AudioSource source)
        {
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minDistance;
            source.maxDistance = Mathf.Max(minDistance + .1f, maxDistance);
            source.spread = 0f;
            source.dopplerLevel = 0f;
        }
    }
}
