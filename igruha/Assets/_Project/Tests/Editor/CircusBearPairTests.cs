using System.Linq;
using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class CircusBearPairTests
    {
        [TestCase("CansOrder")]
        [TestCase("Stopwatch")]
        public void ShippingSceneHasTwoWiredAndIndependentlyReplicatedBears(string sceneName)
        {
            var scene=EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Minigames/"+sceneName+".unity");
            try
            {
                var roots=scene.GetRootGameObjects();
                var bears=roots.SelectMany(r=>r.GetComponentsInChildren<PitBear>(true)).ToArray();
                Assert.That(bears.Length,Is.EqualTo(2));
                var game=roots.SelectMany(r=>r.GetComponentsInChildren<MinigameControllerBase>(true)).Single();
                var fields=new SerializedObject(game);
                Assert.That(fields.FindProperty("bear").objectReferenceValue,Is.Not.Null);
                Assert.That(fields.FindProperty("secondBear").objectReferenceValue,Is.Not.Null);
                Assert.That(fields.FindProperty("secondBear").objectReferenceValue,Is.Not.EqualTo(fields.FindProperty("bear").objectReferenceValue));
                var ids=bears.Select(b=>new SerializedObject(b.GetComponent<NetworkObject>()).FindProperty("GlobalObjectIdHash").longValue).ToArray();
                Assert.That(ids.Distinct().Count(),Is.EqualTo(2));
                Assert.That(ids.All(i=>i!=0),Is.True);
                Assert.That(Vector3.Distance(bears[0].transform.position,bears[1].transform.position),Is.GreaterThan(PitBear.CompanionClearance));
                foreach(var bear in bears)
                {
                    var collider=bear.GetComponent<CapsuleCollider>();
                    Assert.That(collider.direction,Is.EqualTo(2),"Quadruped collision must follow its body, not the old upright beast.");
                    Assert.That(collider.height,Is.GreaterThan(3));
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void BearsCanPassHeadOnWithoutCrossingOrLeavingThePit()
        {
            var a=new GameObject("First bear").AddComponent<PitBear>();
            var b=new GameObject("Second bear").AddComponent<PitBear>();
            try
            {
                a.transform.position=Vector3.left*4.4f;b.transform.position=Vector3.right*4.4f;
                a.Configure(8.2f,2.1f,2.35f,0,8,8.64f);b.Configure(8.2f,2.1f,2.35f,0,8,8.64f);
                a.ConfigureCompanion(b,0);b.ConfigureCompanion(a,1);
                var move=typeof(PitBear).GetMethod("MoveBy",BindingFlags.NonPublic|BindingFlags.Instance);
                for(int i=0;i<120;i++)
                {
                    move.Invoke(a,new object[]{new Vector3(.08f,0,.04f)});
                    move.Invoke(b,new object[]{new Vector3(-.08f,0,-.04f)});
                    Assert.That(Vector3.Distance(a.transform.position,b.transform.position),Is.GreaterThanOrEqualTo(PitBear.CompanionClearance-.001f));
                    Assert.That(a.transform.position.magnitude,Is.LessThanOrEqualTo(8.64f-PitBear.BodyWallClearance+.001f));
                    Assert.That(b.transform.position.magnitude,Is.LessThanOrEqualTo(8.64f-PitBear.BodyWallClearance+.001f));
                }
                Assert.That(a.transform.position.z,Is.GreaterThan(2f),"The pair must be able to pass, not freeze at the separation limit.");
            }
            finally { Object.DestroyImmediate(a.gameObject);Object.DestroyImmediate(b.gameObject); }
        }
    }
}
