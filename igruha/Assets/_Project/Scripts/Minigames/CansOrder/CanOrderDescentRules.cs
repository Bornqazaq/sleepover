using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>One penalty per reveal, from this turn's result only. Solvers are safe.</summary>
    public static class CanOrderDescentRules
    {
        public static int Score(CansOrderEntry entry) => entry.Confirmed ? entry.Matches : -1;

        public static int WorstScore(IReadOnlyList<CansOrderEntry> entries)
        {
            int worst = int.MaxValue;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Alive && !entries[i].Solved) worst = Mathf.Min(worst, Score(entries[i]));
            return worst;
        }

        public static void Apply(ref CansOrderEntry entry, int worst, int steps, int circle)
        {
            entry.Penalty = CansOrderPenalty.None;
            if (!entry.Alive || entry.Solved || Score(entry) != worst) return;
            steps = Mathf.Max(1, steps);
            int level = Mathf.RoundToInt(entry.HeightFraction * steps);
            if (level > 0)
            {
                entry.HeightFraction = (level - 1f) / steps;
                entry.Penalty = CansOrderPenalty.Descended;
                return;
            }
            entry.BottomChances = Mathf.Max(0, entry.BottomChances - 1);
            entry.Penalty = entry.BottomChances > 0 ? CansOrderPenalty.LastChance : CansOrderPenalty.Dropped;
            if (entry.BottomChances == 0)
            {
                entry.Alive = false;
                entry.EliminatedCircle = circle;
            }
        }
    }
}
