using System.Collections.Generic;
using Igruha.Core.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>
    /// Логика <see cref="CharacterFootGrounding"/>, которую можно проверить
    /// без живого аниматора.
    ///
    /// Проверки «ни одна часть тела не под полом» здесь нет намеренно: она
    /// живёт в PlayMode (<c>LiveFootGroundingTests</c>). Выборка клипа в
    /// редакторе ставит тело почти вдвое выше, чем настоящий аниматор, и
    /// проверка по ней была зелёной, пока в игре руки и ноги уходили в пол.
    /// </summary>
    public sealed class FootGroundingTests
    {
        private static readonly string[] Characters = { "Aza", "Boss", "Fat", "Girl", "Milez", "MyBoy", "Shlanga", "Player" };

        private static readonly HumanBodyBones[] FootBones =
        {
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes
        };

        /// <summary>Сколько подошвы может остаться под полом: кость носка стоит на сантиметр выше подошвы.</summary>
        private const float Tolerance = 0.005f;

        /// <summary>Сколько кадров клипа проверяем на секунду длительности.</summary>
        private const float SamplesPerSecond = 30f;

        private readonly List<Object> spawned = new List<Object>();

        /// <summary>
        /// Сцена своя не нужна: раннер EditMode и так гоняет тесты во
        /// временной безымянной сцене, открытую пользователем не трогает.
        /// </summary>
        [SetUp]
        public void Open()
        {
            AnimationMode.StartAnimationMode();
        }

        [TearDown]
        public void Close()
        {
            AnimationMode.StopAnimationMode();
            foreach (Object item in spawned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            spawned.Clear();
        }

        /// <summary>
        /// Аниматор выключен — позу ведёт не он, а ragdoll «Рейса на память».
        /// Тогда подъём не трогает модель вовсе, даже если ступня под полом:
        /// сдвиг корня под физическими костями дёрнул бы тело.
        /// </summary>
        [Test]
        public void DisabledAnimatorLeavesModelAlone()
        {
            Rig rig = Build("Boss");
            AnimationClip dance = null;
            foreach (AnimationClip clip in Clips(rig.Animator))
            {
                if (clip.name == "dance8")
                {
                    dance = clip;
                }
            }

            Assert.That(dance, Is.Not.Null, "у Boss нет dance8");

            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(rig.Animator.gameObject, dance, DeepestDance8Moment);
            AnimationMode.EndSampling();
            Assert.That(LowestFoot(rig.Animator), Is.LessThan(-Tolerance), "кадр должен уводить ступню под пол");

            rig.Animator.enabled = false;
            rig.Grounding.Apply();

            Assert.That(rig.Grounding.CurrentLift, Is.EqualTo(0f));
        }

        /// <summary>Кадр dance8, где у Boss ступня уходит под пол глубже всего (замер 21.09).</summary>
        private const float DeepestDance8Moment = 0.2f;

        private struct Rig
        {
            public Animator Animator;
            public CharacterFootGrounding Grounding;
        }

        private Rig Build(string character)
        {
            string path = $"Assets/_Project/Prefabs/Player/{character}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, $"нет префаба {path}");

            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            spawned.Add(player);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var driver = player.GetComponent<CharacterAnimatorDriver>();
            var motor = player.GetComponent<PlayerController>();
            Assert.That(driver, Is.Not.Null, $"{character}: нет CharacterAnimatorDriver");
            Assert.That(motor, Is.Not.Null, $"{character}: нет PlayerController");

            var data = new SerializedObject(driver);
            var animator = data.FindProperty("animator").objectReferenceValue as Animator;
            var visualRoot = data.FindProperty("visualRoot").objectReferenceValue as Transform;
            Assert.That(animator, Is.Not.Null, $"{character}: у драйвера не назначен аниматор");
            Assert.That(visualRoot, Is.Not.Null, $"{character}: у драйвера не назначен корень модели");
            Assert.That(animator.transform.IsChildOf(visualRoot), Is.True,
                $"{character}: скелет не лежит под корнем модели — поднимать нечего");

            LayerMask ground = motor.GroundLayers;
            Assert.That(ground.value, Is.Not.EqualTo(0), $"{character}: у контроллера пустая маска пола");
            BuildFloor(ground);

            var grounding = player.AddComponent<CharacterFootGrounding>();
            grounding.Bind(animator, visualRoot, ground);
            return new Rig { Animator = animator, Grounding = grounding };
        }

        /// <summary>Плита пола верхом ровно в нуле, на первом слое из маски пола персонажа.</summary>
        private void BuildFloor(LayerMask ground)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(floor);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);

            for (int layer = 0; layer < 32; layer++)
            {
                if ((ground.value & (1 << layer)) != 0)
                {
                    floor.layer = layer;
                    break;
                }
            }

            Physics.SyncTransforms();
        }

        private static void Sample(Rig rig, AnimationClip clip, float time)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(rig.Animator.gameObject, clip, time);
            AnimationMode.EndSampling();

            // Выборка клипа в редакторе сама выключает аниматор — забирает его
            // себе на время показа. А выключенный аниматор для подъёма значит
            // «позу ведёт ragdoll, не трогать», и без этой строки тест проверял
            // бы пустое место: так уже было, провалы сыпались до 4.6 см.
            // В игре аниматор включён всегда, пока ведёт позу.
            rig.Animator.enabled = true;

            // Один проход, а не два: подъём считается по позе без прошлого
            // подъёма и обязан сойтись сразу, не раскачиваясь сам от себя.
            rig.Grounding.Apply();
        }

        private static float LowestFoot(Animator animator)
        {
            float lowest = float.MaxValue;
            foreach (HumanBodyBones bone in FootBones)
            {
                Transform t = animator.GetBoneTransform(bone);
                if (t != null && t.position.y < lowest)
                {
                    lowest = t.position.y;
                }
            }

            return lowest;
        }

        private static IEnumerable<AnimationClip> Clips(Animator animator)
        {
            var seen = new HashSet<AnimationClip>();
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip != null && seen.Add(clip))
                {
                    yield return clip;
                }
            }
        }
    }
}
