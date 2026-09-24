using System.Collections;
using System.Collections.Generic;
using Igruha.Core.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Igruha.Tests.PlayMode
{
    /// <summary>
    /// Ступни персонажей не уходят под пол ни в одном танце и в ходьбе
    /// в приседе — у всех восьми, <b>в живом аниматоре</b>.
    ///
    /// <b>Почему PlayMode.</b> Выборка клипа в редакторе
    /// (<c>AnimationMode.SampleAnimationClip</c>) ставит тело заметно выше,
    /// чем настоящий аниматор, а <c>Animator.Update</c> в редакторе состояние
    /// переключает, но позу на кости не пишет. Проверка по ним была зелёной,
    /// пока геймдизайнер видел в игре ноги в полу (21.09).
    ///
    /// <b>Две грабли, каждая давала пустой зелёный тест</b> (24.09):
    /// персонажи размечены <c>CullUpdateTransforms</c>, и без камеры в сцене
    /// аниматор не двигает кости вовсе; а запечённая <c>BakeMesh</c> сетка
    /// вне кадра остаётся позой покоя — она показывала +0.5 см, когда кости
    /// были на 30 см под полом. Поэтому меряем кости, а отсечение снимаем.
    ///
    /// <b>Кисти сюда не входят намеренно.</b> В <c>dance8</c> рука проносится
    /// сквозь пол на 17–32 см — глубже собственных подошв на 10–19 см, и у
    /// Шланги, которому эти клипы принадлежат, тоже. Это свойство клипа, а не
    /// посадки: поднять тело до кисти значит подвесить персонажа в воздухе.
    /// Решение по клипу за геймдизайнером, см. STATE.
    /// </summary>
    public sealed class LiveFootGroundingTests
    {
        private static readonly string[] Characters = { "Aza", "Boss", "Fat", "Girl", "Milez", "MyBoy", "Shlanga", "Player" };

        /// <summary>Сколько танцев в колесе эмоций — столько и проверяем.</summary>
        private const int DanceCount = PlayerEmoteAbility.SectorCount;

        /// <summary>Сколько сетки может оказаться под полом, м: полсантиметра — шум позы.</summary>
        private const float Tolerance = 0.005f;

        /// <summary>
        /// Насколько модель может быть приподнята в обычной стойке, м.
        ///
        /// Ноль сюда не годится: у части персонажей поза покоя сама уводит
        /// кость носка на сантиметр-полтора ниже нуля (у Милеза — 1.5 см), и
        /// подъём там как раз ставит подошву на пол, а не подвешивает тело.
        /// Порог ловит грубую поломку — «все встали на табуретку», — а не
        /// разницу между костью носка и подошвой.
        /// </summary>
        private const float IdleLiftLimit = 0.03f;

        /// <summary>Сверх длины клипа: переход в танец и запас на последний кадр, с.</summary>
        private const float DanceMargin = 0.4f;

        /// <summary>Сколько секунд смотреть ходьбу в приседе — несколько шагов.</summary>
        private const float CrouchWalkSeconds = 2.5f;

        /// <summary>Расстояние между персонажами на полу, м: чтобы танцующие не задевали друг друга.</summary>
        private const float Spacing = 6f;

        private static readonly int EmoteHash = Animator.StringToHash("Emote");
        private static readonly int EmotePlayHash = Animator.StringToHash("EmotePlay");
        private static readonly int EmoteStopHash = Animator.StringToHash("EmoteStop");
        private static readonly int CrouchHash = Animator.StringToHash("Crouch");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");

        private readonly List<Object> spawned = new List<Object>();

        private sealed class LiveRig
        {
            public string Name;
            public GameObject Body;
            public Animator Animator;
            public CharacterFootGrounding Grounding;
            public SkinnedMeshRenderer[] Skins;
            public float Worst;
            public string WorstWhere;
            public int Measured;
            public string ClipsSeen;

        }

        [SetUp]
        public void Open()
        {
            // Без этого плей-мод замирает, когда окно Unity не в фокусе, и
            // прогон висит с пометкой editor_unfocused: тест минутный, и
            // сидеть над ним, не трогая мышь, никто не будет.
            Application.runInBackground = true;
        }

        [TearDown]
        public void Close()
        {
            foreach (Object item in spawned)
            {
                if (item != null)
                {
                    Object.Destroy(item);
                }
            }

            spawned.Clear();
        }

        [UnityTest]
        public IEnumerator NoDanceOrCrouchWalkPutsAnyPartUnderTheFloor()
        {
            var rigs = new List<LiveRig>();
            for (int i = 0; i < Characters.Length; i++)
            {
                rigs.Add(Spawn(Characters[i], new Vector3(i * Spacing, 0f, 0f)));
            }

            BuildFloor(rigs[0].Body.GetComponent<PlayerController>().GroundLayers, Characters.Length * Spacing);

            // Кадр на Awake и Start: подъём подключается именно там, и
            // проверяется этот путь, а не собранный руками компонент.
            yield return null;

            var failures = new List<string>();
            foreach (LiveRig rig in rigs)
            {
                if (rig.Grounding == null)
                {
                    failures.Add($"{rig.Name}: подъём не подключился к персонажу");
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));

            yield return Watch(rigs, 1f, "стойка");
            foreach (LiveRig rig in rigs)
            {
                Debug.Log($"{rig.Name}: подъём в стойке {rig.Grounding.CurrentLift * 100f:F1} см");
                if (rig.Grounding.CurrentLift > IdleLiftLimit)
                {
                    failures.Add($"{rig.Name}: в стойке модель поднята на {rig.Grounding.CurrentLift * 100f:F1} см — это уже не поправка позы");
                }
            }

            for (int dance = 1; dance <= DanceCount; dance++)
            {
                float length = 0f;
                foreach (LiveRig rig in rigs)
                {
                    rig.Animator.SetInteger(EmoteHash, dance);
                    rig.Animator.SetTrigger(EmotePlayHash);
                    length = Mathf.Max(length, ClipLength(rig.Animator, "dance" + dance));
                }

                yield return Watch(rigs, length + DanceMargin, "dance" + dance);
            }

            foreach (LiveRig rig in rigs)
            {
                rig.Animator.SetInteger(EmoteHash, PlayerEmoteAbility.NoEmote);
                rig.Animator.SetTrigger(EmoteStopHash);
                rig.Animator.SetBool(CrouchHash, true);
                rig.Animator.SetFloat(SpeedHash, 1f);
            }

            yield return Watch(rigs, CrouchWalkSeconds, "ходьба в приседе");

            foreach (LiveRig rig in rigs)
            {
                Debug.Log($"{rig.Name}: кадров {rig.Measured}, ступни ниже всего {rig.Worst * 100f:F2} см ({rig.WorstWhere}), клипы [{rig.ClipsSeen}]");
            }

            // Сам тест обязан доказать, что он что-то видел: пустая проверка
            // уже давала зелёный свет, пока в игре руки уходили в пол.
            foreach (LiveRig rig in rigs)
            {
                if (rig.Measured == 0)
                {
                    failures.Add($"{rig.Name}: не удалось измерить ни одного кадра — проверять было нечем");
                }

                for (int dance = 1; dance <= DanceCount; dance++)
                {
                    if (!rig.ClipsSeen.Contains("dance" + dance))
                    {
                        failures.Add($"{rig.Name}: танец dance{dance} так и не включился, видели только [{rig.ClipsSeen}]");
                    }
                }

                if (rig.Worst < -Tolerance)
                {
                    failures.Add($"{rig.Name}: {rig.WorstWhere} — ступня на {-rig.Worst * 100f:F1} см под полом");
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        /// <summary>
        /// Смотреть заданное время и помнить худшую точку каждого.
        ///
        /// Меряем сразу после <c>yield return null</c>: в этот момент на
        /// костях ещё поза прошлого кадра — ровно та, что ушла на экран,
        /// уже с подъёмом, поставленным в LateUpdate.
        /// </summary>
        private IEnumerator Watch(List<LiveRig> rigs, float seconds, string what)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                yield return null;
                foreach (LiveRig rig in rigs)
                {
                    AnimatorClipInfo[] playing = rig.Animator.GetCurrentAnimatorClipInfo(0);
                    if (playing.Length > 0 && !rig.ClipsSeen.Contains(playing[0].clip.name))
                    {
                        rig.ClipsSeen += playing[0].clip.name + " ";
                    }

                    float feet = LowestFoot(rig);
                    if (float.IsInfinity(feet) || feet > 100f)
                    {
                        continue;
                    }

                    rig.Measured++;
                    if (feet < rig.Worst)
                    {
                        rig.Worst = feet;
                        rig.WorstWhere = what;
                    }
                }
            }
        }

        private static float LowestFoot(LiveRig rig)
        {
            float floor = rig.Body.transform.position.y;
            float lowest = float.MaxValue;
            foreach (HumanBodyBones id in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
            {
                Transform bone = rig.Animator.GetBoneTransform(id);
                if (bone != null)
                {
                    lowest = Mathf.Min(lowest, bone.position.y - floor);
                }
            }

            return lowest;
        }

        /// <summary>
        /// Персонаж, чью позу ведёт только аниматор — как чужая копия в
        /// сетевой катке: без мотора, ввода и драйвера, на месте и без физики.
        /// </summary>
        private LiveRig Spawn(string character, Vector3 position)
        {
            GameObject prefab = LoadPrefab(character);
            Assert.That(prefab, Is.Not.Null, $"нет префаба персонажа {character}");

            GameObject body = Object.Instantiate(prefab, position, Quaternion.identity);
            spawned.Add(body);
            body.name = "Live_" + character;

            var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            // Вне кадра скиннинг не пересчитывается, и запечённая сетка
            // остаётся позой покоя: замер 24.09 показывал +0.5 см, когда
            // кости были на 32 см под полом.
            foreach (var skin in skins) skin.updateWhenOffscreen = true;
            Assert.That(skins.Length, Is.GreaterThan(0), $"{character}: у персонажа нет скиннинговых мешей — мерить нечего");

            var rigidbody = body.GetComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.interpolation = RigidbodyInterpolation.None;

            // Камеры в тестовой сцене нет, а персонажи размечены как
            // CullUpdateTransforms: вне кадра аниматор кости не двигает вовсе.
            // Без этой строки тест мерил неподвижную позу и был зелёным при
            // любом провале — проверено 24.09, обнулённый подъём он не замечал.
            var animator = body.GetComponentInChildren<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            Disable<PlayerController>(body);
            Disable<CharacterAnimatorDriver>(body);
            Disable<PlayerEmoteAbility>(body);
            Disable<PlayerInputReader>(body);
            Disable<StuckDetector>(body);
            body.transform.position = position;

            return new LiveRig
            {
                Name = character,
                Body = body,
                Animator = animator,
                Grounding = body.GetComponent<CharacterFootGrounding>(),
                Skins = skins,
                Worst = float.MaxValue,
                WorstWhere = "—",
                Measured = 0,
                ClipsSeen = string.Empty
            };
        }

        private static void Disable<T>(GameObject body) where T : Behaviour
        {
            if (body.TryGetComponent(out T component))
            {
                component.enabled = false;
            }
        }

        private void BuildFloor(LayerMask ground, float width)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(floor);
            floor.transform.localScale = new Vector3(width + Spacing * 2f, 1f, Spacing * 2f);
            floor.transform.position = new Vector3(width * 0.5f - Spacing * 0.5f, -0.5f, 0f);

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

        private static float ClipLength(Animator animator, string clipName)
        {
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip != null && clip.name == clipName)
                {
                    return clip.length;
                }
            }

            return 0f;
        }

        private static GameObject LoadPrefab(string character)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Player/{character}.prefab");
#else
            return null;
#endif
        }
    }
}
