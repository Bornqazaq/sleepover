using System.Collections.Generic;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>
    /// Очки катки: одна формула, состав на старте раунда, журнал, таблица
    /// и чемпионы. Проверяется локальное табло — сетевое считает тем же
    /// <see cref="SessionScoring"/> и отличается только хранилищем.
    /// </summary>
    public sealed class SessionScoringTests
    {
        private GameObject host;
        private SessionManager session;

        [SetUp]
        public void Up()
        {
            ClearProfile();
            host = new GameObject("SessionManagerUnderTest");
            session = host.AddComponent<SessionManager>();
        }

        [TearDown]
        public void Down()
        {
            Object.DestroyImmediate(host);
            ClearProfile();
        }

        private static void ClearProfile()
        {
            LocalPartyProfile.History.Clear();
            LocalPartyProfile.Champions.Clear();
            LocalPartyProfile.RoundsPlayed = 0;
        }

        private SessionPlayer Register(int id, string name)
        {
            var player = new SessionPlayer(id, name);
            session.RegisterPlayer(player);
            return player;
        }

        private static MinigameResults Results(int playerCount, string game, params (int id, int place)[] places)
        {
            var results = new MinigameResults { PlayerCount = playerCount, GameKey = game };
            foreach (var (id, place) in places) results.Add(id, place);
            return results;
        }

        [TestCase(1, 8, 7)]
        [TestCase(8, 8, 0)]
        [TestCase(1, 2, 1)]
        [TestCase(2, 2, 0)]
        [TestCase(1, 1, 0)]
        [TestCase(0, 4, 3)]
        [TestCase(9, 4, 0)]
        public void PointsAreCountMinusPlaceNeverNegative(int place, int count, int expected) =>
            Assert.That(SessionScoring.PointsFor(place, count), Is.EqualTo(expected));

        [Test]
        public void PlayerCountPrefersRoundStartOverShrunkRoster()
        {
            var results = Results(8, "X", (0, 1));
            Assert.That(SessionScoring.PlayerCountFor(results, 3), Is.EqualTo(8));

            var unknown = Results(0, "X", (0, 1), (1, 2), (2, 3), (3, 4));
            Assert.That(SessionScoring.PlayerCountFor(unknown, 2), Is.EqualTo(4));
        }

        [Test]
        public void WinnerKeepsFullPointsWhenOthersLeftMidMatch()
        {
            // Стартовали восемь, шестеро вышли: победитель берёт 7, как и без уходов (IGR-372).
            var winner = Register(0, "Хозяин");
            var second = Register(1, "Друг");
            var results = Results(8, "CansOrder", (0, 1), (1, 2));

            session.ReportResults(results);

            Assert.That(winner.Score, Is.EqualTo(7));
            Assert.That(second.Score, Is.EqualTo(6));
            Assert.That(results.Awarded, Is.True);
            Assert.That(results.Entries[0].Points, Is.EqualTo(7));
            Assert.That(results.Entries[0].Total, Is.EqualTo(7));
            Assert.That(results.Entries[1].Total, Is.EqualTo(6));
        }

        [Test]
        public void EveryPlayedGameAccumulatesAndIsJournaled()
        {
            var a = Register(0, "A");
            var b = Register(1, "B");
            var c = Register(2, "C");

            session.ReportResults(Results(3, "Stopwatch", (0, 1), (1, 2), (2, 3)));
            session.ReportResults(Results(3, "Exam", (0, 3), (1, 1), (2, 2)));
            session.ReportResults(Results(3, "Infection", (0, 1), (1, 1), (2, 3)));

            Assert.That(a.Score, Is.EqualTo(2 + 0 + 2));
            Assert.That(b.Score, Is.EqualTo(1 + 2 + 2));
            Assert.That(c.Score, Is.EqualTo(0 + 1 + 0));
            Assert.That(session.RoundsPlayed, Is.EqualTo(3));
            Assert.That(session.History.Count, Is.EqualTo(9));
            Assert.That(session.History[3].GameKey, Is.EqualTo("Exam"));
            Assert.That(session.History[3].Round, Is.EqualTo(2));
            Assert.That(session.History[8].PlayerId, Is.EqualTo(2));
            Assert.That(session.History[8].Points, Is.EqualTo(0));
        }

        [Test]
        public void UnknownPlayerInResultsIsSkippedWithoutBreakingOthers()
        {
            var a = Register(0, "A");
            var results = Results(4, "X", (7, 1), (0, 2));

            session.ReportResults(results);

            Assert.That(a.Score, Is.EqualTo(2));
            Assert.That(results.Entries[0].Points, Is.EqualTo(0));
            Assert.That(session.History.Count, Is.EqualTo(1));
            Assert.That(session.RoundsPlayed, Is.EqualTo(1));
        }

        [Test]
        public void ResetScoresClearsScoreJournalAndChampions()
        {
            var a = Register(0, "A");
            Register(1, "B");
            session.ReportResults(Results(2, "X", (0, 1), (1, 2)));
            session.CompleteSeries();
            Assert.That(session.Champions, Is.EquivalentTo(new[] { 0 }));

            session.ResetScores();

            Assert.That(a.Score, Is.EqualTo(0));
            Assert.That(session.History, Is.Empty);
            Assert.That(session.RoundsPlayed, Is.EqualTo(0));
            Assert.That(session.Champions, Is.Empty);
            Assert.That(session.IsChampion(0), Is.False);
        }

        [Test]
        public void CompleteSeriesPicksSingleChampionOrSharesOnTie()
        {
            Register(0, "A"); Register(1, "B"); Register(2, "C");
            session.ReportResults(Results(3, "X", (0, 1), (1, 2), (2, 3)));
            session.ReportResults(Results(3, "Y", (0, 2), (1, 1), (2, 3)));
            // A = 2 + 1 = 3, B = 1 + 2 = 3, C = 0.
            session.CompleteSeries();
            Assert.That(session.Champions, Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(session.IsChampion(2), Is.False);

            session.ReportResults(Results(3, "Z", (0, 1), (1, 3), (2, 2)));
            session.CompleteSeries();
            Assert.That(session.Champions, Is.EquivalentTo(new[] { 0 }));
        }

        [Test]
        public void CompleteSeriesWithoutPointsHasNoChampion()
        {
            Register(0, "A"); Register(1, "B");
            session.CompleteSeries();
            Assert.That(session.Champions, Is.Empty);
        }

        [Test]
        public void StandingsSharePlacesAndCountWins()
        {
            var players = new List<SessionPlayer>
            {
                new SessionPlayer(0, "A") { Score = 5 },
                new SessionPlayer(1, "B") { Score = 9 },
                new SessionPlayer(2, "C") { Score = 5 },
                new SessionPlayer(3, "D") { Score = 0 },
            };
            var history = new List<SessionRoundRecord>
            {
                new SessionRoundRecord(1, "X", 1, 1, 3), new SessionRoundRecord(1, "X", 2, 2, 2),
                new SessionRoundRecord(2, "Y", 2, 1, 3), new SessionRoundRecord(2, "Y", 1, 2, 2),
                new SessionRoundRecord(3, "Z", 1, 1, 3), new SessionRoundRecord(3, "Z", 0, 2, 2),
            };

            var standings = new SessionStandings();
            standings.Rebuild(players, history);

            Assert.That(standings.RoundsPlayed, Is.EqualTo(3));
            Assert.That(standings.LeaderCount, Is.EqualTo(1));
            Assert.That(standings.IsTie, Is.False);
            Assert.That(standings.Entries[0].PlayerId, Is.EqualTo(1));
            Assert.That(standings.Entries[0].Place, Is.EqualTo(1));
            Assert.That(standings.Entries[0].Wins, Is.EqualTo(2));
            // Общее второе место, выше тот, у кого есть победа.
            Assert.That(standings.Entries[1].PlayerId, Is.EqualTo(2));
            Assert.That(standings.Entries[1].Place, Is.EqualTo(2));
            Assert.That(standings.Entries[2].PlayerId, Is.EqualTo(0));
            Assert.That(standings.Entries[2].Place, Is.EqualTo(2));
            // Следующее место со сдвигом на размер группы, а не на единицу.
            Assert.That(standings.Entries[3].Place, Is.EqualTo(4));
            Assert.That(standings.IsLeader(1), Is.True);
            Assert.That(standings.IsLeader(2), Is.False);
        }

        [Test]
        public void StandingsWithAllZeroHaveNoLeader()
        {
            var players = new List<SessionPlayer> { new SessionPlayer(0, "A"), new SessionPlayer(1, "B") };
            var standings = new SessionStandings();
            standings.Rebuild(players, null);
            Assert.That(standings.LeaderCount, Is.EqualTo(0));
            Assert.That(standings.Entries[0].Place, Is.EqualTo(1));
            Assert.That(standings.IsLeader(0), Is.False);
        }

        [Test]
        public void ScoreRankingNoWinnerUsesRoundStartCountForLastPlace()
        {
            // Пятеро стартовали, двое вышли, у оставшихся нули: последнее место — пятое, очков ноль.
            var results = new MinigameResults { PlayerCount = 5 };
            var ranking = new ScoreRanking();
            ranking.Add(0, 0); ranking.Add(1, 0); ranking.Add(2, 0);
            ranking.Build(results);

            Assert.That(results.PlayerCount, Is.EqualTo(5));
            foreach (var entry in results.Entries)
            {
                Assert.That(entry.Place, Is.EqualTo(5));
                Assert.That(SessionScoring.PointsFor(entry.Place, results.PlayerCount), Is.EqualTo(0));
            }
        }

        [Test]
        public void ResultsCopyKeepsAwardsCountAndKey()
        {
            var source = new MinigameResults { PlayerCount = 6, GameKey = "MemoryRun" };
            source.Add(3, 1, 5, 12);
            source.Add(4, 2, 4, 4);

            var copy = new MinigameResults();
            copy.CopyFrom(source);

            Assert.That(copy.Awarded, Is.True);
            Assert.That(copy.PlayerCount, Is.EqualTo(6));
            Assert.That(copy.GameKey, Is.EqualTo("MemoryRun"));
            Assert.That(copy.Entries[0].Total, Is.EqualTo(12));
            Assert.That(copy.IndexOf(4), Is.EqualTo(1));

            copy.Clear();
            Assert.That(copy.Entries, Is.Empty);
            Assert.That(copy.PlayerCount, Is.EqualTo(6), "Clear оставляет состав — его ставит контроллер до сбора мест");
            copy.Reset();
            Assert.That(copy.PlayerCount, Is.EqualTo(0));
        }

        [TestCase(1, "игра")]
        [TestCase(2, "игры")]
        [TestCase(5, "игр")]
        [TestCase(11, "игр")]
        [TestCase(21, "игра")]
        public void GamesWordDeclines(int count, string expected) =>
            Assert.That(PartySeries.GamesWord(count), Is.EqualTo(expected));
    }
}
