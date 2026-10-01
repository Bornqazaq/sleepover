using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Items;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.CarryItem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Igruha.Tests.PlayMode
{
    public sealed class CartClaimTests
    {
        private readonly List<Object> objects = new List<Object>();
        private readonly Dictionary<PlayerController,TeamSide> teams = new Dictionary<PlayerController,TeamSide>();
        private WaterCart cart;
        private CarryItemConfig config;
        [SetUp] public void Setup()
        {
            config=ScriptableObject.CreateInstance<CarryItemConfig>();objects.Add(config);
            var root=new GameObject("Claim cart");objects.Add(root);root.transform.position=Vector3.up*1500;
            root.AddComponent<BoxCollider>();var body=root.AddComponent<Rigidbody>();body.constraints=RigidbodyConstraints.FreezeAll;
            root.AddComponent<MultiCarryObject>();cart=root.AddComponent<WaterCart>();
            cart.Initialize(config,TeamSide.A,4,-100,root.transform.position,Quaternion.identity);
            cart.ConfigureClaims(p=>teams.TryGetValue(p,out var t)?t:TeamSide.None,t=>t==TeamSide.A?4:1,p=>true);
            cart.ChangeWater(150,WaterLossReason.Filled);
        }
        [TearDown] public void Cleanup()
        {
            cart.Carry.ReleaseAll(CarryReleaseReason.RoundEnded);
            for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)Object.DestroyImmediate(objects[i]);
            objects.Clear();teams.Clear();
        }
        private PlayerController Player(TeamSide side,int slot=0)
        {
            var root=new GameObject("Claim player");objects.Add(root);root.SetActive(false);
            var cfg=ScriptableObject.CreateInstance<CharacterConfig>();objects.Add(cfg);
            var p=root.AddComponent<PlayerController>();
            typeof(PlayerController).GetField("config",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,cfg);
            p.enabled=false;root.SetActive(true);root.transform.position=cart.Carry.StationOf(slot);
            p.GetComponent<Rigidbody>().constraints=RigidbodyConstraints.FreezeAll;
            teams.Add(p,side);return p;
        }
        [UnityTest] public IEnumerator CaptureRecolorsAndShrinksToUnevenCrewWithoutLosingWater()
        {
            var thief=Player(TeamSide.B,3);Assert.That(cart.Carry.TryGrab(thief),Is.True);
            yield return null;
            Assert.That(cart.Team,Is.EqualTo(TeamSide.A));Assert.That(cart.ControlTeam,Is.EqualTo(TeamSide.B));
            Assert.That(cart.Carry.HandleCount,Is.EqualTo(1));Assert.That(cart.Carry.IsCarriedBy(thief),Is.True);
            Assert.That(cart.Water,Is.EqualTo(150));
        }
        [Test] public void EnemyCannotJoinOccupiedCartButAllyCan()
        {
            Assert.That(cart.Carry.TryGrab(Player(TeamSide.A)),Is.True);
            Assert.That(cart.Carry.TryGrab(Player(TeamSide.B,1)),Is.False);
            Assert.That(cart.Carry.TryGrab(Player(TeamSide.A,1)),Is.True);
        }
        [Test] public void UnassignedOrBusyPlayerCannotCapture()
        {
            Assert.That(cart.Carry.TryGrab(Player(TeamSide.None)),Is.False);
            cart.ConfigureClaims(p=>TeamSide.B,t=>1,p=>false);
            Assert.That(cart.Carry.TryGrab(Player(TeamSide.B)),Is.False);
        }
        [UnityTest] public IEnumerator ReleasedCartCanBeReclaimedAndHomeRestoresOriginalTeam()
        {
            Assert.That(cart.Carry.TryGrab(Player(TeamSide.B)),Is.True);yield return null;
            cart.Carry.ReleaseAll(CarryReleaseReason.LetGo);
            Assert.That(cart.ControlTeam,Is.EqualTo(TeamSide.B));
            Assert.That(cart.Carry.TryGrab(Player(TeamSide.A)),Is.True);yield return null;
            Assert.That(cart.ControlTeam,Is.EqualTo(TeamSide.A));Assert.That(cart.Carry.HandleCount,Is.EqualTo(4));
            cart.Carry.ReleaseAll(CarryReleaseReason.LetGo);cart.Carry.TryGrab(Player(TeamSide.B));
            cart.ReturnHome();yield return null;
            Assert.That(cart.ControlTeam,Is.EqualTo(TeamSide.A));Assert.That(cart.Carry.CarrierCount,Is.Zero);
        }
        [UnityTest] public IEnumerator StolenWaterGoesOnlyToCapturingTeamAndStopsOnReclaim()
        {
            var root=new GameObject("Claim tank");objects.Add(root);root.transform.position=cart.transform.position;
            var zone=root.AddComponent<BoxCollider>();zone.isTrigger=true;zone.size=Vector3.one*4;
            var tank=root.AddComponent<WaterTank>();tank.Configure(config,TeamSide.B);
            Physics.SyncTransforms();yield return new WaitForSeconds(.2f);Assert.That(tank.Water,Is.Zero);
            cart.Carry.TryGrab(Player(TeamSide.B));yield return new WaitForSeconds(.25f);
            Assert.That(tank.Water,Is.GreaterThan(0));Assert.That(tank.Water,Is.LessThan(150));
            Assert.That(tank.Water+cart.Water,Is.EqualTo(150));
            cart.Carry.ReleaseAll(CarryReleaseReason.LetGo);cart.Carry.TryGrab(Player(TeamSide.A));
            int before=tank.Water;yield return new WaitForSeconds(.2f);Assert.That(tank.Water,Is.EqualTo(before));
            Assert.That(cart.IsPouring,Is.False);
        }
    }
}
