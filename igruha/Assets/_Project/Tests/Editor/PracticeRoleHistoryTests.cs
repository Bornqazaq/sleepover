using Igruha.Core.Session;
using NUnit.Framework;

namespace Igruha.Tests
{
    public sealed class PracticeRoleHistoryTests
    {
        [Test]
        public void PracticeDoesNotConsumeLastUnplayedRole()
        {
            var history = new SpecialRoleHistory();
            var players = new[] { new SessionPlayer(7,"A"), new SessionPlayer(9,"B") };
            history.Mark("hunter", 7);
            Assert.That(history.Pick("hunter",players,record:false), Is.EqualTo(9));
            Assert.That(history.HasPlayed("hunter",9), Is.False);
            Assert.That(history.Pick("hunter",players), Is.EqualTo(9));
        }

        [Test]
        public void PracticeDoesNotClearCompletedRoleRotation()
        {
            var history = new SpecialRoleHistory();
            var players = new[] { new SessionPlayer(7,"A"), new SessionPlayer(9,"B") };
            history.Mark("hunter",7); history.Mark("hunter",9);
            history.Pick("hunter",players,record:false);
            Assert.That(history.HasPlayed("hunter",7), Is.True);
            Assert.That(history.HasPlayed("hunter",9), Is.True);
        }
    }
}
