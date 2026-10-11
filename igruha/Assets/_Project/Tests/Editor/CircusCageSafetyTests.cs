using System.Linq;
using System.Reflection;
using Igruha.Core.Arena;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Igruha.Tests
{
    public sealed class CircusCageSafetyTests
    {
        [TestCase("CansOrder")][TestCase("Stopwatch")]
        public void ContinuousFloorReleasesWithTheHatchAndReturnsWhenClosed(string sceneName)
        {
            var scene=EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Minigames/"+sceneName+".unity");
            GameObject copy=null;
            try
            {
                var source=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CageStation>(true)).First();
                copy=Object.Instantiate(source.gameObject);copy.SetActive(false);
                var station=copy.GetComponent<CageStation>();
                var hatch=copy.GetComponent<HingedFloorHatch>();
                typeof(HingedFloorHatch).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hatch,null);
                typeof(CageStation).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(station,null);
                var support=copy.transform.Find("Floor/ClosedFloorSupport").GetComponent<Collider>();
                station.CloseDoors();Assert.That(support.enabled,Is.True);
                station.OpenDoors(.9f);Assert.That(support.enabled,Is.False,"The extra support cannot leave an invisible floor after opening.");
                station.CloseDoors();Assert.That(support.enabled,Is.True);
                foreach(string side in new[]{"Floor/DoorLeft","Floor/DoorRight"})
                    Assert.That(copy.transform.Find(side).GetComponentInChildren<Collider>().enabled,Is.False,"Only the continuous floor supports a closed hatch.");
            }
            finally { if(copy!=null)Object.DestroyImmediate(copy);EditorSceneManager.ClosePreviewScene(scene); }
        }

        [TestCase("Player")][TestCase("Boss")][TestCase("Fat")][TestCase("MyBoy")]
        [TestCase("Girl")][TestCase("Milez")][TestCase("Aza")][TestCase("Shlanga")]
        public void RunningIntoShelfCannotPushPlayerThroughClosedFloor(string character)
        {
            var source = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Minigames/CansOrder.unity");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var cage = source.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CageStation>(true)).First();
                var board=cage.transform.Find("PropSlot/CanShelf/Board").GetComponent<BoxCollider>();
                Assert.That(board.bounds.min.y,Is.LessThan(cage.transform.position.y),"The physical tabletop must have a solid front down to the floor; CanShelf.Board only names the slot anchor.");
                // Copy collision only into an isolated physics world. No gameplay
                // script or camera can teleport/rescue a falling test passenger.
                foreach(var box in cage.GetComponentsInChildren<BoxCollider>(true))
                {
                    if(!box.enabled || box.isTrigger)continue;
                    var go = new GameObject(box.name);
                    SceneManager.MoveGameObjectToScene(go,scene);
                    go.transform.SetPositionAndRotation(cage.transform.InverseTransformPoint(box.transform.position),
                        Quaternion.Inverse(cage.transform.rotation)*box.transform.rotation);
                    go.transform.localScale=box.transform.lossyScale;
                    go.layer=box.gameObject.layer;
                    var copy=go.AddComponent<BoxCollider>();copy.center=box.center;copy.size=box.size;
                }
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/"+character+".prefab");
                var original=prefab.GetComponent<CapsuleCollider>();
                var actor=new GameObject(character);SceneManager.MoveGameObjectToScene(actor,scene);
                actor.transform.position=new Vector3(0,.01f,-.72f);
                actor.transform.localScale=prefab.transform.localScale;
                var capsule=actor.AddComponent<CapsuleCollider>();capsule.center=original.center;
                capsule.radius=original.radius;capsule.height=original.height;capsule.direction=original.direction;
                var body=actor.AddComponent<Rigidbody>();body.constraints=RigidbodyConstraints.FreezeRotation;
                body.mass=prefab.GetComponent<Rigidbody>().mass;
                var physics=scene.GetPhysicsScene();float low=1,high=-1,lowestSupport=1;
                for(int frame=0;frame<150;frame++)
                {
                    float crouchHeight=Mathf.Max(.8f,capsule.radius*2);
                    capsule.height=Mathf.MoveTowards(capsule.height,frame<32?crouchHeight:original.height,(original.height-crouchHeight)/6);
                    capsule.center=original.center-Vector3.up*(original.height-capsule.height)*.5f;
                    // Sprint straight at the shelf, release, then rush again.
                    float speed=frame<65 || frame>90 ? 6.5f : -6.5f;
                    body.linearVelocity=new Vector3(0,body.linearVelocity.y,speed);
                    physics.Simulate(.02f);
                    if(physics.SphereCast(capsule.bounds.center,capsule.radius*.95f,Vector3.down,out RaycastHit hit,
                        capsule.bounds.extents.y-capsule.radius+.3f,LayerMask.GetMask("Ground"),QueryTriggerInteraction.Ignore))
                        lowestSupport=Mathf.Min(lowestSupport,hit.point.y);
                    low=Mathf.Min(low,body.position.y);high=Mathf.Max(high,body.position.y);
                }
                TestContext.Out.WriteLine(character+" root y="+low+".."+high+" support="+lowestSupport);
                Assert.That(low,Is.GreaterThan(-.06f),"Shelf collision must not expel the capsule under the closed hatch.");
                Assert.That(lowestSupport,Is.GreaterThan(-.03f),"The motor's ground probe must see the top face, never an internal seam below it.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene);EditorSceneManager.ClosePreviewScene(source); }
        }

        [Test]
        public void CrouchCapsuleCannotCrossClosedSupportButCanFallAfterRelease()
        {
            var scene=EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Minigames/CansOrder.unity");
            GameObject cage=null,avatar=null;
            try
            {
                var source=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CageStation>(true)).First();
                cage=Object.Instantiate(source.gameObject);cage.transform.position=Vector3.zero;cage.SetActive(true);
                var station=cage.GetComponent<CageStation>();
                typeof(CageStation).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(station,null);
                avatar=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab"));
                var player=avatar.GetComponent<PlayerController>();var body=avatar.GetComponent<Rigidbody>();
                var capsule=avatar.GetComponent<CapsuleCollider>();capsule.height=.8f;capsule.center=Vector3.up*.4f;
                station.SetOccupant(player);player.enabled=true;body.isKinematic=false;body.position=Vector3.down*.885f;body.linearVelocity=Vector3.down;
                Physics.SyncTransforms();
                typeof(CageStation).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(station,null);
                Assert.That(body.position.y,Is.GreaterThanOrEqualTo(-.001f),"Recorded crouch penetration must be rejected while the floor is closed. support="+cage.transform.Find("Floor/ClosedFloorSupport").GetComponent<Collider>().bounds+" player="+player.enabled+" kinematic="+body.isKinematic);
                Assert.That(body.linearVelocity.y,Is.Zero);
                cage.SetActive(false);station.OpenDoors(.9f);cage.SetActive(true);
                body.position=Vector3.down*.4f;body.linearVelocity=Vector3.down;
                typeof(CageStation).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(station,null);
                Assert.That(body.position.y,Is.EqualTo(-.4f).Within(.001f),"Opening must allow the passenger to fall normally.");
            }
            finally { if(cage!=null)Object.DestroyImmediate(cage);if(avatar!=null)Object.DestroyImmediate(avatar);EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void OpenLeafIsHiddenOnlyForTheObstructedLocalCameraAndRestoredAfterRendering()
        {
            var cage=new GameObject("test cage");var actor=new GameObject("runner");
            var lens=new GameObject("local camera");var other=new GameObject("other camera");
            CircusHatchVisibility visibility=null;
            try
            {
                cage.transform.position=Vector3.up*2;
                var hatch=cage.AddComponent<HingedFloorHatch>();
                var floor=new GameObject("Floor");floor.transform.SetParent(cage.transform,false);
                var leaf=GameObject.CreatePrimitive(PrimitiveType.Cube);leaf.name="DoorLeft";leaf.transform.SetParent(floor.transform,false);
                leaf.transform.position=new Vector3(0,1.4f,-2);leaf.transform.localScale=new Vector3(3,2,.1f);
                var skin=leaf.GetComponent<Renderer>();var collider=leaf.GetComponent<Collider>();
                var player=actor.AddComponent<PlayerController>();
                var camera=lens.AddComponent<Camera>();camera.transform.position=new Vector3(0,1.5f,-4);
                var second=other.AddComponent<Camera>();
                typeof(HingedFloorHatch).GetField("<DoorsOpen>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(hatch,true);
                visibility=new CircusHatchVisibility(player,camera,cage.transform);visibility.SetActive(true);
                Render(visibility,"BeginCamera",camera);
                Assert.That(skin.forceRenderingOff,Is.True);Assert.That(collider.enabled,Is.True);
                Render(visibility,"BeginCamera",second);Assert.That(skin.forceRenderingOff,Is.False);
                Render(visibility,"EndCamera",second);Assert.That(skin.forceRenderingOff,Is.True);
                Render(visibility,"EndCamera",camera);Assert.That(skin.forceRenderingOff,Is.False);
                leaf.transform.position=new Vector3(0,3.5f,-2);
                Render(visibility,"BeginCamera",camera);
                Assert.That(skin.forceRenderingOff,Is.True,"A nearby leaf above the runner must not cover approaching bears at the top of the frame.");
                Render(visibility,"EndCamera",camera);Assert.That(skin.forceRenderingOff,Is.False);
                hatch.CloseDoors();Render(visibility,"BeginCamera",camera);
                Assert.That(skin.forceRenderingOff,Is.False,"A closed floor must stay visible.");
                Render(visibility,"EndCamera",camera);
            }
            finally
            {
                visibility?.Dispose();Object.DestroyImmediate(cage);Object.DestroyImmediate(actor);
                Object.DestroyImmediate(lens);Object.DestroyImmediate(other);
            }
        }

        private static void Render(CircusHatchVisibility view,string method,Camera camera)
            => typeof(CircusHatchVisibility).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(view,new object[]{default(ScriptableRenderContext),camera});
    }
}
