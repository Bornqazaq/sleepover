using System.Collections.Generic;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.CansOrder
{
    public sealed partial class CansOrderMinigame
    {
        private readonly List<int> practiceOrder = new List<int>(8);
        private readonly List<int> practiceIncoming = new List<int>(8);
        private ulong practiceArrangement;
        private int practiceCircle = -1, practiceMatches = -1;
        private byte practiceCount;
        public int PracticeMatches => IsPractice && Phase == MinigamePhase.Practice ? practiceMatches : -1;

        private void ResetPracticePreview()
        {
            practiceCircle = practiceMatches = -1;
            practiceCount = 0;
        }

        public void RefreshPracticePreview(CanShelf shelf)
        {
            if (!IsPractice || Phase != MinigamePhase.Practice || shelf == null) { ResetPracticePreview(); return; }
            var local = FindLocal();
            if (local == null) return;
            if (Stage == StageReveal && resultsRevealed && local.Entry.Confirmed)
            { practiceMatches = local.Entry.Matches; return; }
            if (Stage != StagePlacement || !shelf.Active || !shelf.TryGetArrangement(practiceOrder) ||
                !CansOrderEntryNetState.TryPack(practiceOrder, out ulong packed, out byte count)) return;
            if (practiceCircle == round.Circle && practiceCount == count && practiceArrangement == packed) return;
            if (!HasAuthority && (network == null || !network.IsSpawned)) return;
            practiceCircle = round.Circle; practiceCount = count; practiceArrangement = packed;
            practiceMatches = -1;
            if (HasAuthority)
            {
                if (TryGetPracticeMatches(local.Entry.PlayerId, packed, count, round.Circle, out int matches)) practiceMatches = matches;
            }
            else network.RequestPracticePreview(packed, count, round.Circle);
        }

        // This is an oracle only during practice. Never submit or score an attempt here.
        public bool TryGetPracticeMatches(int sender, ulong packed, byte count, int circle, out int matches)
        {
            matches = -1;
            var player = Find(sender);
            if (!HasAuthority || !IsPractice || Phase != MinigamePhase.Practice || Stage != StagePlacement ||
                circle != round.Circle || count != round.CanCount || count > CansOrderEntryNetState.MaxPackedCans ||
                player == null || !player.Entry.Alive || player.Entry.Solved || player.Entry.Confirmed) return false;
            practiceIncoming.Clear();
            for (int i = 0; i < count; i++) practiceIncoming.Add((byte)(packed >> (i * 8)));
            if (!IsPermutation(practiceIncoming, round.CanCount)) return false;
            matches = CountMatches(practiceIncoming);
            return true;
        }

        public void ReceivePracticePreview(ulong packed, byte count, int circle, int matches)
        {
            if (!IsPractice || Phase != MinigamePhase.Practice || circle != round.Circle || circle != practiceCircle ||
                count != practiceCount || packed != practiceArrangement || matches < 0 || matches > count) return;
            practiceMatches = matches;
        }
    }
}
