using System;
using System.Linq;
using Igruha.Minigames.CansOrder;
using NUnit.Framework;

namespace Igruha.Tests
{
    public sealed class CansOrderSolutionsTests
    {
        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        [TestCase(6)] [TestCase(7)] [TestCase(8)]
        public void EveryPlayerGetsUniqueValidAnswerAndCannotCopyAnother(int players)
        {
            for (int seed = 0; seed < 256; seed++)
            {
                var answers = CansOrderSolutions.Deal(5, players, new Random(seed));
                Assert.That(answers.Length, Is.EqualTo(players));
                for (int i = 0; i < players; i++)
                {
                    CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4 }, answers[i]);
                    for (int j = 0; j < players; j++)
                        if (i == j) Assert.That(CansOrderSolutions.CountMatches(answers[i], answers[j]), Is.EqualTo(5));
                        else Assert.That(CansOrderSolutions.CountMatches(answers[i], answers[j]), Is.LessThan(5));
                }
            }
        }

        [Test]
        public void CanDealEntirePermutationSpaceWithoutRepeats()
        {
            var answers = CansOrderSolutions.Deal(5, 120, new Random(44));
            Assert.That(answers.Select(a => string.Join(",", a)).Distinct().Count(), Is.EqualTo(120));
        }

        [Test]
        public void ImpossibleUniqueDealIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CansOrderSolutions.Deal(2, 8, new Random(1)));
        }
    }
}
