using System;
using UnityEngine;
namespace Igruha.Minigames.Circus
{
    public sealed class CircusAngerLibrary : ScriptableObject
    {
        [Serializable] public struct Entry
        {
            public string character;
            public Mesh sourceMesh;
            public Mesh headMesh;
        }
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        public Entry[] Entries => entries;
        public Mesh Find(Mesh source)
        {
            foreach (var entry in entries) if (entry.sourceMesh == source) return entry.headMesh;
            return null;
        }
    }
}
