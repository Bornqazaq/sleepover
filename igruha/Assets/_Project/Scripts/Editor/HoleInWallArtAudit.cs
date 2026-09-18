using UnityEditor;

namespace Igruha.EditorTools
{
    /// <summary>The established audit menu now validates the original studio.
    /// Collision clearance, fitted meshes, dependencies and runtime wiring live
    /// together in the studio audit instead of asserting obsolete Synty layout.</summary>
    internal static class HoleInWallArtAudit
    {
        [MenuItem("Igruha/Дырка в стене/Замеры арта")]
        public static void Run() => HoleInWallStudioBuilder.Audit();
    }
}
