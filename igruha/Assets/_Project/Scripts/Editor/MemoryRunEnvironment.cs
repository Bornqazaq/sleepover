using UnityEngine;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    internal static class MemoryRunEnvironment
    {
        internal static void Build(Transform arena, MemoryRunConfig config) => MemoryFoundryBuilder.Build(arena, config);
        internal static string Report() => "Original foundry: tall window bays, deep service galleries, end-platform machinery.";
    }
}
