using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>Opt-in continuous walking surface. Preserve commanded planar speed
    /// rather than repeatedly shortening it by projecting onto a slope.</summary>
    [DisallowMultipleComponent]
    public sealed class WalkableRamp : MonoBehaviour { }
}
