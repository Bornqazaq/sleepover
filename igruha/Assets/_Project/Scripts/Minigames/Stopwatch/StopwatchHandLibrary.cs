using System;
using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.Stopwatch
{
    public sealed class StopwatchHandLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string character;
            public Mesh sourceMesh;
            public StopwatchHandRig hand;
        }
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        public Entry[] Entries => entries;

        public StopwatchHandRig Find(PlayerController player)
        {
            var skin = player.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin == null) return null;
            foreach (var entry in entries)
                if (entry.sourceMesh == skin.sharedMesh) return entry.hand;
            return null;
        }
    }
}
