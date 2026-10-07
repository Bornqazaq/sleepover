using System.Reflection;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class CircusBearContactPresentationTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private GameObject root;
        private Transform visual;
        private CircusBearContactPresentation presentation;
        private readonly Vector3 authoredPosition=new Vector3(.1f,.05f,-.2f);
        private readonly Quaternion authoredRotation=Quaternion.Euler(0,12,0);

        [SetUp]
        public void SetUp()
        {
            root=new GameObject("Interpolated network bear");
            visual=new GameObject("Unanimated visual wrapper").transform;
            visual.SetParent(root.transform,false);
            visual.localPosition=authoredPosition;
            visual.localRotation=authoredRotation;
            presentation=root.AddComponent<CircusBearContactPresentation>();
            presentation.Initialize(null,visual);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void SnapshotHoldsVisualWorldPoseWithoutMovingNetworkRootAndRejoinsWithoutJump()
        {
            root.transform.SetPositionAndRotation(new Vector3(2,0,1),Quaternion.Euler(0,25,0));
            Vector3 oldRoot=root.transform.position;
            Quaternion oldRotation=root.transform.rotation;
            Vector3 server=new Vector3(2.5f,0,1.7f);
            Quaternion serverRotation=Quaternion.Euler(0,60,0);
            Vector3 contactVisual=server+serverRotation*authoredPosition;
            Quaternion contactRotation=serverRotation*authoredRotation;

            presentation.BeginContact(server,60);

            AssertPose(root.transform,oldRoot,oldRotation);
            AssertPose(visual,contactVisual,contactRotation);
            root.transform.SetPositionAndRotation(Vector3.Lerp(oldRoot,server,.5f),Quaternion.Slerp(oldRotation,serverRotation,.5f));
            Tick();
            AssertPose(visual,contactVisual,contactRotation);
            Assert.That(presentation.IsActive,Is.True);
            root.transform.SetPositionAndRotation(server,serverRotation);
            typeof(CircusBearContactPresentation).GetField("startedAt",Private).SetValue(presentation,Time.time-.1f);
            Tick();

            Assert.That(presentation.IsActive,Is.False);
            AssertPose(root.transform,server,serverRotation);
            AssertPose(visual,contactVisual,contactRotation);
            AssertAuthoredPose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ResetOrDisableRestoresAuthoredLocalPose(bool disable)
        {
            presentation.BeginContact(new Vector3(3,0,4),90);
            Assert.That(presentation.IsActive,Is.True);
            if(disable)typeof(CircusBearContactPresentation).GetMethod("OnDisable",Private).Invoke(presentation,null);
            else presentation.ResetPresentation();
            Assert.That(presentation.IsActive,Is.False);
            AssertAuthoredPose();
            AssertPose(root.transform,Vector3.zero,Quaternion.identity);
        }

        private void Tick() => typeof(CircusBearContactPresentation).GetMethod("LateUpdate",Private).Invoke(presentation,null);
        private void AssertAuthoredPose()
        {
            Assert.That(Vector3.Distance(visual.localPosition,authoredPosition),Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(visual.localRotation,authoredRotation),Is.LessThan(.01f));
        }
        private static void AssertPose(Transform target,Vector3 position,Quaternion rotation)
        {
            Assert.That(Vector3.Distance(target.position,position),Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(target.rotation,rotation),Is.LessThan(.01f));
        }
    }
}
