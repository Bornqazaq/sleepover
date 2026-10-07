using System.Collections.Generic;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class CircusBearContactShapeTests
    {
        private static readonly string[] Characters={"Player","Boss","Shlanga","Fat","MyBoy","Girl","Milez","Aza"};
        private static readonly string[] Poses={"Idle","Run","FlyBack"};
        private static readonly HumanBodyBones[] BodyBones={HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.UpperChest};
        private GameObject avatar;
        private Mesh baked;

        [TearDown] public void Clean()
        {
            if(avatar!=null) Object.DestroyImmediate(avatar);
            if(baked!=null) Object.DestroyImmediate(baked);
        }

        [Test] public void PawEnvelopeClearsTheActualAnimatedTorso([ValueSource(nameof(Characters))] string character)
        {
            var shape=Build(character,out Animator animator,out SkinnedMeshRenderer skin);
            var torsoVertices=TorsoVertices(animator,skin);
            Assert.That(torsoVertices.Count,Is.GreaterThan(100),character+": no independently measured torso vertices");
            baked=new Mesh();
            foreach(string pose in Poses)
            {
                Sample(animator,pose,.38f);shape.RefreshPose();
                // Bake only in this editor regression test. Production uses its cached sparse probes.
                // BakeMesh(true) compensates for imported renderer scale, which
                // TransformPoint then applies exactly once.
                skin.BakeMesh(baked,true);var vertices=baked.vertices;
                Matrix4x4 toWorld=skin.transform.localToWorldMatrix;
                var worldVertices=new Vector3[torsoVertices.Count];
                for(int i=0;i<worldVertices.Length;i++) worldVertices[i]=toWorld.MultiplyPoint3x4(vertices[torsoVertices[i]]);
                Vector3 center=shape.BodyCenter;
                var skinBounds=new Bounds(worldVertices[0],Vector3.zero);
                foreach(Vector3 point in worldVertices)skinBounds.Encapsulate(point);
                Assert.That(Vector3.Distance(skinBounds.center,center),Is.LessThan(1.5f),
                    character+" "+pose+": baked skin is not in the posed body's world coordinates");
                Assert.That(skinBounds.size.magnitude,Is.LessThan(3.5f),character+": skin scale was applied twice");
                for(int side=0;side<8;side++)
                {
                    Vector3 direction=Quaternion.Euler(0,45*side,0)*Vector3.forward;
                    Vector3 approach=center+direction*4f;
                    Vector3 paw=shape.SurfaceToward(approach);
                    AssertFinite(paw,character+" "+pose);
                    float nearest=float.PositiveInfinity;
                    foreach(Vector3 point in worldVertices) nearest=Mathf.Min(nearest,(paw-point).sqrMagnitude);
                    Assert.That(Mathf.Sqrt(nearest),Is.GreaterThanOrEqualTo(CircusBearContactShape.PawRadius-.025f),
                        character+" "+pose+" direction "+side+": paw intersects the independently baked torso");
                    // An arm can legitimately stop the paw before it reaches the
                    // torso. The same proximity bound must then have a witness on
                    // the complete actual skin, including sleeves and fingers.
                    // Torso vertices are already a subset, so scan the full mesh
                    // only when they did not establish this upper bound.
                    float nearestBody=nearest;
                    if(nearestBody>.5f*.5f)
                        foreach(Vector3 vertex in vertices)
                        {
                            nearestBody=Mathf.Min(nearestBody,(toWorld.MultiplyPoint3x4(vertex)-paw).sqrMagnitude);
                            if(nearestBody<=.5f*.5f)break;
                        }
                    Assert.That(Mathf.Sqrt(nearestBody),Is.LessThanOrEqualTo(.5f),
                        character+" "+pose+" direction "+side+": envelope is not near the actual animated body or arms");
                    Vector3 repeated=shape.ProjectOutside(paw,approach);
                    Assert.That(Vector3.Distance(paw,repeated),Is.LessThan(.012f),character+" "+pose+": contact envelope oscillates");
                    Vector3 swept=shape.SweepOutside(approach,center-direction*4f,approach);
                    Assert.That(Vector3.Distance(swept,center-direction*4f),Is.GreaterThan(1),character+" "+pose+": fast swipe tunnels through body");
                }
            }
        }

        [Test] public void EnvelopeFollowsInstanceScaleAndTeleport([ValueSource(nameof(Characters))] string character)
        {
            var shape=Build(character,out Animator animator,out SkinnedMeshRenderer skin);
            Sample(animator,"Idle",.25f);shape.RefreshPose();
            Vector3 center=shape.BodyCenter;
            Vector3 approach=center+Vector3.forward*4;
            float original=Vector3.Distance(center,shape.SurfaceToward(approach,0));
            Vector3 scale=avatar.transform.localScale;
            avatar.transform.localScale=scale*1.5f;shape.RefreshPose();
            Vector3 scaledCenter=shape.BodyCenter;
            float enlarged=Vector3.Distance(scaledCenter,shape.SurfaceToward(scaledCenter+Vector3.forward*4,0));
            Assert.That(enlarged,Is.GreaterThan(original*1.25f),character+": body radius did not follow scale");
            Assert.That(enlarged,Is.LessThan(original*1.8f),character+": body scale was applied twice");
            Vector3 offset=new Vector3(7,3,-5);
            Vector3 before=shape.SurfaceToward(scaledCenter+Vector3.forward*4);
            avatar.transform.position+=offset;shape.RefreshPose();
            Vector3 after=shape.SurfaceToward(scaledCenter+offset+Vector3.forward*4);
            Assert.That(Vector3.Distance(after,before+offset),Is.LessThan(.003f),character+": contact did not follow teleport");
            shape.Bind(null);
            Assert.That(shape.ProjectOutside(Vector3.one,Vector3.zero),Is.EqualTo(Vector3.one));
        }

        private CircusBearContactShape Build(string character,out Animator animator,out SkinnedMeshRenderer skin)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/"+character+".prefab");
            Assert.That(prefab,Is.Not.Null);
            avatar=Object.Instantiate(prefab);
            foreach(var behaviour in avatar.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled=false;
            avatar.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            animator=avatar.GetComponentInChildren<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();animator.Update(0);
            skin=null;
            foreach(var candidate in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if(CharacterTorsoShape.Shared.TryGet(candidate.sharedMesh,out CharacterTorsoShape.Entry entry)) { skin=candidate;break; }
            Assert.That(skin,Is.Not.Null);
            var shape=new CircusBearContactShape();shape.Bind(avatar.GetComponent<PlayerController>());
            Assert.That(shape.HasSkinEnvelope,Is.True,character+": real skin probes are required, not the capsule fallback");
            return shape;
        }

        private static void Sample(Animator animator,string pose,float phase)
        {
            Assert.That(animator.HasState(0,Animator.StringToHash(pose)),Is.True,"Missing pose "+pose);
            animator.Play(pose,0,phase);animator.Update(0);
        }

        private static List<int> TorsoVertices(Animator animator,SkinnedMeshRenderer skin)
        {
            var body=new HashSet<Transform>();
            foreach(var bone in BodyBones)
            { Transform transform=animator.GetBoneTransform(bone);if(transform!=null) body.Add(transform); }
            var bones=skin.bones;var weights=skin.sharedMesh.boneWeights;var result=new List<int>();
            for(int i=0;i<weights.Length;i++)
            {
                BoneWeight weight=weights[i];int strongest=weight.boneIndex0;float amount=weight.weight0;
                if(weight.weight1>amount) { strongest=weight.boneIndex1;amount=weight.weight1; }
                if(weight.weight2>amount) { strongest=weight.boneIndex2;amount=weight.weight2; }
                if(weight.weight3>amount) strongest=weight.boneIndex3;
                if(strongest<bones.Length && body.Contains(bones[strongest])) result.Add(i);
            }
            return result;
        }

        private static void AssertFinite(Vector3 point,string context)
        {
            Assert.That(float.IsNaN(point.x)||float.IsNaN(point.y)||float.IsNaN(point.z)
                ||float.IsInfinity(point.x)||float.IsInfinity(point.y)||float.IsInfinity(point.z),Is.False,context+": non-finite contact");
        }
    }
}
