using System;
using System.Collections.Generic;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.SumoRing
{
    /// <summary>One server physics step is one elimination group, independent of roster iteration order.</summary>
    public sealed class SumoRound
    {
        public sealed class Record
        {
            public int Id { get; }
            public int Group { get; internal set; } = -1;
            public double Life { get; internal set; }
            public bool Alive => Group < 0;
            public Record(int id) => Id = id;
        }
        private readonly List<Record> records = new List<Record>(8);
        private readonly List<int> groupIds = new List<int>(8);
        private readonly EliminationRanking ranking = new EliminationRanking();
        private int nextGroup;
        public IReadOnlyList<Record> Records => records;
        public double BeginsAt { get; private set; }
        public double FinishedAt { get; private set; }
        public bool Ready { get; private set; }
        public bool Finished { get; private set; }
        public int AliveCount { get { int n = 0; foreach (var p in records) if (p.Alive) n++; return n; } }
        public void Reset(IReadOnlyList<int> ids, double begins)
        {
            records.Clear(); nextGroup = 0; BeginsAt = begins; Finished = false; FinishedAt = 0; Ready = true;
            foreach (int id in ids) if (Find(id) == null) records.Add(new Record(id));
        }
        public Record Find(int id) { foreach (var p in records) if (p.Id == id) return p; return null; }
        public bool Eliminate(IReadOnlyList<int> ids, double now)
        {
            if (!Ready || Finished) return false;
            bool changed = false;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = Find(ids[i]);
                if (p == null || !p.Alive) continue;
                p.Group = nextGroup; p.Life = Math.Max(0, now - BeginsAt); changed = true;
            }
            if (changed) nextGroup++;
            return changed;
        }
        public void Finish(double now)
        {
            if (Finished) return;
            Finished = true; FinishedAt = now;
            foreach (var p in records) if (p.Alive) p.Life = Math.Max(0, now - BeginsAt);
        }
        public void Collect(MinigameResults results)
        {
            ranking.Clear();
            for (int group = 0; group < nextGroup; group++)
            {
                groupIds.Clear();
                foreach (var p in records) if (p.Group == group) groupIds.Add(p.Id);
                ranking.AddEliminationGroup(groupIds);
            }
            foreach (var p in records) if (p.Alive) ranking.AddSurvivor(p.Id);
            ranking.Build(results, (a, b) => 0);
        }
        public void ApplyHeader(double begins, bool finished, double ended)
        { Ready = true; BeginsAt = begins; Finished = finished; FinishedAt = ended; }
        public void ApplyRecord(int id, int group, double life)
        {
            var p = Find(id);
            if (p == null) { p = new Record(id); records.Add(p); }
            p.Group = group; p.Life = life; nextGroup = Math.Max(nextGroup, group + 1);
        }
    }
}
