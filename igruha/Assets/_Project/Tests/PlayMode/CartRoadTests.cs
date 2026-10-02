using System.Collections;
using System.Collections.Generic;
using Igruha.Core.Items;
using Igruha.Core.Session;
using Igruha.Minigames.CarryItem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Igruha.Tests.PlayMode
{
    public sealed class CartRoadTests
    {
        private readonly List<Object> objects = new List<Object>();
        private WaterCart cart;
        private Rigidbody body;
        private float beforeScale;
        private bool background;
        [SetUp] public void SetUp()
        {
            beforeScale=Time.timeScale;Time.timeScale=3; background=Application.runInBackground;Application.runInBackground=true;
            var config=ScriptableObject.CreateInstance<CarryItemConfig>();objects.Add(config);
            var root=new GameObject("Road test cart");objects.Add(root);root.transform.position=new Vector3(-2,1000,0);
            root.transform.rotation=Quaternion.Euler(0,90,0); root.AddComponent<BoxCollider>();
            body=root.AddComponent<Rigidbody>();body.useGravity=false;
            root.AddComponent<MultiCarryObject>();cart=root.AddComponent<WaterCart>();
            cart.Initialize(config,TeamSide.A,1,-100,root.transform.position,root.transform.rotation);
            cart.Carry.enabled=false;cart.enabled=false;body.linearDamping=0;body.constraints=RigidbodyConstraints.FreezePositionY|RigidbodyConstraints.FreezeRotation;
            cart.ChangeWater(150,WaterLossReason.Filled);
        }
        [TearDown] public void TearDown()
        { foreach(var o in objects) if(o!=null) Object.DestroyImmediate(o);objects.Clear();Time.timeScale=beforeScale;Application.runInBackground=background; }
        private CartRoadJoint Joint(float x, float z=0)
        { var g=new GameObject("Visible road seam");objects.Add(g);g.transform.position=new Vector3(x,1000,z);var j=g.AddComponent<CartRoadJoint>();j.Configure(5.4f,1);return j; }
        [Test] public void CrossingsRespectWidthHeightAndBothTravelDirections()
        {
            var j=Joint(0);float approach;
            Assert.That(j.Crossed(new Vector3(-1,1000,0),new Vector3(1,1000,0),out approach),Is.True);
            Assert.That(approach,Is.EqualTo(1));
            Assert.That(j.Crossed(new Vector3(1,1000,0),new Vector3(-1,1000,0),out approach),Is.True);
            Assert.That(j.Crossed(new Vector3(-1,1001,0),new Vector3(1,1001,0),out approach),Is.False,"airborne");
            Assert.That(j.Crossed(new Vector3(-1,1000,3),new Vector3(1,1000,3),out approach),Is.False,"bypass");
            Assert.That(j.Crossed(new Vector3(0,1000,0),new Vector3(1,1000,0),out approach),Is.False,"no duplicate on line");
        }
        [UnityTest] public IEnumerator FastSeriesSpillsOnlyThroughVisibleRimAndThenSettles()
        {
            for(int i=0;i<6;i++)Joint(i*1.55f);
            float integral=0;bool lifted=false, rocked=false;
            for(int i=0;i<300;i++)
            {
                body.linearVelocity=Vector3.right*2.3f;yield return new WaitForFixedUpdate();
                var s=cart.Stability.State;integral+=s.Outflow*Time.fixedDeltaTime;
                lifted|=s.RoadHop>.008f;rocked|=s.BodySlope.magnitude>.035f;
            }
            Debug.Log("ROAD fast full remaining="+cart.Water+" visible="+integral);
            Assert.That(cart.Water,Is.LessThan(145));Assert.That(lifted&&rocked,Is.True);
            Assert.That(150-cart.Water,Is.EqualTo(Mathf.FloorToInt(integral)).Within(1));
            body.linearVelocity=Vector3.zero;yield return new WaitForSeconds(5);
            Assert.That(cart.Stability.State.Outflow,Is.Zero);Assert.That(cart.Stability.State.BodySlope.magnitude,Is.LessThan(.005f));
        }
        [UnityTest] public IEnumerator SlowSeriesPreservesFullCart()
        {
            for(int i=0;i<3;i++)Joint(i*.8f);
            for(int i=0;i<350;i++){body.linearVelocity=Vector3.right*.7f;yield return new WaitForFixedUpdate();}
            Debug.Log("ROAD slow full remaining="+cart.Water);Assert.That(cart.Water,Is.EqualTo(150));
        }
        [UnityTest] public IEnumerator SoloHoldingForwardPaysForSixRoughJoints()
        {
            for(int i=0;i<6;i++)Joint(i*1.55f);
            for(int i=0;i<390;i++){body.linearVelocity=Vector3.right*1.6f;yield return new WaitForFixedUpdate();}
            Debug.Log("ROAD solo full remaining="+cart.Water);
            Assert.That(cart.Water,Is.LessThanOrEqualTo(115),"Solo speed must not bypass the road risk");
            Assert.That(cart.Water,Is.GreaterThan(40),"A mistake must still leave a useful delivery");
        }
        [UnityTest] public IEnumerator RepeatedCountersteeringSpillsAndStraightTravelSettles()
        {
            for(int i=0;i<500;i++)
            {
                float angle=Mathf.Sin(i*Time.fixedDeltaTime*2f)*.8f;
                body.linearVelocity=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*1.6f;
                yield return new WaitForFixedUpdate();
            }
            Debug.Log("TURN solo remaining="+cart.Water);
            Assert.That(cart.Water,Is.LessThanOrEqualTo(115));
            for(int i=0;i<250;i++){body.linearVelocity=Vector3.right*1.6f;yield return new WaitForFixedUpdate();}
            Assert.That(cart.Stability.State.Outflow,Is.Zero);
            Assert.That(cart.Stability.State.BodySlope.magnitude,Is.LessThan(.005f));
        }
        [UnityTest] public IEnumerator BroadContinuousCurvePreservesFullCart()
        {
            for(int i=0;i<300;i++)
            {
                float angle=i*Time.fixedDeltaTime*.32f;
                body.linearVelocity=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*1.6f;
                yield return new WaitForFixedUpdate();
            }
            Assert.That(cart.Water,Is.EqualTo(150),"Normal gentle steering must remain useful on a smooth route");
        }
        [UnityTest] public IEnumerator HalfCartHasMoreFreeboardOnFastSeries()
        {
            cart.ChangeWater(-75,WaterLossReason.Tilt);
            for(int i=0;i<6;i++)Joint(i*1.55f);
            for(int i=0;i<300;i++){body.linearVelocity=Vector3.right*2.3f;yield return new WaitForFixedUpdate();}
            Debug.Log("ROAD fast half remaining="+cart.Water);Assert.That(cart.Water,Is.GreaterThanOrEqualTo(70));
        }
        [UnityTest] public IEnumerator ParallelBypassAndTeleportDoNotCreateRoadImpulses()
        {
            for(int i=0;i<6;i++)Joint(i*1.55f,6);
            for(int i=0;i<100;i++){body.linearVelocity=Vector3.right*2.3f;yield return new WaitForFixedUpdate();}
            Assert.That(cart.Water,Is.EqualTo(150));Assert.That(cart.Stability.State.RoadHop,Is.Zero);
            Joint(cart.transform.position.x+3);
            body.position+=Vector3.right*7;yield return new WaitForFixedUpdate();
            Assert.That(cart.Stability.State.RoadHop,Is.Zero);Assert.That(cart.Stability.State.Wave.magnitude,Is.LessThan(.02f));
        }
        [Test] public void RepeatedImpactsBuildEnergyAndQuietMotionDampsIt()
        {
            var r=new CartRoadResponse();var first=r.Strike(Vector2.up,Vector2.right,2,1,1).magnitude;
            for(int i=0;i<8;i++)r.Step(.02f);
            var second=r.Strike(Vector2.up,Vector2.right,2,1,1).magnitude;
            Assert.That(second,Is.GreaterThan(first));
            for(int i=0;i<300;i++)r.Step(.02f);
            Assert.That(r.Energy,Is.Zero);Assert.That(r.Slope.magnitude,Is.LessThan(.002f));Assert.That(Mathf.Abs(r.Hop),Is.LessThan(.001f));
        }
        [UnityTest] public IEnumerator BrokenConcreteHasRealWheelResponseInsideItsVisibleFootprint()
        {
            var root=new GameObject("Broken concrete");objects.Add(root);root.transform.position=new Vector3(0,1000,0);
            var patch=root.AddComponent<CartRoughSurface>();float approach,severity;
            Assert.That(patch.Crossed(new Vector3(-.2f,1000,0),new Vector3(.1f,1000,0),out approach,out severity),Is.True);
            Assert.That(patch.Crossed(new Vector3(-.2f,1000,2),new Vector3(.1f,1000,2),out approach,out severity),Is.False);
            Assert.That(patch.Crossed(new Vector3(-.2f,1001,0),new Vector3(.1f,1001,0),out approach,out severity),Is.False);
            float hop=0;
            for(int i=0;i<160;i++){body.linearVelocity=Vector3.right*1.6f;yield return new WaitForFixedUpdate();hop=Mathf.Max(hop,cart.Stability.State.RoadHop);}
            Assert.That(hop,Is.GreaterThan(.005f));Assert.That(cart.Water,Is.LessThan(145));
        }
    }
}
