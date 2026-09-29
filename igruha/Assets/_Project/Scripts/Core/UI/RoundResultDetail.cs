using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>Только открытые данные завершённого раунда. Места и очки задаёт сервер.</summary>
    public readonly struct RoundResultDetail
    {
        public string Value { get; }
        public string Note { get; }
        public Color Accent { get; }

        public RoundResultDetail(string value, string note = "", Color? accent = null)
        {
            Value = value;
            Note = note;
            Accent = accent ?? MinigameUiStyle.Ink;
        }
    }
}
