using System.Reflection;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class CircusKnockoutContactTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;

        [TestCase(1f)]
        [TestCase(6f)]
        public void OwnerContactCorrectsPhysicsAndRenderTransformTogetherAndRestoresInterpolation(float predictionDistance)
        {
            var avatar=new GameObject("Owner contact avatar");
            CircusKnockout knockout=null;
            try
            {
                var player=avatar.AddComponent<PlayerController>();
                var body=avatar.GetComponent<Rigidbody>();
                typeof(PlayerController).GetField("rb",Private).SetValue(player,body);
                body.interpolation=RigidbodyInterpolation.Interpolate;
                Vector3 contact=new Vector3(1.4f,0,.1f);
                // A late authoritative contact can arrive after a multi-metre
                // prediction drift. Both physics and the rendered body must agree.
                avatar.transform.position=contact+new Vector3(2,0,1).normalized*predictionDistance;
                body.position=avatar.transform.position;
                Assert.That(Vector3.Distance(body.position,contact),Is.EqualTo(predictionDistance).Within(.001f));
                Quaternion rotation=Quaternion.Euler(0,32,0);
                knockout=avatar.AddComponent<CircusKnockout>();
                typeof(CircusKnockout).GetMethod("Awake",Private).Invoke(knockout,null);

                knockout.Eliminate(contact,Vector3.forward*8,KnockdownType.FallForward,32);

                Assert.That(Vector3.Distance(body.position,contact),Is.LessThan(.001f));
                Assert.That(Vector3.Distance(avatar.transform.position,contact),Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(body.rotation,rotation),Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(avatar.transform.rotation,rotation),Is.LessThan(.001f));
                Assert.That(player.MovementLocked,Is.True);
                knockout.Restore();
                Assert.That(body.interpolation,Is.EqualTo(RigidbodyInterpolation.Interpolate));
                Assert.That(player.MovementLocked,Is.False);
            }
            finally
            {
                if(knockout!=null)knockout.Restore();
                Object.DestroyImmediate(avatar);
            }
        }

        [TestCase(0f, 180f, true)]
        [TestCase(90f, -90f, true)]
        [TestCase(0f, 180f, false)]
        [TestCase(179.9f, -1f, false)]
        public void OppositeRemoteFacingCannotChangeServerFallOrRemotePhysics(float serverYaw,float clientYaw,bool frontHit)
        {
            var serverObject=new GameObject("Server contact avatar");
            var clientObject=new GameObject("Remote contact avatar");
            CircusKnockout knockout=null;
            try
            {
                var server=serverObject.AddComponent<PlayerController>();
                var serverBody=serverObject.GetComponent<Rigidbody>();
                serverBody.rotation=Quaternion.Euler(0,serverYaw,0);
                typeof(PlayerController).GetField("rb",Private).SetValue(server,serverBody);
                Vector3 impulse=server.Facing*(frontHit?-8:8)+Vector3.up*2;
                CircusKnockout.CaptureContact(server,impulse,out KnockdownType type,out float yaw);
                Assert.That(type,Is.EqualTo(frontHit?KnockdownType.FlyBack:KnockdownType.FallForward));

                var client=clientObject.AddComponent<PlayerController>();
                client.enabled=false;
                var body=clientObject.GetComponent<Rigidbody>();
                body.isKinematic=true;
                body.position=new Vector3(4,0,3);
                body.rotation=Quaternion.Euler(0,clientYaw,0);
                typeof(PlayerController).GetField("rb",Private).SetValue(client,body);
                Vector3 beforePosition=body.position;
                Quaternion beforeRotation=body.rotation;
                knockout=clientObject.AddComponent<CircusKnockout>();
                typeof(CircusKnockout).GetMethod("Awake",Private).Invoke(knockout,null);
                knockout.Eliminate(Vector3.zero,impulse,type,yaw);

                Assert.That(knockout.FallType,Is.EqualTo(type));
                Assert.That((int)typeof(CircusKnockout).GetField("fallState",Private).GetValue(knockout),
                    Is.EqualTo(Animator.StringToHash(frontHit?"FlyBack":"FallForward")));
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(knockout.ContactYaw,serverYaw)),Is.LessThan(.001f));
                Assert.That(Vector3.Distance(body.position,beforePosition),Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(body.rotation,beforeRotation),Is.LessThan(.001f));
                Assert.That(body.isKinematic,Is.True);
                knockout.Restore();
                Assert.That(client.enabled,Is.False);
                Assert.That(client.MovementLocked,Is.False);
            }
            finally
            {
                if(knockout!=null)knockout.Restore();
                Object.DestroyImmediate(clientObject);
                Object.DestroyImmediate(serverObject);
            }
        }
    }
}
