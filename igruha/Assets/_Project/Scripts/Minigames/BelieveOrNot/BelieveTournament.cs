using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace Igruha.Minigames.BelieveOrNot
{
    public enum BelieveTournamentPhase : byte { Qualification, Playoff, Final, Finished }

    public struct BelieveTournamentState : INetworkSerializable, IEquatable<BelieveTournamentState>
    {
        public BelieveTournamentPhase Phase;
        public int QualificationRound, QualificationTotal;
        public int FinalA, FinalB, WinsA, WinsB, Champion, RunnerUp;
        public int Contenders, PlacesAvailable;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Phase); s.SerializeValue(ref QualificationRound); s.SerializeValue(ref QualificationTotal);
            s.SerializeValue(ref FinalA); s.SerializeValue(ref FinalB); s.SerializeValue(ref WinsA); s.SerializeValue(ref WinsB);
            s.SerializeValue(ref Champion); s.SerializeValue(ref RunnerUp);
            s.SerializeValue(ref Contenders); s.SerializeValue(ref PlacesAvailable);
        }
        public bool Equals(BelieveTournamentState o) => Phase == o.Phase && QualificationRound == o.QualificationRound &&
            QualificationTotal == o.QualificationTotal && FinalA == o.FinalA && FinalB == o.FinalB && WinsA == o.WinsA &&
            WinsB == o.WinsB && Champion == o.Champion && RunnerUp == o.RunnerUp && Contenders == o.Contenders && PlacesAvailable == o.PlacesAvailable;
    }

    public readonly struct BelieveTournamentHand
    {
        public readonly int A, B, Knower;
        public readonly bool Rematch;
        public BelieveTournamentHand(int a, int b, int knower, bool rematch)
        { A = a; B = b; Knower = knower; Rematch = rematch; }
    }

    /// <summary>Серверный турнир. Расписание и отбор не зависят от кадров, сцен и UI.</summary>
    public sealed class BelieveTournament
    {
        public const int NoPlayer = -1;
        public const int QualificationHandsPerPlayer = 4;
        public const int FinalWinsRequired = 2;
        public const int HistoryLength = 3;
        private readonly List<BelieveEntry> entries;
        private readonly List<BelieveTournamentHand> schedule = new List<BelieveTournamentHand>();
        private readonly List<int> seed = new List<int>();
        private readonly List<int> finalists = new List<int>(2);
        private readonly List<int> contenders = new List<int>();
        private readonly Random random;
        private BelieveTournamentState state;
        private BelieveTournamentHand current;
        private int cursor, finalHands, completedHands, firstFinalKnower;
        private bool active;
        public BelieveTournamentState State => state;
        public IReadOnlyList<BelieveTournamentHand> Schedule => schedule;

        public BelieveTournament(List<BelieveEntry> participants, int randomSeed)
        {
            entries = participants;
            random = new Random(randomSeed);
            foreach (var e in entries) if (e.Present) seed.Add(e.PlayerId);
            for (int i = seed.Count - 1; i > 0; i--)
            { int j = random.Next(i + 1); int id = seed[i]; seed[i] = seed[j]; seed[j] = id; }
            if (seed.Count == 2)
            {
                AddPair(seed[0], seed[1]); AddPair(seed[0], seed[1]);
            }
            else if (seed.Count > 2)
            {
                // Рёбра кольца: два разных соперника каждому. Непересекающиеся
                // пары идут сначала, чтобы один игрок не сидел четыре кона подряд.
                for (int parity = 0; parity < 2; parity++)
                    for (int i = parity; i < seed.Count; i += 2) AddPair(seed[i], seed[(i + 1) % seed.Count]);
            }
            state = new BelieveTournamentState { QualificationTotal = schedule.Count,
                FinalA = NoPlayer, FinalB = NoPlayer, Champion = NoPlayer, RunnerUp = NoPlayer };
        }

        private void AddPair(int a, int b)
        {
            int knower = random.Next(2) == 0 ? a : b;
            schedule.Add(new BelieveTournamentHand(a, b, knower, false));
            schedule.Add(new BelieveTournamentHand(a, b, knower == a ? b : a, true));
        }

        public bool Next(out BelieveTournamentHand hand)
        {
            hand = default;
            if (active) throw new InvalidOperationException("Previous duel has no result");
            int present = 0, last = NoPlayer;
            foreach (var e in entries) if (e.Present) { present++; last = e.PlayerId; }
            if (present < 2) { Finish(last, NoPlayer); return false; }
            if (state.Phase == BelieveTournamentPhase.Final || state.Phase == BelieveTournamentPhase.Finished)
            {
                bool a = Present(state.FinalA), b = Present(state.FinalB);
                if (a != b) { Finish(a ? state.FinalA : state.FinalB, a ? state.FinalB : state.FinalA); return false; }
                if (state.Champion >= 0 && Present(state.Champion)) return false;
                if (a && b) return StartFinalHand(out hand);
                finalists.Clear(); contenders.Clear(); SelectContenders();
            }
            if (state.Phase == BelieveTournamentPhase.Qualification)
            {
                while (cursor < schedule.Count)
                {
                    current = schedule[cursor++]; state.QualificationRound = cursor;
                    bool a = Present(current.A), b = Present(current.B);
                    if (a && b) return Activate(current, out hand);
                    if (a || b) { active = true; Record(a ? current.A : current.B, true); }
                }
                SelectContenders();
            }
            if (state.Phase == BelieveTournamentPhase.Playoff)
            {
                finalists.RemoveAll(id => !Present(id));
                contenders.RemoveAll(id => !Present(id));
                int slots = 2 - finalists.Count;
                if (contenders.Count < slots) SelectContenders();
                slots = 2 - finalists.Count;
                state.Contenders = contenders.Count; state.PlacesAvailable = slots;
                if (contenders.Count > slots)
                {
                    int a = contenders[0], b = contenders[1];
                    contenders.RemoveRange(0, 2);
                    return Activate(new BelieveTournamentHand(a, b, random.Next(2) == 0 ? a : b, false), out hand);
                }
                finalists.AddRange(contenders); contenders.Clear();
            }
            if (finalists.Count == 2)
            {
                state.Phase = BelieveTournamentPhase.Final;
                state.FinalA = finalists[0]; state.FinalB = finalists[1];
                state.WinsA = state.WinsB = finalHands = 0;
                firstFinalKnower = random.Next(2) == 0 ? state.FinalA : state.FinalB;
                return StartFinalHand(out hand);
            }
            ForceFinish();
            return false;
        }

        private void SelectContenders()
        {
            finalists.Clear(); contenders.Clear();
            var ranked = new List<int>();
            foreach (int id in seed) if (Present(id)) ranked.Add(id);
            ranked.Sort((a, b) => Wins(b).CompareTo(Wins(a)) != 0 ? Wins(b).CompareTo(Wins(a)) : seed.IndexOf(a).CompareTo(seed.IndexOf(b)));
            for (int i = 0; i < ranked.Count && finalists.Count < 2;)
            {
                int end = i + 1;
                while (end < ranked.Count && Wins(ranked[end]) == Wins(ranked[i])) end++;
                if (end - i <= 2 - finalists.Count)
                    for (; i < end; i++) finalists.Add(ranked[i]);
                else
                {
                    for (; i < end; i++) contenders.Add(ranked[i]);
                    break;
                }
            }
            state.Phase = BelieveTournamentPhase.Playoff;
        }

        private bool StartFinalHand(out BelieveTournamentHand hand)
        {
            int knower = finalHands % 2 == 0 ? firstFinalKnower :
                firstFinalKnower == state.FinalA ? state.FinalB : state.FinalA;
            return Activate(new BelieveTournamentHand(state.FinalA, state.FinalB, knower, finalHands > 0), out hand);
        }

        private bool Activate(BelieveTournamentHand next, out BelieveTournamentHand hand)
        { current = hand = next; active = true; return true; }

        /// <summary>Зовётся после открытия крышек; повтор и чужой победитель отвергаются.</summary>
        public bool Record(int winner, bool forfeit = false)
        {
            if (!active || (winner != current.A && winner != current.B)) return false;
            active = false;
            completedHands++;
            if (state.Phase == BelieveTournamentPhase.Qualification)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e.PlayerId != current.A && e.PlayerId != current.B) continue;
                    e.QualificationPlayed++;
                    if (e.PlayerId == winner) e.QualificationWins++;
                    entries[i] = e;
                }
            }
            else if (state.Phase == BelieveTournamentPhase.Playoff) contenders.Add(winner);
            else if (state.Phase == BelieveTournamentPhase.Final)
            {
                finalHands++;
                if (winner == state.FinalA) state.WinsA++; else state.WinsB++;
                if (state.WinsA >= FinalWinsRequired || state.WinsB >= FinalWinsRequired)
                    Finish(winner, winner == state.FinalA ? state.FinalB : state.FinalA);
            }
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.PlayerId != winner) continue;
                e.RoundsWon++;
                if (forfeit) e.ForfeitWins++;
                else if (winner != current.Knower) e.DeciderWins++;
                e.LastWonAt = completedHands;
                entries[i] = e;
                break;
            }
            return true;
        }

        public void ForceFinish()
        {
            active = false;
            if (state.Champion >= 0 && Present(state.Champion)) return;
            int best = NoPlayer, second = NoPlayer;
            foreach (int id in seed)
            {
                if (!Present(id)) continue;
                if (best < 0 || Wins(id) > Wins(best)) { second = best; best = id; }
                else if (second < 0 || Wins(id) > Wins(second)) second = id;
            }
            Finish(best, second);
        }

        private void Finish(int winner, int runner)
        { state.Phase = BelieveTournamentPhase.Finished; state.Champion = winner; state.RunnerUp = runner; }
        private bool Present(int id) { foreach (var e in entries) if (e.PlayerId == id) return e.Present; return false; }
        private int Wins(int id) { foreach (var e in entries) if (e.PlayerId == id) return e.QualificationWins; return 0; }
    }
}
