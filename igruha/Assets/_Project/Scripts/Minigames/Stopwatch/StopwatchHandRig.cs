using UnityEngine;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>Character-specific arm skin. Its contact anchor is measured from the index finger pad.</summary>
    public sealed class StopwatchHandRig : MonoBehaviour
    {
        [SerializeField] private Transform fingerContact;
        private Vector3 contactOffset;
        public Vector3 ContactPosition => fingerContact.position;

        private void Awake() => contactOffset = transform.InverseTransformPoint(fingerContact.position);

        public void Place(Vector3 contact, Quaternion orientation)
        {
            transform.SetPositionAndRotation(contact - orientation * contactOffset, orientation);
        }
    }
}
