using System;
using System.Collections.Generic;
using System.Text;
using Igruha.Core.Minigame;
using TMPro;
using UnityEngine;

namespace Igruha.Minigames.BelieveOrNot
{
    public sealed partial class BelieveOrNotMinigame
    {
        private BelieveTournament tournament;
        private BelieveTournamentHud tournamentHud;
        private int pendingWinner = -1, committedRound;
        private bool finalIntroPending;
        private int streakAnnouncedRound;
        private readonly List<BelieveEntry> standings = new List<BelieveEntry>(8);
        public BelieveTournamentState TournamentState => match.Tournament;
        public event Action FinalAnnounced;
        public event Action ChampionAnnounced;
        public event Action PredictionStreakAwarded;
        public IReadOnlyList<BelieveEntry> TournamentEntries => entries;

        private void InitializeTournamentHud()
        {
            var root = new GameObject("TournamentHud", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            tournamentHud = root.AddComponent<BelieveTournamentHud>();
            var sample = seatHud != null ? seatHud.GetComponentInChildren<TMP_Text>(true) : null;
            var plate = seatHud != null ? seatHud.GetComponentInChildren<UnityEngine.UI.Image>(true) : null;
            tournamentHud.Initialize(sample != null ? sample.font : TMP_Settings.defaultFontAsset,
                plate != null ? plate.sprite : null);
        }

        private bool PrepareTournamentRound(int round)
        {
            if (tournament == null) return false;
            var previousPhase = match.Tournament.Phase;
            if (!tournament.Next(out BelieveTournamentHand hand)) { ShowChampion(); return false; }
            match.Tournament = tournament.State;
            match.RoundNumber = round;
            match.Seat0PlayerId = hand.A; match.Seat1PlayerId = hand.B;
            match.KnowerPlayerId = hand.Knower;
            match.DeciderPlayerId = hand.Knower == hand.A ? hand.B : hand.A;
            match.IsRematch = hand.Rematch;
            match.ForfeitWinner = -1;
            pendingWinner = -1;
            finalIntroPending = match.Tournament.Phase == BelieveTournamentPhase.Final && previousPhase != BelieveTournamentPhase.Final;
            return true;
        }

        private void CommitDuelResult()
        {
            if (!HasAuthority || tournament == null || pendingWinner < 0 || committedRound == match.RoundNumber) return;
            if (!tournament.Record(pendingWinner)) return;
            committedRound = match.RoundNumber;
            MarkSeated(match.Seat0PlayerId); MarkSeated(match.Seat1PlayerId);
            match.Tournament = tournament.State;
            if (BelieveOathRules.IsClaim(match.Oath))
            {
                int index = IndexOf(match.KnowerPlayerId);
                if (index >= 0)
                {
                    var entry = entries[index];
                    bool truth = BelieveOathRules.IsTrue(match.Oath, winningSeat == SeatOf(match.KnowerPlayerId));
                    entry.OathHistory = (byte)(((entry.OathHistory << 1) | (truth ? 1 : 0)) & 7);
                    entry.OathCount = (byte)Mathf.Min(BelieveTournament.HistoryLength, entry.OathCount + 1);
                    entries[index] = entry;
                }
            }
            PublishMatch(); PublishEntries();
        }

        private void RecordPredictionStreaks()
        {
            if (!HasAuthority) return;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!entry.Present || entry.PlayerId == match.Seat0PlayerId || entry.PlayerId == match.Seat1PlayerId) continue;
                bool correct = predictions.ChoiceOf(entry.PlayerId) == predictionResults.WinnerId;
                entry.PredictionStreak = correct ? entry.PredictionStreak + 1 : 0;
                if (correct) entry.CorrectPredictions++;
                entry.BestPredictionStreak = Mathf.Max(entry.BestPredictionStreak, entry.PredictionStreak);
                entry.PredictionRound = match.RoundNumber;
                entries[i] = entry;
            }
            PublishEntries();
        }

        private void ForfeitCurrentHand(int departed)
        {
            if (match.Resolved || match.Cancelled) return;
            int winner = departed == match.Seat0PlayerId ? match.Seat1PlayerId : match.Seat0PlayerId;
            if (IndexOf(winner) < 0 || !entries[IndexOf(winner)].Present) return;
            if (!tournament.Record(winner, true)) return;
            match.ForfeitWinner = winner;
            match.Cancelled = true; match.Resolved = true;
            resolvedRound = committedRound = match.RoundNumber;
            match.Tournament = tournament.State;
            pendingWinner = -1;
            PublishMatch(); PublishEntries();
            stageState.EnterStage(BelieveStage.Cancelled, config.ReactionSeconds);
        }

        private void ShowChampion()
        {
            match.Tournament = tournament.State;
            PublishMatch(); PublishEntries();
            if (match.Tournament.Champion < 0) { EndMinigame(); return; }
            stageState.EnterStage(BelieveStage.Champion, config.ChampionSeconds);
        }

        private void RefreshTournamentView()
        {
            if (tournamentHud == null) return;
            if (!Phase.IsGameplay()) { tournamentHud.Close(); return; }
            // Репликация счёта может опередить локальную анимацию крышек.
            if (Stage == BelieveStage.Reveal && !predictionBoxesOpened) return;
            standings.Clear(); standings.AddRange(entries);
            standings.Sort((a, b) => a.Present != b.Present ? (a.Present ? -1 : 1) :
                b.QualificationWins != a.QualificationWins ? b.QualificationWins.CompareTo(a.QualificationWins) : a.PlayerId.CompareTo(b.PlayerId));
            var rows = new StringBuilder();
            foreach (var entry in standings)
            {
                bool local = entry.PlayerId == LocalPlayerId;
                rows.Append(local ? "<color=#F8D18A>" : entry.Present ? "<color=#E8E2D5>" : "<color=#777D7B>");
                rows.Append(ShortPredictionName(entry.PlayerId)).Append(local ? " • вы" : "");
                rows.Append("<pos=64%>").Append(entry.QualificationWins).Append("<pos=82%>")
                    .Append(entry.QualificationPlayed).Append("/4</color>\n");
            }
            var t = match.Tournament;
            string title = t.Phase == BelieveTournamentPhase.Qualification ? "ДОРОГА В ФИНАЛ" :
                t.Phase == BelieveTournamentPhase.Playoff ? "ЗА ПУТЁВКУ В ФИНАЛ" : "РЕЗУЛЬТАТЫ ОТБОРА";
            string footer = t.Phase == BelieveTournamentPhase.Qualification ? "Две лучшие позиции → финал" :
                t.Phase == BelieveTournamentPhase.Playoff ? "Равный счёт решит дуэль" : "Отдельный счёт · до двух побед";
            int own = IndexOf(LocalPlayerId);
            if (own >= 0 && predictionBoxesOpened && entries[own].PredictionRound == match.RoundNumber &&
                entries[own].PredictionStreak >= 3 && streakAnnouncedRound != match.RoundNumber)
            { streakAnnouncedRound = match.RoundNumber; PredictionStreakAwarded?.Invoke(); }
            string streak = own >= 0 ? $"Ваш прогноз: серия {entries[own].PredictionStreak} · рекорд {entries[own].BestPredictionStreak}" : "Прогнозы зрителей";
            tournamentHud.ShowStandings(title, rows.ToString(), footer, streak,
                Stage != BelieveStage.FinalIntro && Stage != BelieveStage.Champion && (SeatOf(LocalPlayerId) < 0 || Stage == BelieveStage.Reaction));
            tournamentHud.ShowReputation(HistoryLine(match.KnowerPlayerId), Stage == BelieveStage.Oath || Stage == BelieveStage.Persuasion);
            tournamentHud.ShowFinalScore(t.FinalA >= 0 && (t.Phase == BelieveTournamentPhase.Final || t.Phase == BelieveTournamentPhase.Finished),
                ShortPredictionName(t.FinalA), ShortPredictionName(t.FinalB), t.WinsA, t.WinsB);
            if (Stage == BelieveStage.FinalIntro)
                tournamentHud.ShowBanner("БОЛЬШОЙ ФИНАЛ", ShortPredictionName(t.FinalA) + "  /  " + ShortPredictionName(t.FinalB), "До двух побед · роли меняются каждый кон", false);
            else if (Stage == BelieveStage.Champion)
                tournamentHud.ShowBanner("ПОБЕДИТЕЛЬ ТУРНИРА", ShortPredictionName(t.Champion), BestPredictorLine(), true);
            else tournamentHud.HideBanner();
        }

        private string HistoryLine(int id)
        {
            int index = IndexOf(id);
            if (index < 0 || entries[index].OathCount == 0) return "РЕПУТАЦИЯ · ещё нет раскрытых клятв";
            var e = entries[index]; var b = new StringBuilder("ЕГО ПРОШЛЫЕ КЛЯТВЫ · ");
            for (int i = e.OathCount - 1; i >= 0; i--)
            {
                if (i != e.OathCount - 1) b.Append("  /  ");
                b.Append((e.OathHistory & (1 << i)) != 0 ? "<color=#B3DDC5>ПРАВДА</color>" : "<color=#F39A85>ЛОЖЬ</color>");
            }
            return b.ToString();
        }

        private string BestPredictorLine()
        {
            int best = 0;
            foreach (var e in entries) best = Mathf.Max(best, e.BestPredictionStreak);
            if (best == 0) return "Первое место · личный турнир";
            int leaders = 0;
            foreach (var e in entries) if (e.BestPredictionStreak == best) leaders++;
            var b = new StringBuilder(leaders == 1 ? "ЛУЧШИЙ ПРЕДСКАЗАТЕЛЬ · " : "ЛУЧШИЕ ПРЕДСКАЗАТЕЛИ · ");
            bool first = true;
            foreach (var e in entries) if (e.BestPredictionStreak == best)
            { if (!first) b.Append(", "); b.Append(ShortPredictionName(e.PlayerId)); first = false; }
            return b.Append(" · серия ").Append(best).ToString();
        }
    }
}
