using System.Collections.Generic;
using Igruha.Minigames.BelieveOrNot;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Original club architecture. Furniture is a separate art pass.</summary>
    internal static class BelieveOrNotHall
    {
        internal static IReadOnlyList<string> Notes { get; } = new[]
        {
            "Собственная модульная оболочка Blender, тёмно-синее дерево, редкий бархат; декор без коллайдеров."
        };

        internal static void Build(Transform arena, BelieveOrNotConfig config, float wallFace)
        {
            if (arena != null && config != null) BelievePrivateClubBuilder.Build(arena, config);
        }
    }
}
