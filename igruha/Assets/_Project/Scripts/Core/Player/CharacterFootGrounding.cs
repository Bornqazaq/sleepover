using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Держит ступни персонажа над полом: после того как аниматор поставил
    /// позу, модель приподнимается ровно на столько, на сколько ступня ушла
    /// под пол. Выше пола ступня — подъёма нет, поза не меняется вовсе.
    ///
    /// <b>Зачем.</b> Танцы у всех восьмерых — одни и те же клипы Шланги,
    /// пересаженные на остальные тела, и на более низких телах они опускают
    /// таз ниже, чем позволяет рост. Живой замер 21.09: до 14.7 см под полом
    /// (<c>dance8</c> и <c>dance2</c> у Boss), ходьба в приседе — тоже.
    /// Капсула при этом стоит на полу честно: проваливается только картинка.
    ///
    /// <b>Почему кодом, а не правкой клипов.</b> Анимации, аниматоры и
    /// префабы персонажей заморожены (<c>igruha/CLAUDE.md</c>, раздел 🔒 0).
    /// Подъём модели не меняет ни одного кадра позы: персонаж танцует ровно
    /// так же, только над полом, а не в нём. И работает на любом клипе,
    /// включая те, что добавят потом.
    ///
    /// <b>Пол ищется лучом под каждой ступнёй</b>, а не берётся по низу
    /// капсулы: на склоне нижняя ступня законно стоит ниже центра, и подъём
    /// «до капсулы» подвешивал бы персонажа на каждом пандусе.
    ///
    /// Чисто визуально и считается на каждой машине сама: поза у всех своя
    /// (аниматор идёт у каждой копии), сети здесь делать нечего.
    ///
    /// <b>Кожа, а не только кости (21.09, вечер).</b> Кость голеностопа у
    /// наших персонажей стоит на 11–22 см выше подошвы, и подъём по костям
    /// включался, когда подошва уже глубоко в полу. Хуже всего — падение и
    /// вставание после удара: подошва до 25 см в полу, лежащее тело до 29 см.
    /// Если для персонажа собраны <see cref="CharacterSkinProbes"/>, подъём
    /// считается по настоящим вершинам кожи: подошве, спине, животу, ладоням.
    /// Правило по костям ступней остаётся в силе и поверх этого — нижняя
    /// граница: кость ступни под пол не уходит ни в каком режиме.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterFootGrounding : MonoBehaviour
    {
        private static readonly HumanBodyBones[] FootBones =
        {
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot,
            HumanBodyBones.LeftToes,
            HumanBodyBones.RightToes
        };

        /// <summary>
        /// Пол под ступнёй не бывает выше корня больше чем на это, м. С этой
        /// высоты и пускается луч вниз.
        ///
        /// Выше — уже не пол, а скамья, край стола или ступень, под которую
        /// ступня заехала по ходу танца. Луч, пущенный с высоты колена,
        /// упёрся бы в неё, не нашёл пола под ней и подбросил бы персонажа
        /// на её высоту. Пущенный отсюда — просто проходит под ней.
        /// </summary>
        private const float MaxFloorAboveRoot = 0.12f;

        /// <summary>Сколько ищем пол ниже корня персонажа, м.</summary>
        private const float ProbeDepth = 0.4f;

        /// <summary>
        /// Больше этого не поднимаем никогда, м.
        ///
        /// Самый глубокий провал, замеренный <b>в живой игре</b>, — 14.7 см
        /// (<c>dance8</c> у Boss), следом 14.4 (<c>dance2</c> у Boss) и 13.7
        /// (<c>dance2</c> у MyBoy). Выборка клипа в редакторе показывает почти
        /// вдвое меньше — по ней потолок сперва и поставили в 15 см, впритык,
        /// и следующий клип упёрся бы в него. Двойной запас: подбросить
        /// персонажа потолок не даёт и так — высокие предметы над ступнёй луч
        /// не видит вовсе (см. <see cref="MaxFloorAboveRoot"/>).
        /// </summary>
        private const float MaxLift = 0.30f;

        /// <summary>Меньше этого — шум позы, а не провал. Иначе модель дрожит на миллиметры.</summary>
        private const float LiftDeadZone = 0.003f;

        /// <summary>
        /// Больше этого не поднимаем по коже, м. Замер 21.09 на живом
        /// аниматоре: встающий Толстый уходит в пол до 58 см, а Milez лёжа в
        /// конце падения — до 93: его клипы падения записаны с большим
        /// перепадом высоты. Глубже метра — уже не поза, а ошибка данных.
        /// Поднимается только картинка: капсула персонажа стоит, где стояла.
        /// </summary>
        private const float MaxSkinLift = 1f;

        /// <summary>
        /// Сколько кожи может остаться в полу, м. В обычной стойке подошва
        /// сидит в полу на 0.3–1.7 см — это толщина подошвы, а не провал, и
        /// подвешивать из-за неё персонажа нельзя: стойка не поднимается.
        /// Вычитается, а не обрезает — подъём растёт плавно, без скачка на пороге.
        /// </summary>
        private const float SkinTolerance = 0.02f;

        private readonly Transform[] feet = new Transform[4];
        private int footCount;

        private Animator animator;
        private Transform visualRoot;
        private LayerMask groundLayers;
        private Vector3 baseLocalPosition;
        private float lift;

        private Transform[] skinBones;
        private Matrix4x4[] bindPoses;
        private Matrix4x4[] skinMatrices;
        private CharacterSkinProbes.Probe[] probes;
        private readonly Vector3[] groupLowest = new Vector3[CharacterSkinProbes.GroupCount];
        private float lowestSkinBeforeLift = float.NaN;

        /// <summary>На сколько модель приподнята в этом кадре, м. Ноль — ступни и так над полом.</summary>
        public float CurrentLift => lift;

        /// <summary>Подъём считается по коже, а не только по костям ступней.</summary>
        public bool UsesSkin => probes != null;

        /// <summary>
        /// Мировая высота нижней точки кожи после подъёма. Нет точек кожи —
        /// NaN. Открыта для тестов: проверить надо то, что видит игрок.
        /// </summary>
        public float LowestSkinHeight => lowestSkinBeforeLift + lift;

        /// <summary>
        /// Подключить к модели. Зовёт <see cref="CharacterAnimatorDriver"/>:
        /// он знает и аниматор, и корень модели, а префабы персонажей
        /// заморожены — компонент добавляется кодом, а не в префаб.
        /// </summary>
        public void Bind(Animator modelAnimator, Transform modelRoot, LayerMask ground)
        {
            animator = modelAnimator;
            visualRoot = modelRoot;
            groundLayers = ground;
            lift = 0f;
            footCount = 0;

            if (visualRoot == null || animator == null || !animator.isHuman)
            {
                return;
            }

            baseLocalPosition = visualRoot.localPosition;

            for (int i = 0; i < FootBones.Length; i++)
            {
                Transform bone = animator.GetBoneTransform(FootBones[i]);
                if (bone != null)
                {
                    feet[footCount++] = bone;
                }
            }

            BindSkin();
        }

        /// <summary>
        /// Найти точки кожи этого персонажа. Нет записи или она собрана под
        /// другую сетку — остаёмся на костях ступней: персонаж без данных не
        /// ломается, а просто стоит, как стоял до них.
        /// </summary>
        private void BindSkin()
        {
            probes = null;
            skinBones = null;
            lowestSkinBeforeLift = float.NaN;

            CharacterSkinProbes library = CharacterSkinProbes.Shared;
            var skin = visualRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (library == null || skin == null || !library.TryGet(skin.sharedMesh, out CharacterSkinProbes.Entry entry))
            {
                return;
            }

            Transform[] bones = skin.bones;
            if (entry.Probes == null || entry.Probes.Length == 0 ||
                entry.BindPoses == null || entry.BindPoses.Length != bones.Length)
            {
                return;
            }

            skinBones = bones;
            bindPoses = entry.BindPoses;
            skinMatrices = new Matrix4x4[bones.Length];
            probes = entry.Probes;
        }

        /// <summary>
        /// После аниматора: он пишет позу до LateUpdate, и ступни здесь стоят
        /// ровно там, где их нарисует кадр.
        /// </summary>
        private void LateUpdate()
        {
            Apply();
        }

        /// <summary>
        /// Посчитать и поставить подъём по текущей позе. Открыт для тестов:
        /// в режиме редактора LateUpdate не идёт, а проверить надо ровно эту
        /// математику на настоящем клипе.
        /// </summary>
        public void Apply()
        {
            // Аниматор выключен — позу ведёт не он, а, например, ragdoll
            // «Рейса на память». Подъём тогда не трогаем вовсе: сдвиг корня
            // под физическими костями дёрнул бы тело.
            if (visualRoot == null || footCount == 0 || animator == null || !animator.isActiveAndEnabled)
            {
                return;
            }

            float needed = RequiredLift();
            float maxLift = MaxLift;
            if (probes != null)
            {
                needed = Mathf.Max(needed, RequiredSkinLift() - SkinTolerance);
                maxLift = MaxSkinLift;
            }

            float next = needed < LiftDeadZone ? 0f : Mathf.Min(needed, maxLift);
            if (Mathf.Approximately(next, lift))
            {
                return;
            }

            lift = next;
            Transform parent = visualRoot.parent;
            Vector3 offset = parent != null ? parent.InverseTransformVector(Vector3.up * lift) : Vector3.up * lift;
            visualRoot.localPosition = baseLocalPosition + offset;
        }

        /// <summary>
        /// Насколько самая глубокая ступня ушла под пол, м, — без учёта
        /// подъёма, уже стоящего с прошлого кадра: кости едут вместе с корнем,
        /// и без вычета подъём раскачивался бы сам от себя.
        /// </summary>
        private float RequiredLift()
        {
            PhysicsScene physics = gameObject.scene.GetPhysicsScene();
            float rootY = transform.position.y;
            float needed = 0f;

            for (int i = 0; i < footCount; i++)
            {
                Vector3 foot = feet[i].position;
                float rawY = foot.y - lift;

                var origin = new Vector3(foot.x, rootY + MaxFloorAboveRoot, foot.z);
                if (!physics.Raycast(origin, Vector3.down, out RaycastHit hit, MaxFloorAboveRoot + ProbeDepth,
                        groundLayers, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                float depth = hit.point.y - rawY;
                if (depth > needed)
                {
                    needed = depth;
                }
            }

            return needed;
        }

        /// <summary>
        /// Насколько кожа ушла в пол, м. Точки считаются по позам костей и
        /// весам — как их считает видеокарта, — поэтому лежат ровно на коже,
        /// которую видит игрок, и тело не зависает над полом из-за грубой
        /// оценки у сустава.
        ///
        /// Пол ищется не под каждой точкой — их сотни, — а под самой нижней
        /// точкой каждой части тела: на склоне у двух ног разный пол.
        /// Подъём прошлого кадра вычитается, как и по костям ступней.
        /// </summary>
        private float RequiredSkinLift()
        {
            for (int i = 0; i < skinBones.Length; i++)
            {
                skinMatrices[i] = skinBones[i].localToWorldMatrix * bindPoses[i];
            }

            for (int g = 0; g < groupLowest.Length; g++)
            {
                groupLowest[g] = new Vector3(0f, float.MaxValue, 0f);
            }

            float lowest = float.MaxValue;
            for (int i = 0; i < probes.Length; i++)
            {
                Vector3 point = Skin(probes[i]);
                point.y -= lift;

                int group = probes[i].Group;
                if (point.y < groupLowest[group].y)
                {
                    groupLowest[group] = point;
                }

                if (point.y < lowest)
                {
                    lowest = point.y;
                }
            }

            lowestSkinBeforeLift = lowest;

            // Вся кожа выше любого пола, который мы признаём, — прыжок или
            // полёт после удара. Лучи тогда не нужны вовсе.
            float floorCeiling = transform.position.y + MaxFloorAboveRoot;
            if (lowest > floorCeiling)
            {
                return 0f;
            }

            PhysicsScene physics = gameObject.scene.GetPhysicsScene();
            float needed = 0f;

            for (int g = 0; g < groupLowest.Length; g++)
            {
                Vector3 point = groupLowest[g];
                if (point.y > floorCeiling)
                {
                    continue;
                }

                var origin = new Vector3(point.x, floorCeiling, point.z);
                if (!physics.Raycast(origin, Vector3.down, out RaycastHit hit, MaxFloorAboveRoot + ProbeDepth,
                        groundLayers, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                float depth = hit.point.y - point.y;
                if (depth > needed)
                {
                    needed = depth;
                }
            }

            return needed;
        }

        /// <summary>Мировое положение вершины кожи в текущей позе.</summary>
        private Vector3 Skin(in CharacterSkinProbes.Probe probe)
        {
            Vector4 weights = probe.Weights;
            Vector3 result = skinMatrices[probe.Bone(0)].MultiplyPoint3x4(probe.Position) * weights.x;

            if (weights.y > 0f)
            {
                result += skinMatrices[probe.Bone(1)].MultiplyPoint3x4(probe.Position) * weights.y;
            }

            if (weights.z > 0f)
            {
                result += skinMatrices[probe.Bone(2)].MultiplyPoint3x4(probe.Position) * weights.z;
            }

            if (weights.w > 0f)
            {
                result += skinMatrices[probe.Bone(3)].MultiplyPoint3x4(probe.Position) * weights.w;
            }

            return result;
        }
    }
}
