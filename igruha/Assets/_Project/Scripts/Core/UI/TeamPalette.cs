using UnityEngine;
using Igruha.Core.Session;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Цвета команд — один набор на проект.
    ///
    /// Заведён потому, что цвет команды перестал быть делом интерфейса. Пока
    /// он жил только в полосе прогресса, мир оставался серым: в «Переноске»
    /// на плейтесте не читалось, чей бак и чья бутыль. Теперь этим же цветом
    /// красятся крышка бутыли, обод бака и штабель, и держать две копии
    /// значений нельзя — на первой же правке они разъедутся, а разъехавшийся
    /// цвет команды хуже, чем его отсутствие.
    /// </summary>
    public static class TeamPalette
    {
        /// <summary>Команда A — синяя.</summary>
        public static readonly Color TeamA = new Color(0.25f, 0.55f, 1f);

        /// <summary>Команда B — оранжевая.</summary>
        public static readonly Color TeamB = new Color(1f, 0.45f, 0.2f);

        /// <summary>Команды нет либо участник вне составов.</summary>
        public static readonly Color Neutral = new Color(0.72f, 0.72f, 0.72f);

        /// <summary>Цвет этой команды.</summary>
        public static Color ColorOf(TeamSide side)
        {
            switch (side)
            {
                case TeamSide.A:
                    return TeamA;
                case TeamSide.B:
                    return TeamB;
                default:
                    return Neutral;
            }
        }
    }
}
