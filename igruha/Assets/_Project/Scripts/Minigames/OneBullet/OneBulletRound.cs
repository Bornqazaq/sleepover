using System;
using System.Collections.Generic;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.OneBullet
{
    /// <summary>Authoritative rules, independent of Unity physics and presentation.</summary>
    public sealed class OneBulletRound
    {
        public const int Nobody = -1;
        public sealed class Record
        {
            public int Id { get; }
            public bool Alive { get; internal set; } = true;
            public int Kills { get; internal set; }
            public double Life { get; internal set; }
            public Record(int id) { Id = id; }
        }
        private readonly List<Record> records = new List<Record>(8);
        private readonly List<Record> sorted = new List<Record>(8);
        public IReadOnlyList<Record> Records => records;
        public int Holder { get; private set; } = Nobody;
        public int Pickup { get; private set; } = Nobody;
        public int PreviousPickup { get; private set; } = Nobody;
        public double BeginsAt { get; private set; }
        public double EndsAt { get; private set; }
        public double SpawnAt { get; private set; }
        public bool Finished { get; private set; }
        public int Winner { get; private set; } = Nobody;
        public int AliveCount { get { int n = 0; foreach (var r in records) if (r.Alive) n++; return n; } }

        public void Reset(IReadOnlyList<int> ids, double beginsAt, double duration, double firstDelay)
        {
            records.Clear(); sorted.Clear();
            foreach (int id in ids) records.Add(new Record(id));
            Holder = Pickup = PreviousPickup = Winner = Nobody;
            BeginsAt = beginsAt; EndsAt = beginsAt + duration;
            SpawnAt = beginsAt + firstDelay; Finished = false;
        }
        public Record Find(int id)
        {
            foreach (var r in records) if (r.Id == id) return r;
            return null;
        }
        public bool IsLive(double now) => !Finished && now >= BeginsAt && now < EndsAt;
        public bool CanSpawn(double now) => IsLive(now) && Holder == Nobody && Pickup == Nobody && now >= SpawnAt;
        public bool Spawn(int point, double now)
        {
            if (!CanSpawn(now) || point < 0 || point == PreviousPickup) return false;
            Pickup = PreviousPickup = point; return true;
        }
        public bool Take(int id, double now)
        {
            if (!IsLive(now) || Pickup < 0 || Holder >= 0 || Find(id)?.Alive != true) return false;
            Holder = id; Pickup = Nobody; return true;
        }
        public bool CanFire(int id, double now) => IsLive(now) && Holder == id && Find(id)?.Alive == true;
        public bool Fire(int id, int victim, double now, double respawnDelay)
        {
            if (!CanFire(id, now)) return false;
            Holder = Pickup = Nobody; SpawnAt = now + respawnDelay;
            var target = Find(victim);
            if (victim != id && target != null && target.Alive)
            {
                Eliminate(target, now); Find(id).Kills++;
            }
            return true;
        }
        public bool Leave(int id, double now, double respawnDelay)
        {
            var r = Find(id);
            if (Finished || r == null || !r.Alive) return false;
            Eliminate(r, now);
            if (Holder == id) { Holder = Pickup = Nobody; SpawnAt = now + respawnDelay; }
            return true;
        }
        private void Eliminate(Record r, double now)
        {
            r.Alive = false; r.Life = Math.Max(0, Math.Min(now, EndsAt) - BeginsAt);
        }
        public void Finish(double now)
        {
            if (Finished) return;
            Finished = true;
            int living = AliveCount;
            foreach (var r in records)
                if (r.Alive) { r.Life = Math.Max(0, Math.Min(now, EndsAt) - BeginsAt); if (living == 1) Winner = r.Id; }
            if (Winner < 0 && Find(Holder)?.Alive == true) Winner = Holder;
        }
        public void Collect(MinigameResults output)
        {
            sorted.Clear(); sorted.AddRange(records); sorted.Sort(Compare);
            int place = 1;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0 && Compare(sorted[i - 1], sorted[i]) != 0) place = i + 1;
                output.Add(sorted[i].Id, place);
            }
        }
        private int Compare(Record a, Record b)
        {
            if (a.Id == Winner) return b.Id == Winner ? 0 : -1;
            if (b.Id == Winner) return 1;
            int kills = b.Kills.CompareTo(a.Kills);
            return kills != 0 ? kills : b.Life.CompareTo(a.Life);
        }

        public void ApplyHeader(int holder, int pickup, int previous, double begins, double ends, double spawn, bool finished, int winner)
        {
            Holder = holder; Pickup = pickup; PreviousPickup = previous;
            BeginsAt = begins; EndsAt = ends; SpawnAt = spawn; Finished = finished; Winner = winner;
        }

        public void ApplyRecord(int id, bool alive, int kills, double life)
        {
            var record = Find(id);
            if (record == null) { record = new Record(id); records.Add(record); }
            record.Alive = alive; record.Kills = kills; record.Life = life;
        }
    }
}
