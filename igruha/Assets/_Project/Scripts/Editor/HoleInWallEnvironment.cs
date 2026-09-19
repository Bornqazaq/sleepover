using UnityEngine;
using Igruha.Minigames.HoleInWall;

namespace Igruha.EditorTools
{
    /// <summary>The regular arena rebuild always uses our original studio.</summary>
    internal static class HoleInWallEnvironment
    {
        internal static void Build(Transform arena, HoleInWallConfig config,
            HoleInWallTrack[] tracks, System.Random rng)
        {
            HoleInWallStudioBuilder.Build(arena, config, tracks);
        }
    }
}
