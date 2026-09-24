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
    /// Замер 22.09 на живом аниматоре: у Shlanga руки почти не заходят внутрь
    /// торса ни на одном танце, у остальных заходят до 33 см, и у Толстого на
    /// <c>dance3</c> оба предплечья пропадают внутри живота целиком. Это
    /// пересадка движения худого тела на толстое, а не поломка клипа и не
    /// коллайдер.
    ///
    /// Меряется кожа руки, а не её кость: полутолщину даёт запись
    /// <see cref="CharacterTorsoShape"/>. Первый заход правки считал по
    /// кости и по середине предплечья — и пропускал ровно тот случай, из-за
    /// которого тикет открыли снова: запястье уже снаружи, а локоть и
    /// половина предплечья ещё в животе, и рука перечёркивает футболку.
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
        /// Сколько кожи руки может остаться внутри тела, м. Считается от
        /// поверхности руки, а не от её кости: полутолщину даёт
        /// <see cref="CharacterTorsoShape.Entry.LowerArmRadius"/>.
        ///
        /// Полностью выйти наружу удаётся не везде. Восьмой танец — катание
        /// по полу, рука там прижата телом, и доворот упирается в потолок
        /// в 40°: у Толстого остаётся 14 см из 33. Стоячие танцы вылечены
        /// целиком, их держит <see cref="StandingTolerance"/>.
        /// </summary>
        private const float Tolerance = 0.17f;

        /// <summary>Сколько может остаться на третьем танце Толстого, м: там доворот доходит до конца.</summary>
        private const float StandingTolerance = 0.08f;

        /// <summary>Больше этого доворот на эталонном теле не заходит, град.</summary>
        private const float ReferenceAngleLimit = 25f;

        /// <summary>Меньше этого доворот на толстом теле не имеет права остановиться, град.</summary>
        private const float FatAngleFloor = 30f;

        /// <summary>Кости, по которым считается доворот.</summary>
        private static readonly HumanBodyBones[] ArmBones =
        {
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm
        };

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

            // Без толщины руки доворот выводит наружу кость, а не руку, и
            // половина её остаётся в животе — ровно тот баг, из-за которого
            // у Толстого предплечье перечёркивало футболку.
            Assert.That(entry.UpperArmRadius, Is.InRange(0.02f, 0.25f), $"{character}: не измерена толщина плеча");
            Assert.That(entry.LowerArmRadius, Is.InRange(0.02f, 0.25f), $"{character}: не измерена толщина предплечья");
            Assert.That(entry.HandRadius, Is.InRange(0.02f, 0.25f), $"{character}: не измерена толщина кисти");

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
            Assert.That(left, Is.LessThanOrEqualTo(StandingTolerance),
                $"Fat: после правки рука всё ещё в теле на {left * 100f:F1} см");
        }

        /// <summary>
        /// Эталонное тело правка почти не трогает. Танцы записаны на Shlanga,
        /// и это главный довод, что дело в комплекции, а не в клипах: на
        /// четырёх танцах из восьми доворота нет вовсе, на остальных он не
        /// доходит и до 25° — тогда как толстые тела упираются в потолок
        /// в 40° почти всюду.
        ///
        /// Требовать «ровно ноль» после того, как в расчёт вошла толщина
        /// руки, уже нельзя: на восьмом танце, где рука ложится на пол под
        /// телом, доворот появляется и у Шланги. Позу это не меняет —
        /// кадры до и после совпадают.
        /// </summary>
        [Test]
        public void ReferenceBodyIsBarelyCorrected([ValueSource(nameof(Dances))] string dance)
        {
            Rig rig = Build("Shlanga");
            Play(rig, dance);

            float angle = WatchCorrection(rig, Mathf.CeilToInt(WatchTime / Step));
            Assert.That(angle, Is.LessThanOrEqualTo(ReferenceAngleLimit),
                $"Shlanga, {dance}: доворот на {angle:F0}° — на эталонном теле столько не нужно, " +
                "проверить объём торса и толщину руки");
        }

        /// <summary>
        /// Обратная сторона той же проверки: на толстом теле доворот обязан
        /// быть большим. Без неё «рука в допуске» могло бы означать, что
        /// доворот сдался на первом проходе, а замер сломан.
        /// </summary>
        [Test]
        public void FatBodyIsCorrectedHard([Values("Dance_1", "Dance_3", "Dance_4", "Dance_8")] string dance)
        {
            Rig rig = Build("Fat");
            Play(rig, dance);

            float angle = WatchCorrection(rig, Mathf.CeilToInt(WatchTime / Step));
            Assert.That(angle, Is.GreaterThanOrEqualTo(FatAngleFloor),
                $"Fat, {dance}: доворот всего {angle:F0}° — рука так из живота не выйдет");
        }

        /// <summary>Наибольший доворот кости руки за прогон, град.</summary>
        private static float WatchCorrection(Rig rig, int steps)
        {
            var raw = new Quaternion[ArmBones.Length];
            float angle = 0f;
            for (int i = 0; i < steps; i++)
            {
                rig.Animator.Update(Step);
                for (int b = 0; b < ArmBones.Length; b++)
                {
                    raw[b] = rig.Animator.GetBoneTransform(ArmBones[b]).localRotation;
                }

                rig.Clearance.Apply(Step);

                for (int b = 0; b < ArmBones.Length; b++)
                {
                    angle = Mathf.Max(angle, Quaternion.Angle(raw[b], rig.Animator.GetBoneTransform(ArmBones[b]).localRotation));
                }
            }

            return angle;
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
