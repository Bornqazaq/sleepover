using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Broken concrete with several uneven crowns, shared by the authored mesh and wheel contact.</summary>
    public sealed class CartRoughSurface : MonoBehaviour
    {
        public const float HalfLength = 1.25f, HalfWidth = 1.12f, CrownSpacing = .62f;
        private static readonly List<CartRoughSurface> active = new List<CartRoughSurface>();
        public static IReadOnlyList<CartRoughSurface> Active => active;
        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() => active.Remove(this);
        public static float Envelope(float x, float z) => Mathf.Clamp01((1f - x*x/(HalfLength*HalfLength) - z*z/(HalfWidth*HalfWidth)) * 3f);
        public static float Height(float x, float z) => Envelope(x,z) *
            (.012f + .048f * Mathf.Pow(.5f + .5f * Mathf.Cos((x + z*.16f) / CrownSpacing * Mathf.PI * 2f), 3f));
        public bool Crossed(Vector3 from, Vector3 to, out float approach, out float severity)
        {
            approach = severity = 0;
            Vector3 a=transform.InverseTransformPoint(from), b=transform.InverseTransformPoint(to);
            float u=a.x+a.z*.16f, v=b.x+b.z*.16f;
            if(Mathf.FloorToInt(u/CrownSpacing)==Mathf.FloorToInt(v/CrownSpacing))return false;
            float boundary=(v>u?Mathf.FloorToInt(v/CrownSpacing):Mathf.FloorToInt(u/CrownSpacing))*CrownSpacing;
            var hit=Vector3.Lerp(a,b,(boundary-u)/(v-u));
            if(Mathf.Abs(hit.y)>.38f)return false;
            severity=Envelope(hit.x,hit.z)*.64f;
            approach=Mathf.Abs(Vector3.Dot((to-from).normalized,transform.TransformDirection(new Vector3(1,0,.16f).normalized)));
            return severity>.05f;
        }
    }
}
