using System.Linq;
using Igruha.Core.Minigame;
using Igruha.Minigames.OneBullet;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class OneBulletRoundTests
    {
        private OneBulletRound game;
        [SetUp] public void SetUp(){game=new OneBulletRound();game.Reset(new[]{0,1,2,3},3,300,10);}
        [Test] public void WeaponHasCountdownThenSearchDelay()
        {Assert.False(game.Spawn(0,12.99));Assert.True(game.Spawn(0,13));Assert.False(game.Spawn(1,13));}
        [Test] public void SimultaneousTouchesHaveOneWinner()
        {game.Spawn(0,13);Assert.True(game.Take(2,13));Assert.False(game.Take(1,13));Assert.AreEqual(2,game.Holder);}
        [Test] public void MissConsumesRoundAndPreviousSpawnIsExcluded()
        {game.Spawn(0,13);game.Take(0,13);Assert.True(game.Fire(0,-1,14,5));Assert.False(game.Fire(0,1,14,5));Assert.False(game.Spawn(1,18.99));Assert.False(game.Spawn(0,19));Assert.True(game.Spawn(1,19));Assert.AreEqual(4,game.AliveCount);}
        [Test] public void HitCountsOnlyOnceAndVictimCannotPickUp()
        {game.Spawn(0,13);game.Take(0,13);game.Fire(0,1,14,5);Assert.AreEqual(1,game.Find(0).Kills);Assert.AreEqual(11,game.Find(1).Life);game.Spawn(1,19);Assert.False(game.Take(1,19));}
        [Test] public void OnlyOwnerCanShootAndDeadlineIsExclusive()
        {game.Spawn(0,13);game.Take(0,13);Assert.False(game.Fire(1,0,14,5));Assert.False(game.Fire(0,1,303,5));Assert.True(game.Fire(0,1,302.99,5));}
        [Test] public void LeavingOwnerSchedulesNewWeaponWithoutResurrection()
        {game.Spawn(0,13);game.Take(2,13);Assert.True(game.Leave(2,16,5));Assert.False(game.Leave(2,17,5));Assert.AreEqual(-1,game.Holder);Assert.AreEqual(21,game.SpawnAt);game.Spawn(1,21);Assert.False(game.Take(2,21));}
        [Test] public void HolderBeatsMoreKillsAtWhistle()
        {game.Spawn(0,13);game.Take(0,13);game.Fire(0,1,14,5);game.Spawn(1,19);game.Take(2,19);game.Finish(303);var r=new MinigameResults();game.Collect(r);Assert.AreEqual(2,r.Entries[0].PlayerId);Assert.AreEqual(0,r.Entries[1].PlayerId);}
        [Test] public void LastSurvivorBeatsDeadKiller()
        {game.Spawn(0,13);game.Take(0,13);game.Fire(0,1,14,5);game.Leave(0,20,5);game.Leave(2,20,5);game.Finish(20);Assert.AreEqual(3,game.Winner);}
        [TestCase(2)] [TestCase(4)] [TestCase(8)] public void AllParticipantsSharePlaceWhenNobodyFindsWeapon(int n)
        {game.Reset(Enumerable.Range(0,n).ToArray(),3,300,10);game.Finish(303);var r=new MinigameResults();game.Collect(r);Assert.AreEqual(n,r.Entries.Count);Assert.True(r.Entries.All(x=>x.Place==1));}
        [Test] public void DeadKillerRanksByTableBeforeLongerLivedZeroKills()
        {game.Spawn(0,13);game.Take(0,13);game.Fire(0,1,14,5);game.Leave(0,20,5);game.Finish(303);var r=new MinigameResults();game.Collect(r);Assert.AreEqual(0,r.Entries[0].PlayerId);Assert.AreEqual(2,r.Entries[1].Place);Assert.AreEqual(2,r.Entries[2].Place);Assert.AreEqual(4,r.Entries[3].Place);}
        [Test] public void InvalidAimCannotReachPhysics()
        {Assert.False(OneBulletMinigame.ValidDirection(new Vector3(float.NaN,0,1)));Assert.False(OneBulletMinigame.ValidDirection(Vector3.zero));Assert.False(OneBulletMinigame.ValidDirection(Vector3.forward*100));Assert.True(OneBulletMinigame.ValidDirection(Vector3.forward));}
        [Test] public void FinishedRoundRejectsFurtherChanges()
        {game.Spawn(0,13);game.Take(0,13);game.Finish(20);Assert.False(game.Fire(0,1,21,5));Assert.False(game.Leave(0,21,5));Assert.False(game.Spawn(1,21));}
    }
}
