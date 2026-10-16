using System.Linq;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class CircusMascotTests
    {
        private const string Art = "Assets/_Project/Art/CircusNight/Mascot/";

        [TestCase("Karlan")][TestCase("Boss")][TestCase("Shlanga")][TestCase("Fat")]
        [TestCase("MyBoy")][TestCase("Girl")][TestCase("Milez")][TestCase("Aza")]
        public void AngerSkinContainsOnlyHeadInfluencedVerticesOfTheOriginalAvatar(string name)
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            var library = AssetDatabase.LoadAssetAtPath<CircusAngerLibrary>(Art + "CN_AngerHeads.asset");
            var player = Object.Instantiate(roster.Characters.Single(c => c.DisplayName == name).Prefab);
            try
            {
                var head = player.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Head);
                int found = 0;
                foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh = library.Find(skin.sharedMesh); if (mesh == null) continue; found++;
                    Assert.That(mesh, Is.Not.SameAs(skin.sharedMesh));
                    Assert.That(mesh.vertexCount, Is.GreaterThan(30).And.LessThan(skin.sharedMesh.vertexCount));
                    Assert.That(mesh.bindposes, Is.EqualTo(skin.sharedMesh.bindposes));
                    Assert.That(mesh.subMeshCount, Is.EqualTo(skin.sharedMesh.subMeshCount));
                    foreach (var weight in mesh.boneWeights)
                    {
                        float influence = 0;
                        var indices = new[] { weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3 };
                        var weights = new[] { weight.weight0, weight.weight1, weight.weight2, weight.weight3 };
                        for (int i = 0; i < 4; i++)
                            if (skin.bones[indices[i]] == head || skin.bones[indices[i]].IsChildOf(head)) influence += weights[i];
                        Assert.That(influence, Is.GreaterThanOrEqualTo(.149f));
                    }
                    Assert.That(mesh.colors.Max(c => c.a), Is.GreaterThan(.99f));
                }
                Assert.That(found, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(player); }
        }

        [TestCase(.8f, 1.4f)][TestCase(1.7f, 1.0f)][TestCase(2.6f, 1.9f)]
        public void MascotUsesActualFingerAndEyelidRig(float time, float targetHeight)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "CN_Mascot.prefab");
            var root = Object.Instantiate(prefab);
            try
            {
                var actor = root.GetComponent<CircusMascotActor>(); actor.Begin(root.transform, time, 73);
                var target = new Vector3(-.3f, targetHeight, 2.4f); actor.Sample(time + .05f, target);
                Assert.That(Vector3.Dot(actor.PointDirection, (target - actor.PointOrigin).normalized), Is.GreaterThan(.95f));
                Assert.That(root.GetComponentsInChildren<Collider>(), Is.Empty, "Mascot must not block cage movement");
                Assert.That(root.GetComponentsInChildren<SkinnedMeshRenderer>().Count(r => r.sharedMesh.blendShapeCount > 0), Is.EqualTo(3));
                actor.Sample(.23f + (73 % 7) * .037f, target);
                Assert.That(actor.BlinkWeight, Is.GreaterThan(99));
                actor.Sample(.6f, target); Assert.That(actor.BlinkWeight, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void LaughterAndBlinkMoveTheRenderedFacialMesh()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Art + "CN_Mascot.prefab"));
            var baked = new Mesh();
            try
            {
                var actor = root.GetComponent<CircusMascotActor>(); actor.Begin(root.transform, 0, 73);
                var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>();
                var face = skins.Single(r => r.name == "Horse_Meshy_Skin");
                var lid = skins.Single(r => r.name == "UpperLid.L");
                var head = root.GetComponentsInChildren<Transform>().Single(t => t.name == "Head");
                int jaw = System.Array.FindIndex(face.bones, b => b.name == "Jaw");
                var weights = face.sharedMesh.boneWeights;
                var mouth = Enumerable.Range(0, weights.Length).Where(i => weights[i].boneIndex0 == jaw && weights[i].weight0 > .9f).ToArray();
                Assert.That(mouth.Length, Is.GreaterThan(20), "Lower lip must be skinned to the jaw");
                var target = new Vector3(0, 1.6f, 2.4f);
                actor.Sample(.6f, target); face.BakeMesh(baked);
                var neutral = baked.vertices.Select(v => head.InverseTransformPoint(face.transform.TransformPoint(v))).ToArray();
                float mouthMotion = 0;
                // Sample a whole laugh cycle, rather than one phase of its retimed clip.
                for (float time = 1f; time < 1.8f; time += .1f)
                {
                    actor.Sample(time, target); face.BakeMesh(baked);
                    var laughing = baked.vertices.Select(v => head.InverseTransformPoint(face.transform.TransformPoint(v))).ToArray();
                    mouthMotion = Mathf.Max(mouthMotion, mouth.Max(i => Vector3.Distance(neutral[i], laughing[i])));
                }
                Assert.That(mouthMotion, Is.GreaterThan(.06f));
                Assert.That(actor.SmileWeight, Is.GreaterThan(40));
                actor.Sample(.6f, target); lid.BakeMesh(baked);
                var open = baked.vertices.Select(v => head.InverseTransformPoint(lid.transform.TransformPoint(v))).ToArray();
                actor.Sample(.23f + (73 % 7) * .037f, target); lid.BakeMesh(baked);
                var closed = baked.vertices.Select(v => head.InverseTransformPoint(lid.transform.TransformPoint(v))).ToArray();
                Assert.That(open.Select((v, i) => Vector3.Distance(v, closed[i])).Max(), Is.GreaterThan(.06f));
            }
            finally { Object.DestroyImmediate(baked); Object.DestroyImmediate(root); }
        }

        [Test]
        public void HorsePopsAtFullSizeAndLeavesItsCloudAlive()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Art + "CN_Mascot.prefab"));
            try
            {
                var actor = root.GetComponent<CircusMascotActor>();
                actor.Begin(root.transform, 0, 73);
                var model = root.transform.Find("Model");
                foreach (float time in new[] { 0f, .1f, 4.8f, CircusCageMascot.ShowSeconds - .01f })
                {
                    actor.Sample(time, new Vector3(0, 1.6f, 2.4f));
                    Assert.That(model.localScale.x, Is.EqualTo(.42f).Within(.0001f), "No grow/shrink transition");
                    Assert.That(model.gameObject.activeSelf, Is.True);
                }
                var puffs = root.GetComponentInChildren<ParticleSystem>();
                puffs.Clear();
                actor.Stop();
                Assert.That(model.gameObject.activeSelf, Is.False, "Horse disappears immediately");
                Assert.That(root.activeSelf, Is.True, "Exit cloud must not be deactivated with the mesh");
                Assert.That(puffs.particleCount, Is.GreaterThan(0));
                int count = puffs.particleCount;
                actor.Stop();
                Assert.That(puffs.particleCount, Is.EqualTo(count), "Repeated cleanup must not emit another cloud");
                actor.Begin(root.transform, 0, 73);
                Assert.That(model.gameObject.activeSelf, Is.True, "Next descent reuses a visible horse");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SuppliedLaughFitsTheEntirePresentationAtOriginalPitch()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "CN_Mascot.prefab");
            var audio = root.GetComponent<AudioSource>();
            Assert.That(audio.clip.name, Is.EqualTo("creepy-laugh-single-male-aspic-long"));
            Assert.That(audio.pitch, Is.EqualTo(1f));
            Assert.That(audio.loop, Is.False);
            Assert.That(audio.clip.length, Is.InRange(4.9f, 5.2f));
            Assert.That(CircusCageMascot.ShowSeconds - CircusCageMascot.LaughStarts, Is.GreaterThanOrEqualTo(audio.clip.length));
            var material = root.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
            Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True, "Dark arena still needs visible colors");
            Assert.That(material.GetColor("_EmissionColor").maxColorComponent, Is.GreaterThan(.3f));
            var validated = new Material(material);
            try
            {
                var validator = System.Type.GetType("UnityEditor.BaseShaderGUI, Unity.RenderPipelines.Universal.Editor");
                Assert.That(validator, Is.Not.Null);
                validator.GetMethod("SetMaterialKeywords").Invoke(null, new object[] { validated, null, null });
                Assert.That(validated.IsKeywordEnabled("_EMISSION"), Is.True, "URP import/build validation must preserve the fill light");
            }
            finally { Object.DestroyImmediate(validated); }
        }

        [Test]
        public void AngerBeginsWithLaughterAndFullyDisappearsAfterwards()
        {
            Assert.That(CircusCageMascot.HeatAt(0f), Is.Zero);
            Assert.That(CircusCageMascot.HeatAt(1.2f), Is.GreaterThan(.99f));
            Assert.That(CircusCageMascot.HeatAt(CircusCageMascot.ShowSeconds), Is.Zero);
        }
    }
}
