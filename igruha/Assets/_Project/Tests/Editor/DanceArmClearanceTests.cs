using System.Collections.Generic;
using Igruha.Core.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>
    /// Танцы не заводят руки внутрь тела.
    ///
    /// Все восемь персонажей танцуют одними клипами — <c>Shlanga@dance1..8</c>.
    /// Замер 22.09 на живом аниматоре: у Shlanga кости рук не заходят внутрь
    /// торса ни на одном танце, у остальных заходят до 25 см, и у Толстого на
    /// <c>dance3</c> оба предплечья пропадают внутри живота целиком. Это
    /// пересадка движения худого тела на толстое, а не поломка клипа и не
    /// коллайдер.
    ///
    /// Чинит <see cref="CharacterArmClearance"/>: после аниматора рука
    /// доворачивается в плече и локте наружу. Клипы и аниматоры заморожены и
    /// не тронуты; правка живёт только пока играет танец.
    ///
    /// Аниматор здесь крутится настоящий, кадр за кадром, и переведён в
    /// «анимировать всегда»: у персонажей стоит «не писать кости, пока не
    /// видно», а в тесте камеры нет вовсе — без этой строки поза застывает и
    /// тест проходит впустую.
    /// </summary>
    public sealed class DanceArmClearanceTests
    {
        private static readonly string[] Characters = { "Aza", "Boss", "Fat", "Girl", "Milez", "MyBoy", "Shlanga", "Player" };

        /// <summary>Танцы колеса эмоций: восемь секторов — восемь состояний.</summary>
        private static readonly string[] Dances = BuildDanceNames();

        /// <summary>Состояния, которых правка касаться не имеет права: удар в их числе и заморожен отдельно.</summary>
        private static readonly string[] QuietStates = { "Idle", "Run", "Jump", "Punch", "CrouchIdle", "CrouchWalk", "FallForward", "StandUpFromBack" };

        /// <summary>Шаг аниматора — кадр при 60 к/с.</summary>
        private const float Step = 1f / 60f;

        /// <summary>Сколько крутим состояние до замера: доворот набирает силу за 0.15 с.</summary>
        private const float SettleTime = 0.5f;

        /// <summary>Сколько смотрим: самый длинный танец короче четырёх секунд.</summary>
        private const float WatchTime = 4f;

        /// <summary>
        /// Сколько кости руки может остаться внутри объёма торса, м. Объём
        /// вписанный — взят по самому узкому месту сектора, — и рука на его
        /// границе уже прижата к телу, а не спрятана в нём. Самый тяжёлый
        /// случай после правки — 6 см у Толстого и Boss.
        /// </summary>
        private const float Tolerance = 0.08f;

        /// <summary>Насколько глубоко руки уходили в тело до правки — тот самый баг.</summary>
        private const float BuriedDepth = 0.15f;

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
        /// У каждого персонажа есть объём торса, и собран он под его нынешнюю
        /// сетку. Заменили модель — пересобрать: «Igruha/Персонажи/Собрать
        /// объём торса».
        /// </summary>
        [Test]
        public void EveryCharacterHasTorsoShape([ValueSource(nameof(Characters))] string character)
        {
            CharacterTorsoShape library = CharacterTorsoShape.Shared;
            Assert.That(library, Is.Not.Null, $"нет Resources/{CharacterTorsoShape.ResourceName}");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(character));
            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(skin, Is.Not.Null, $"{character}: нет SkinnedMeshRenderer");

            Assert.That(library.TryGet(skin.sharedMesh, out CharacterTorsoShape.Entry entry), Is.True,
                $"{character}: нет объёма торса для сетки {skin.sharedMesh.name} — пересобрать объём");
            Assert.That(entry.BoneCount, Is.EqualTo(skin.bones.Length),
                $"{character}: объём собран под другой скелет — пересобрать объём");
            Assert.That(entry.Slices.Length, Is.GreaterThan(3), $"{character}: слишком мало срезов торса");

            foreach (CharacterTorsoShape.Slice slice in entry.Slices)
            {
                Assert.That(slice.Bone, Is.InRange(0, skin.bones.Length - 1), $"{character}: срез привязан к кости вне скелета");
                Assert.That(slice.HalfHeight, Is.GreaterThan(0f), $"{character}: у среза нулевая высота");
                Assert.That(slice.Radii.Length, Is.EqualTo(CharacterTorsoShape.SectorCount), $"{character}: у среза не все секторы");

                foreach (float radius in slice.Radii)
                {
                    Assert.That(radius, Is.GreaterThan(0f), $"{character}: у среза сектор без радиуса");
                }
            }
        }

        /// <summary>
        /// Главная проверка: ни на одном танце ни у одного персонажа рука не
        /// прячется внутри тела.
        /// </summary>
        [Test]
        public void DancesDoNotBuryArmsInTheBody(
            [ValueSource(nameof(Characters))] string character,
            [ValueSource(nameof(Dances))] string dance)
        {
            Rig rig = Build(character);
            Play(rig, dance);

            float worst = 0f;
            float left = 0f;
            int steps = Mathf.CeilToInt(WatchTime / Step);
            for (int i = 0; i < steps; i++)
            {
                Advance(rig, 1);
                worst = Mathf.Max(worst, rig.Clearance.WorstDepth);
                left = Mathf.Max(left, rig.Clearance.RemainingDepth);
            }

            Assert.That(left, Is.LessThanOrEqualTo(Tolerance),
                $"{character}, {dance}: рука уходит в тело на {left * 100f:F1} см (до правки было {worst * 100f:F1})");
        }

        /// <summary>
        /// Тот самый случай из тикета: у Толстого на третьем танце руки
        /// пропадали в животе. Проверка держит и причину, и следствие — если
        /// объём торса однажды соберётся пустым, «стало хорошо» пройдёт, а
        /// «было плохо» упадёт.
        /// </summary>
        [Test]
        public void FatArmsWereBuriedOnThirdDanceAndAreNotAnymore()
        {
            Rig rig = Build("Fat");
            Play(rig, CharacterAnimatorDriver.EmoteStatePrefix + "3");

            float worst = 0f;
            float left = 0f;
            int steps = Mathf.CeilToInt(WatchTime / Step);
            for (int i = 0; i < steps; i++)
            {
                Advance(rig, 1);
                worst = Mathf.Max(worst, rig.Clearance.WorstDepth);
                left = Mathf.Max(left, rig.Clearance.RemainingDepth);
            }

            Assert.That(worst, Is.GreaterThan(BuriedDepth),
                $"Fat: клип больше не заводит руки в тело ({worst * 100f:F1} см) — проверять нечего, замер сломан");
            Assert.That(left, Is.LessThanOrEqualTo(Tolerance),
                $"Fat: после правки рука всё ещё в теле на {left * 100f:F1} см");
        }

        /// <summary>
        /// На эталонном теле доворота нет вовсе. Танцы записаны на Shlanga —
        /// и это главный довод, что дело в комплекции, а не в клипах.
        /// </summary>
        [Test]
        public void ReferenceBodyIsNeverCorrected([ValueSource(nameof(Dances))] string dance)
        {
            Rig rig = Build("Shlanga");
            Play(rig, dance);

            int steps = Mathf.CeilToInt(WatchTime / Step);
            for (int i = 0; i < steps; i++)
            {
                Advance(rig, 1);
                Assert.That(rig.Clearance.WorstDepth, Is.Zero,
                    $"Shlanga, {dance}: рука зашла в тело на {rig.Clearance.WorstDepth * 100f:F1} см — клипы записаны на нём, такого быть не должно");
            }
        }

        /// <summary>
        /// Вне танца поза остаётся ровно той, какую поставил аниматор — до
        /// последнего бита. Удар, бег, присед, падение и подъём заморожены, и
        /// доворот их не касается.
        /// </summary>
        [Test]
        public void PoseOutsideDancesIsUntouched(
            [ValueSource(nameof(Characters))] string character,
            [ValueSource(nameof(QuietStates))] string state)
        {
            Rig rig = Build(character);
            Play(rig, state);

            HumanBodyBones[] armBones =
            {
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand
            };

            var before = new Quaternion[armBones.Length];
            int steps = Mathf.CeilToInt(WatchTime / Step);
            for (int i = 0; i < steps; i++)
            {
                rig.Animator.Update(Step);
                for (int b = 0; b < armBones.Length; b++)
                {
                    before[b] = rig.Animator.GetBoneTransform(armBones[b]).localRotation;
                }

                rig.Clearance.Apply(Step);

                for (int b = 0; b < armBones.Length; b++)
                {
                    Assert.That(rig.Animator.GetBoneTransform(armBones[b]).localRotation, Is.EqualTo(before[b]),
                        $"{character}, {state}: доворот тронул {armBones[b]} через {i * Step:F2} с");
                }
            }
        }

        private struct Rig
        {
            public Animator Animator;
            public CharacterArmClearance Clearance;
        }

        /// <summary>
        /// Настоящий префаб с доворотом, подключённым так же, как в игре: там
        /// его вешает драйвер в <c>Awake</c>, а в режиме редактора <c>Awake</c>
        /// не идёт.
        /// </summary>
        private Rig Build(string character)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(character));
            Assert.That(prefab, Is.Not.Null, $"нет префаба {PrefabPath(character)}");

            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            spawned.Add(player);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var driver = player.GetComponent<CharacterAnimatorDriver>();
            Assert.That(driver, Is.Not.Null, $"{character}: нет CharacterAnimatorDriver");

            var data = new SerializedObject(driver);
            var animator = data.FindProperty("animator").objectReferenceValue as Animator;
            Assert.That(animator, Is.Not.Null, $"{character}: у драйвера не назначен аниматор");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (!player.TryGetComponent(out CharacterArmClearance clearance))
            {
                clearance = player.AddComponent<CharacterArmClearance>();
            }

            clearance.Bind(animator);
            Assert.That(clearance.IsReady, Is.True, $"{character}: доворот не нашёл объёма торса или костей рук");

            return new Rig { Animator = animator, Clearance = clearance };
        }

        private static void Play(Rig rig, string state)
        {
            rig.Animator.Rebind();
            rig.Animator.Update(0f);
            rig.Animator.Play(state, 0, 0f);
            Advance(rig, Mathf.RoundToInt(SettleTime / Step));
        }

        private static void Advance(Rig rig, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                rig.Animator.Update(Step);
                rig.Clearance.Apply(Step);
            }
        }

        private static string PrefabPath(string character)
        {
            return $"Assets/_Project/Prefabs/Player/{character}.prefab";
        }

        private static string[] BuildDanceNames()
        {
            var names = new string[PlayerEmoteAbility.SectorCount];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = CharacterAnimatorDriver.EmoteStatePrefix + (i + 1);
            }

            return names;
        }
    }
}
