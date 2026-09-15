using System;
using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Core.Hub
{
    [CreateAssetMenu(menuName = "Igruha/Console Artwork Library")]
    public sealed class ConsoleArtworkLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private MinigameDefinition game;
            [SerializeField] private Sprite cover;
            [SerializeField] private Color accent = Color.white;
            [SerializeField] private string genre;
            [SerializeField, TextArea] private string summary;

            public MinigameDefinition Game => game;
            public Sprite Cover => cover;
            public Color Accent => accent;
            public string Genre => genre;
            public string Summary => summary;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public Entry Find(MinigameDefinition game)
        {
            if (game == null) return null;
            foreach (Entry entry in entries)
                if (entry != null && entry.Game == game) return entry;
            return null;
        }
    }
}
