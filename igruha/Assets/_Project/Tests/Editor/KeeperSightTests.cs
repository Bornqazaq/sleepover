using Igruha.Core.Vision;
using Igruha.Minigames.CryingAngels;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class KeeperSightTests
    {
        [Test]
        public void BriefSweepsNeverAccumulateAndStableSightPlaysOnlyOnce()
        {
            var cue = new KeeperSightConfirmation();
            for (int i=0;i<20;i++)
            {
                Assert.That(cue.Tick(true, .04f, .3f), Is.False);
                Assert.That(cue.Tick(false, .04f, .3f), Is.False);
            }
            Assert.That(cue.Tick(true, .2f, .3f), Is.False);
            Assert.That(cue.Tick(true, .11f, .3f), Is.True);
            Assert.That(cue.Tick(true, 1f, .3f), Is.False);
        }

        [Test]
        public void SweptTargetCanFreezeWithoutProvidingAnAudioCue()
        {
            var eye=new GameObject("Sight test eye");
            var target=GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                eye.transform.position = new Vector3(400,1,400);
                target.transform.position = new Vector3(400,1,410);
                eye.transform.rotation=Quaternion.Euler(0,40,0);
                var cone=eye.AddComponent<VisionCone>(); cone.SetSweep(60);
                Physics.SyncTransforms();
                Assert.That(cone.CanSee(target.GetComponent<Collider>()), Is.True);
                Assert.That(KeeperSightConfirmation.FullyVisible(eye.transform,28,34,target.GetComponent<Collider>(),0), Is.False);
                eye.transform.rotation=Quaternion.identity;
                Assert.That(KeeperSightConfirmation.FullyVisible(eye.transform,28,34,target.GetComponent<Collider>(),0), Is.True);
            }
            finally { Object.DestroyImmediate(eye); Object.DestroyImmediate(target); }
        }

        [Test]
        public void VisibleHeadBehindCoverDoesNotCountAsFullSight()
        {
            var eye=new GameObject("Sight test eye");
            var target=GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var cover=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                eye.transform.position=new Vector3(400,2,400);
                target.transform.position=new Vector3(400,1,410);
                cover.transform.position=new Vector3(400,.7f,408);
                cover.transform.localScale=new Vector3(3,1.4f,1);
                cover.layer=LayerMask.NameToLayer("Cover");
                Physics.SyncTransforms();
                var body=target.GetComponent<Collider>(); int mask=LayerMask.GetMask("Cover");
                Assert.That(Physics.Linecast(eye.transform.position,body.bounds.center+Vector3.up*.8f,mask), Is.False);
                Assert.That(KeeperSightConfirmation.FullyVisible(eye.transform,28,34,body,mask), Is.False);
                cover.transform.position+=Vector3.right*4; Physics.SyncTransforms();
                Assert.That(KeeperSightConfirmation.FullyVisible(eye.transform,28,34,body,mask), Is.True);
                eye.transform.rotation=Quaternion.Euler(-40,0,0);
                Assert.That(KeeperSightConfirmation.FullyVisible(eye.transform,28,34,body,mask), Is.False);
            }
            finally { Object.DestroyImmediate(eye); Object.DestroyImmediate(target); Object.DestroyImmediate(cover); }
        }
    }
}
