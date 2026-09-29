using Igruha.Core.UI;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Подиум остаётся частью арены, карточку строит общий UI.</summary>
    internal static class HoleInWallResultsBuilder
    {
        internal static void Build() => UnifiedResultsBuilder.Apply(Object.FindFirstObjectByType<RoundHud>());
    }
}
