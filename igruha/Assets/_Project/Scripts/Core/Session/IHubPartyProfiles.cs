using System;
using System.Text;

namespace Igruha.Core.Session
{
    /// <summary>Only the local participant may request their own profile changes.</summary>
    public interface IHubPartyProfiles
    {
        event Action<string> ProfileResult;
        int CharacterOf(int playerId);
        void ChangeOwnName(string value);
        void ChangeOwnCharacter(int index);
    }

    public interface ISessionScoreReset { void ResetScores(); }

    public static class PartyDisplayName
    {
        // NGO FixedString32Bytes has 29 UTF-8 payload bytes. Count bytes, not glyphs.
        public static bool TryNormalize(string input, out string value)
        {
            value = (input ?? string.Empty).Trim();
            foreach (char c in value) if (char.IsSurrogate(c)) return false;
            value = value.Normalize();
            if (value.Length == 0 || value.Length > 20 || Encoding.UTF8.GetByteCount(value) > 29) return false;
            foreach (char c in value)
                if (char.IsControl(c) || c == '<' || c == '>' || char.IsSurrogate(c)) return false;
            return true;
        }
    }
}
