using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>Length-preserving two-bone solve shared by the swat and short turning steps.</summary>
    internal sealed class CircusBearLeg
    {
        internal Transform Upper, Lower, Paw, Toes;
        internal Vector3 ContactOffset;
        internal float UpperLength, LowerLength;

        internal void Solve(Vector3 target, Quaternion pawRotation)
            => Solve(target, pawRotation, Lower.position - Upper.position);

        internal void Solve(Vector3 target, Quaternion pawRotation, Vector3 bendHint)
        {
            Vector3 start = Upper.position;
            Vector3 direction = target - start;
            // Use the current rigid segment lengths, including the imported rig scale.
            float upper = Vector3.Distance(start, Lower.position);
            float lower = Vector3.Distance(Lower.position, Paw.position);
            float distance = Mathf.Clamp(direction.magnitude, Mathf.Abs(upper - lower) + .006f, upper + lower - .006f);
            direction.Normalize();
            Vector3 pole = bendHint;
            pole -= direction * Vector3.Dot(pole, direction);
            if (pole.sqrMagnitude < .00001f) pole = Vector3.Cross(direction, Upper.right);
            pole.Normalize();
            float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
            Vector3 joint = start + direction * along + pole * Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
            Upper.rotation = Quaternion.FromToRotation(Lower.position - start, joint - start) * Upper.rotation;
            Lower.rotation = Quaternion.FromToRotation(Paw.position - Lower.position, start + direction * distance - Lower.position) * Lower.rotation;
            Paw.rotation = pawRotation;
        }
    }
}
