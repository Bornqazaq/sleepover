using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>Shared screen UI palette. World art and character colours remain game-specific.</summary>
    public static class MinigameUiStyle
    {
        public static readonly Color Paper = new Color(1f, .97f, .88f, 1f);
        public static readonly Color Ink = new Color(.055f, .16f, .17f, 1f);
        public static readonly Color HudSurface = new Color(.035f, .09f, .105f, .96f);
        public static readonly Color OnDark = new Color(1f, .97f, .88f, 1f);
        public static readonly Color MutedOnDark = new Color(.70f, .80f, .78f, 1f);
        public static readonly Color Accent = new Color(1f, .79f, .36f, 1f);
        public static readonly Color Urgent = new Color(1f, .43f, .34f, 1f);
        public static readonly Color HudEdge = new Color(.70f, .80f, .78f, .20f);
    }
}
