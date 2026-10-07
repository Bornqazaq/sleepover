using Igruha.Minigames.CansOrder;
using NUnit.Framework;

namespace Igruha.Tests
{
    public sealed class CansDescentTests
    {
        private static CansOrderEntry Player(int matches, float height=1) =>
            new CansOrderEntry { Alive=true,Confirmed=true,Matches=matches,HeightFraction=height,BottomChances=2 };
        private static void Turn(CansOrderEntry[] entries,int circle=1)
        {
            int worst=CanOrderDescentRules.WorstScore(entries);
            for(int i=0;i<entries.Length;i++)CanOrderDescentRules.Apply(ref entries[i],worst,3,circle);
        }
        [Test] public void OnlyWorstCurrentResultDescendsIncludingTies()
        {
            var entries=new[]{Player(1),Player(3),Player(1)};
            entries[0].BestMatches=5;
            Turn(entries);
            Assert.That(entries[0].HeightFraction,Is.EqualTo(2f/3).Within(.001));
            Assert.That(entries[1].HeightFraction,Is.EqualTo(1));
            Assert.That(entries[2].HeightFraction,Is.EqualTo(entries[0].HeightFraction));
        }
        [Test] public void ReachingBottomThenTwoMoreLossesOpensHatch()
        {
            var entries=new[]{Player(0,1f/3),Player(1)};
            Turn(entries,3);
            Assert.That(entries[0].HeightFraction,Is.Zero);
            Assert.That(entries[0].BottomChances,Is.EqualTo(2));
            Assert.That(entries[0].Alive,Is.True);
            Turn(entries,4);
            Assert.That(entries[0].BottomChances,Is.EqualTo(1));
            Assert.That(entries[0].Alive,Is.True);
            Turn(entries,5);
            Assert.That(entries[0].Alive,Is.False);
            Assert.That(entries[0].Penalty,Is.EqualTo(CansOrderPenalty.Dropped));
            Assert.That(entries[0].EliminatedCircle,Is.EqualTo(5));
        }
        [Test] public void BetterTurnAtBottomPreservesChance()
        {
            var entries=new[]{Player(2,0),Player(0)};entries[0].BottomChances=1;
            Turn(entries);
            Assert.That(entries[0].Alive,Is.True);Assert.That(entries[0].BottomChances,Is.EqualTo(1));
        }
        [Test] public void SolvingOnLastChanceIsSafe()
        {
            var entries=new[]{Player(5,0),Player(3)};
            entries[0].Solved=true;entries[0].BottomChances=1;
            Turn(entries);
            Assert.That(entries[0].Alive,Is.True);Assert.That(entries[0].BottomChances,Is.EqualTo(1));
        }
        [Test] public void NoConfirmationIsWorseThanConfirmedZeroWithoutInventingMatches()
        {
            var entries=new[]{Player(0),Player(0)};entries[0].Confirmed=false;
            Turn(entries);
            Assert.That(entries[0].Penalty,Is.EqualTo(CansOrderPenalty.Descended));
            Assert.That(entries[1].Penalty,Is.EqualTo(CansOrderPenalty.None));
            Assert.That(entries[0].Confirmed,Is.False);
        }
        [Test] public void EveryoneSolvedAndEliminatedPlayersDoNotDescend()
        {
            var entries=new[]{Player(5),Player(0,0)};entries[0].Solved=true;entries[1].Alive=false;
            Turn(entries);
            Assert.That(entries[0].HeightFraction,Is.EqualTo(1));Assert.That(entries[1].BottomChances,Is.EqualTo(2));
        }
        [Test] public void SolvedOnBottomRanksAboveUnsolvedAtTop()
        {
            var solved=Player(5,0);solved.Solved=true;
            Assert.That(CanOrderRanking.CompareFinish(solved,Player(4)),Is.LessThan(0));
        }
        [Test] public void SurvivorsRankAboveEliminatedAndLaterEliminationRanksHigher()
        {
            var earlier=Player(4,0);earlier.Alive=false;earlier.EliminatedCircle=5;
            var later=Player(0,0);later.Alive=false;later.EliminatedCircle=6;
            Assert.That(CanOrderRanking.CompareFinish(Player(0,0),later),Is.LessThan(0));
            Assert.That(CanOrderRanking.CompareFinish(later,earlier),Is.LessThan(0));
        }
    }
}
