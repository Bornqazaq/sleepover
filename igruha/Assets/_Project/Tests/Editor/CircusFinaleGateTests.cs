using System.Collections.Generic;
using Igruha.Core.Minigame;
using Igruha.Minigames.Circus;
using NUnit.Framework;

namespace Igruha.Tests
{
    public sealed class CircusFinaleGateTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DelayedHitDeliversPhaseThenIndependentPayloadInEitherPacketOrder(bool resultsFirst)
        {
            var calls=new List<string>();
            var copy=new MinigameResults();
            bool final=false;
            var gate=new CircusFinaleGate(phase=>calls.Add(phase.ToString()),(results,series)=>
            { calls.Add("payload");copy.CopyFrom(results);final=series; });
            var incoming=Results();
            if(resultsFirst)gate.ReceiveResults(incoming,true,true,10);
            gate.ReceivePhase(MinigamePhase.Results,true,10.1f);
            if(!resultsFirst)gate.ReceiveResults(incoming,true,true,10.2f);
            incoming.Reset();incoming.Add(99,8,0,0);
            gate.Tick(true,10.5f);
            Assert.That(calls,Is.Empty,"A received results packet cannot cover the active local impact.");
            gate.Tick(false,10.6f);
            CollectionAssert.AreEqual(new[]{"Results","payload"},calls);
            Assert.That(final,Is.True);
            Assert.That(copy.GameKey,Is.EqualTo("CansOrder"));
            Assert.That(copy.MetricTitle,Is.EqualTo("БАНКИ"));
            Assert.That(copy.PlayerCount,Is.EqualTo(2));
            Assert.That(copy.CountsTowardSession,Is.True);
            Assert.That(copy.AreTeams,Is.True);
            Assert.That(copy.Entries.Count,Is.EqualTo(1));
            Assert.That(copy.Entries[0].PlayerId,Is.EqualTo(7));
            Assert.That(copy.Entries[0].Points,Is.EqualTo(3));
            Assert.That(copy.Entries[0].Total,Is.EqualTo(11));
            gate.Tick(false,11);
            Assert.That(calls.Count,Is.EqualTo(2),"Finishing must not deliver the queue twice.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisableResetOrNewPhaseDiscardsQueuedResults(bool newPhase)
        {
            var calls=new List<string>();
            var gate=new CircusFinaleGate(phase=>calls.Add(phase.ToString()),(_,__)=>calls.Add("payload"));
            gate.ReceiveResults(Results(),false,true,10);
            if(newPhase)gate.ReceivePhase(MinigamePhase.Tutorial,true,10.2f);
            else gate.Cancel();
            gate.Tick(false,20);
            CollectionAssert.AreEqual(newPhase?new[]{"Tutorial"}:new string[0],calls);
            Assert.That(gate.HasPending,Is.False);
        }

        [Test]
        public void NoLocalPresentationDeliversBothMessagesImmediately()
        {
            var calls=new List<string>();
            var gate=new CircusFinaleGate(phase=>calls.Add(phase.ToString()),(_,__)=>calls.Add("payload"));
            gate.ReceiveResults(Results(),false,false,10);
            Assert.That(calls,Is.EqualTo(new[]{"payload"}));
            gate.ReceivePhase(MinigamePhase.Results,false,10.1f);
            CollectionAssert.AreEqual(new[]{"payload","Results"},calls);
            Assert.That(gate.HasPending,Is.False);
        }

        [Test]
        public void StalledKnockoutCannotHoldResultsLongerThanTwoRealtimeSeconds()
        {
            var calls=new List<string>();
            var gate=new CircusFinaleGate(phase=>calls.Add(phase.ToString()),(_,__)=>calls.Add("payload"));
            gate.ReceivePhase(MinigamePhase.Results,true,10);
            gate.ReceiveResults(Results(),false,true,11.9f);
            Assert.That(calls,Is.Empty);
            gate.ReceivePhase(MinigamePhase.Results,true,11.99f);
            gate.Tick(true,12);
            CollectionAssert.AreEqual(new[]{"Results","payload"},calls);
            Assert.That(gate.HasPending,Is.False,"Later packets must not extend the original safety deadline.");
        }

        private static MinigameResults Results()
        {
            var results=new MinigameResults { GameKey="CansOrder",MetricTitle="БАНКИ",PlayerCount=2,CountsTowardSession=true,AreTeams=true };
            results.Add(7,1,3,11);
            return results;
        }
    }
}
