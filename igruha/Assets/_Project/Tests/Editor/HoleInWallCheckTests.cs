using Igruha.Minigames.HoleInWall;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>
    /// Разбор участника на стене (IGR-594): по чему сервер судит клиента, если
    /// отчёт владельца пришёл, опоздал, приехал раньше линии или не прошёл
    /// проверку правдоподобия. Сама сеть и физика — на стенде
    /// <c>BOT_ARGS=--bot-sloppy tools/autorun-series.sh 4 HoleInWall</c>.
    /// </summary>
    public sealed class HoleInWallCheckTests
    {
        [Test]
        public void ReportWithinSlackIsPlausible()
        {
            var seen = new Vector3(1f, 0.03f, 0f);
            Assert.That(HoleInWallCheck.Plausible(seen + new Vector3(HoleInWallCheck.ReportSlack - 0.01f, 0f, 0f), seen));
            Assert.That(HoleInWallCheck.Plausible(seen + new Vector3(0f, HoleInWallCheck.ReportSlack - 0.01f, 0f), seen));
        }

        [Test]
        public void ReportFartherThanSlackIsRejected()
        {
            var seen = new Vector3(1f, 0.03f, 0f);
            Assert.That(HoleInWallCheck.Plausible(seen + new Vector3(HoleInWallCheck.ReportSlack + 0.01f, 0f, 0f), seen), Is.False);
            Assert.That(HoleInWallCheck.Plausible(seen + new Vector3(0f, -HoleInWallCheck.ReportSlack - 0.01f, 0f), seen), Is.False,
                "из воды на платформу отчётом не перенестись");
        }

        [Test]
        public void DepthIsNotComparedBecauseVerdictIgnoresIt()
        {
            var seen = Vector3.zero;
            Assert.That(HoleInWallCheck.Plausible(new Vector3(0f, 0f, 3f), seen));
        }

        [Test]
        public void AwaitingCheckIsUndecidedUntilReportOrTimeout()
        {
            var check = new HoleInWallCheck();
            check.Await(default);

            Assert.That(check.AwaitingReport);
            Assert.That(check.Decided, Is.False);

            check.DecideByFallback(HoleInWallCheck.Source.Timeout, 1.5);
            Assert.That(check.Decided);
            Assert.That(check.AwaitingReport, Is.False);
            Assert.That(check.DecidedBy, Is.EqualTo(HoleInWallCheck.Source.Timeout));
            Assert.That(check.DecidedAt, Is.EqualTo(1.5));
        }

        [Test]
        public void EarlyReportIsKeptUntilLineAndClearedByDecision()
        {
            var check = new HoleInWallCheck();
            var position = new Vector3(0.4f, 0.03f, 0f);
            check.StashEarlyReport(position);

            Assert.That(check.HasEarlyReport);
            Assert.That(check.EarlyReport, Is.EqualTo(position));
            Assert.That(check.Decided, Is.False, "ранний отчёт разбирается на линии, а не при приезде");

            check.Decide(default, HoleInWallCheck.Source.Owner, 2d);
            Assert.That(check.HasEarlyReport, Is.False);
        }

        [Test]
        public void NewWallForgetsPreviousDecision()
        {
            var check = new HoleInWallCheck();
            check.Await(default);
            check.Decide(default, HoleInWallCheck.Source.Owner, 3d);

            check.Reset();

            Assert.That(check.Decided, Is.False);
            Assert.That(check.AwaitingReport, Is.False);
            Assert.That(check.HasEarlyReport, Is.False);
        }

        [Test]
        public void ServerCorrectionChangesPoseRowEvenWhenPoseIsTheSame()
        {
            // Клиент показал позу у себя, сервер отказал: в списке и так лежало
            // «без позы». Без счётчика строка не менялась — и поправка не ехала.
            var before = new HoleInWallPoseNetState { PlayerId = 2, Pose = 0, Revision = 0 };
            var corrected = new HoleInWallPoseNetState { PlayerId = 2, Pose = 0, Revision = 1 };

            Assert.That(before.Equals(corrected), Is.False);
        }
    }
}
