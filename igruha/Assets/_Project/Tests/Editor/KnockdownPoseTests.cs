using System.Collections.Generic;
using Igruha.Core.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>
    /// Падение и вставание после удара не уводят тело под пол и не
    /// подвешивают его над полом.
    ///
    /// Замер 21.09 на живом аниматоре: встающий персонаж проваливался
    /// ступнями на 35–54 см у всех восьмерых, а лежащий висел над полом на
    /// полметра. Причин две, и чинятся они в паре.
    ///
    /// 1. Клипы Mixamo импортировались без «Bake Into Pose» по высоте: подъём
    ///    и опускание таза считались движением корня, а оно у персонажа
    ///    выключено — вертикаль выбрасывалась. Теперь высота запечена в позу
    ///    от ступней («Feet»): в первом кадре клипа ступни на полу, дальше
    ///    тело движется как записано. «Original» не годится — корень у
    ///    разных скачанных клипов разный, и Milez в падении уходил на 67 см.
    /// 2. Падения записаны на худом эталоне, а наши персонажи толще и с
    ///    большими ботинками: лежащий Толстый всё равно уходил в пол на 29 см.
    ///    Это добирает <see cref="CharacterFootGrounding"/> по точкам кожи
    ///    (<see cref="CharacterSkinProbes"/>). Он умеет только поднимать,
    ///    поэтому первая правка и нужна: после неё тело никогда не висит,
    ///    а только уходит в пол.
    ///
    /// <see cref="FootGroundingTests"/> этого не видел: он берёт клип отдельно
    /// от аниматора (<c>AnimationMode.SampleAnimationClip</c>), где высота таза
    /// остаётся такой, какой её записал Mixamo. Здесь крутится настоящий
    /// аниматор, кадр за кадром, как в игре.
    /// </summary>
    public sealed class KnockdownPoseTests
    {
        private static readonly string[] Characters = { "Aza", "Boss", "Fat", "Girl", "Milez", "MyBoy", "Shlanga", "Player" };

        private static readonly string[] KnockdownStates = { "FlyBack", "StandUpFromBack", "FallForward", "StandUpFromForward" };

        private static readonly string[] KnockdownTriggers = { "KnockdownFront", "KnockdownBack" };

        private static readonly HumanBodyBones[] FootBones =
        {
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes
        };

        /// <summary>Шаг аниматора — кадр при 60 к/с.</summary>
        private const float Step = 1f / 60f;

        /// <summary>Сколько стоим до удара: переход в покой после Rebind.</summary>
        private const float SettleTime = 0.5f;

        /// <summary>
        /// Сколько смотрим после удара. Самая длинная цепочка «падение →
        /// подъём» при скорости 1.5 короче четырёх секунд; остаток — возврат
        /// в покой, где ступни тоже должны стоять на полу.
        /// </summary>
        private const float WatchTime = 5f;

        /// <summary>Та же граница, что в <see cref="FootGroundingTests"/>: кость носка на сантиметр выше подошвы.</summary>
        private const float BoneTolerance = 0.005f;

        /// <summary>
        /// Сколько кожи может оставаться в полу: 2 см подъём оставляет
        /// нарочно (толщина подошвы в стойке), полсантиметра — запас на шаг.
        /// </summary>
        private const float SkinTolerance = 0.025f;

        /// <summary>
        /// Запас для настоящей сетки: точек кожи — сотни из миллиона вершин,
        /// самая нижняя вершина может пройти между ними.
        /// </summary>
        private const float MeshTolerance = 0.04f;

        /// <summary>Выше этого над полом при поднятой модели — уже зависание.</summary>
        private const float FloatTolerance = 0.01f;

        /// <summary>
        /// Насколько таз обязан опуститься после удара. Если меньше — аниматор
        /// не крутился, и тест проверял бы застывшую стойку.
        /// </summary>
        private const float ExpectedHipsDrop = 0.3f;

        /// <summary>Настоящую сетку запекаем не каждый кадр: миллион вершин.</summary>
        private const int MeshSampleEvery = 10;

        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void Clean()
        {
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
        /// Настройка, от которой всё зависит, — прямо на клипах. Если кто-то
        /// заменит клип подъёма, забыв галочку, тест назовёт клип.
        /// </summary>
        [Test]
        public void KnockdownClipsBakeBodyHeightIntoPose([ValueSource(nameof(Characters))] string character)
        {
            AnimatorController controller = ControllerOf(character);
            var failures = new List<string>();

            foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                if (System.Array.IndexOf(KnockdownStates, child.state.name) < 0)
                {
                    continue;
                }

                var clip = child.state.motion as AnimationClip;
                Assert.That(clip, Is.Not.Null, $"{character}: у состояния {child.state.name} нет клипа");

                if (!BakesHeightFromFeet(clip))
                {
                    failures.Add($"{child.state.name} ({clip.name})");
                }
            }

            Assert.That(failures, Is.Empty,
                $"{character}: высота таза не запечена в позу от ступней — тело уйдёт под пол: {string.Join(", ", failures)}");
        }

        /// <summary>
        /// У каждого персонажа есть точки кожи, и собраны они под его нынешнюю
        /// сетку. Заменили модель — пересобрать: «Igruha/Персонажи/Собрать
        /// опорные точки кожи».
        /// </summary>
        [Test]
        public void EveryCharacterHasSkinProbes([ValueSource(nameof(Characters))] string character)
        {
            CharacterSkinProbes library = CharacterSkinProbes.Shared;
            Assert.That(library, Is.Not.Null, $"нет Resources/{CharacterSkinProbes.ResourceName}");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Player/{character}.prefab");
            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(skin, Is.Not.Null, $"{character}: нет SkinnedMeshRenderer");

            Assert.That(library.TryGet(skin.sharedMesh, out CharacterSkinProbes.Entry entry), Is.True,
                $"{character}: нет точек кожи для сетки {skin.sharedMesh.name} — пересобрать опоры");
            Assert.That(entry.BindPoses.Length, Is.EqualTo(skin.bones.Length),
                $"{character}: точки собраны под другой скелет — пересобрать опоры");
            Assert.That(entry.Probes.Length, Is.GreaterThan(100), $"{character}: слишком мало точек кожи");

            foreach (CharacterSkinProbes.Probe probe in entry.Probes)
            {
                for (int slot = 0; slot < CharacterSkinProbes.BonesPerProbe; slot++)
                {
                    Assert.That(probe.Bone(slot), Is.LessThan(skin.bones.Length), $"{character}: индекс кости вне скелета");
                }

                Assert.That(probe.Group, Is.InRange(0, CharacterSkinProbes.GroupCount - 1));
            }
        }

        [Test]
        public void BodyStaysOnTheFloorThroughKnockdown(
            [ValueSource(nameof(Characters))] string character,
            [ValueSource(nameof(KnockdownTriggers))] string trigger)
        {
            Rig rig = Build(character);
            Assert.That(rig.Grounding.UsesSkin, Is.True, $"{character}: подъём не нашёл точек кожи");

            Animator animator = rig.Animator;
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            float standingHips = hips.position.y;
            float lowestHips = standingHips;

            animator.SetTrigger(trigger);

            float worstSkin = 0f;
            float worstFoot = 0f;
            string worstSkinClip = string.Empty;
            string worstFootClip = string.Empty;
            int steps = Mathf.CeilToInt(WatchTime / Step);

            for (int i = 0; i < steps; i++)
            {
                Advance(rig, Step);
                lowestHips = Mathf.Min(lowestHips, hips.position.y);

                float skin = rig.Grounding.LowestSkinHeight;
                if (skin < worstSkin)
                {
                    worstSkin = skin;
                    worstSkinClip = $"{CurrentClip(animator)} через {i * Step:F2} с";
                }

                float foot = LowestFoot(animator);
                if (foot < worstFoot)
                {
                    worstFoot = foot;
                    worstFootClip = $"{CurrentClip(animator)} через {i * Step:F2} с";
                }
            }

            Assert.That(standingHips - lowestHips, Is.GreaterThan(ExpectedHipsDrop),
                $"{character}, {trigger}: таз не опустился — аниматор не крутился, проверять нечего");
            Assert.That(worstSkin, Is.GreaterThanOrEqualTo(-SkinTolerance),
                $"{character}, {trigger}: кожа на {-worstSkin * 100f:F1} см в полу — {worstSkinClip}");
            Assert.That(worstFoot, Is.GreaterThanOrEqualTo(-BoneTolerance),
                $"{character}, {trigger}: кость ступни на {-worstFoot * 100f:F1} см под полом — {worstFootClip}");
        }

        /// <summary>
        /// Сверка точек кожи с настоящей сеткой на самом толстом персонаже:
        /// ничего не уходит в пол и ничего не висит. Точки — выборка, и если
        /// отбор когда-нибудь испортится, это всплывёт здесь, а не на глаз.
        /// </summary>
        [Test]
        public void RealMeshOfFatNeitherSinksNorFloats([ValueSource(nameof(KnockdownTriggers))] string trigger)
        {
            Rig rig = Build("Fat");
            var skin = rig.Animator.GetComponentInChildren<SkinnedMeshRenderer>();
            var baked = new Mesh();
            spawned.Add(baked);

            rig.Animator.SetTrigger(trigger);

            float worstSink = 0f;
            float worstFloat = 0f;
            int steps = Mathf.CeilToInt(WatchTime / Step);

            for (int i = 0; i < steps; i++)
            {
                Advance(rig, Step);
                if (i % MeshSampleEvery != 0)
                {
                    continue;
                }

                float lowest = LowestVertex(skin, baked);
                worstSink = Mathf.Min(worstSink, lowest);
                if (rig.Grounding.CurrentLift > FloatTolerance)
                {
                    worstFloat = Mathf.Max(worstFloat, lowest);
                }
            }

            Assert.That(worstSink, Is.GreaterThanOrEqualTo(-MeshTolerance),
                $"Fat, {trigger}: сетка на {-worstSink * 100f:F1} см в полу");
            Assert.That(worstFloat, Is.LessThanOrEqualTo(FloatTolerance),
                $"Fat, {trigger}: поднятая модель висит над полом на {worstFloat * 100f:F1} см");
        }

        private struct Rig
        {
            public Animator Animator;
            public CharacterFootGrounding Grounding;
        }

        /// <summary>
        /// Настоящий префаб на полу, в стойке. Подъём ставится так же, как в
        /// игре: там его вешает драйвер в <c>Awake</c>, а в режиме редактора
        /// <c>Awake</c> не идёт.
        ///
        /// Аниматор переводится в «анимировать всегда». У персонажей стоит
        /// «не писать кости, пока не видно», а в тесте камеры нет вовсе:
        /// без этой строки поза застывает, и тест проходит впустую.
        /// </summary>
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
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            LayerMask ground = motor.GroundLayers;
            BuildFloor(ground);

            var grounding = player.AddComponent<CharacterFootGrounding>();
            grounding.Bind(animator, visualRoot, ground);
            var rig = new Rig { Animator = animator, Grounding = grounding };

            animator.Rebind();
            animator.Update(0f);
            Advance(rig, SettleTime);
            return rig;
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

        private static void Advance(Rig rig, float seconds)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(seconds / Step));
            for (int i = 0; i < steps; i++)
            {
                rig.Animator.Update(Step);
                rig.Grounding.Apply();
            }
        }

        private static string CurrentClip(Animator animator)
        {
            AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
            return clips.Length > 0 ? $"«{clips[0].clip.name}»" : "?";
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

        private static float LowestVertex(SkinnedMeshRenderer skin, Mesh baked)
        {
            skin.BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            Matrix4x4 toWorld = skin.transform.localToWorldMatrix;

            float lowest = float.MaxValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                float y = toWorld.MultiplyPoint3x4(vertices[i]).y;
                if (y < lowest)
                {
                    lowest = y;
                }
            }

            return lowest;
        }

        private static AnimatorController ControllerOf(string character)
        {
            string path = $"Assets/_Project/Prefabs/Player/{character}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, $"нет префаба {path}");

            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null, $"{character}: нет аниматора");

            var controller = animator.runtimeAnimatorController as AnimatorController;
            Assert.That(controller, Is.Not.Null, $"{character}: у аниматора нет контроллера");
            return controller;
        }

        private static bool BakesHeightFromFeet(AnimationClip clip)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as ModelImporter;
            if (importer == null)
            {
                return false;
            }

            foreach (ModelImporterClipAnimation settings in importer.clipAnimations)
            {
                if (settings.name == clip.name)
                {
                    return settings.lockRootHeightY && settings.heightFromFeet;
                }
            }

            return false;
        }
    }
}
