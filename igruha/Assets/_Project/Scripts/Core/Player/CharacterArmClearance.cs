using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Держит руки снаружи тела на танцах: после того как аниматор поставил
    /// позу, рука, ушедшая внутрь торса, доворачивается в плече и локте ровно
    /// настолько, чтобы выйти на поверхность. Рука снаружи — доворота нет,
    /// поза не меняется вовсе.
    ///
    /// <b>Зачем.</b> Все восемь персонажей танцуют одними клипами —
    /// <c>Shlanga@dance1..8</c>. Замер 22.09 на живом аниматоре, глубина
    /// захода кожи руки внутрь <see cref="CharacterTorsoShape"/> — до
    /// доворота и после, см, и наибольший доворот:
    ///
    /// <code>
    ///               d1        d3        d4        d8
    ///   Shlanga    0-&gt;0/0°   2-&gt;1/4°   8-&gt;2/14°  15-&gt;5/21°  ← эталон
    ///   Fat       22-&gt;4/39° 33-&gt;3/40° 29-&gt;11/40° 33-&gt;15/40°
    ///   Boss      18-&gt;8/40° 31-&gt;5/40° 30-&gt;15/40° 29-&gt;16/40°
    /// </code>
    ///
    /// У Толстого на <c>dance3</c> оба предплечья пропадают внутри живота
    /// целиком — это и есть «модель косаебит». Коллайдер тут ни при чём: он
    /// на анимацию не влияет. Та же природа, что у ступней под полом, которые
    /// поднимает <see cref="CharacterFootGrounding"/>.
    ///
    /// <b>Меряется кожа, а не кость.</b> Первый заход правки выводил наружу
    /// кость и смотрел на предплечье от середины. У Толстого этого хватило,
    /// чтобы отчитаться нулём, и не хватило, чтобы вылечить картинку:
    /// запястье с кулаком уже снаружи, а локоть и половина предплечья ещё в
    /// животе — на экране рука перечёркивала футболку поперёк. Поэтому в
    /// набор точек добавлен локоть, а к радиусу тела — полутолщина руки
    /// (<see cref="CharacterTorsoShape.Entry.LowerArmRadius"/> и соседи).
    ///
    /// <b>Почему кодом, а не правкой клипов.</b> Анимации, аниматоры и
    /// префабы персонажей заморожены (<c>igruha/CLAUDE.md</c>, раздел 🔒 0).
    /// Доворот не трогает ни одного кадра клипа и работает на любом танце,
    /// включая те, что добавят потом. Клипы у всех общие — правка клипа ради
    /// Толстого сломала бы танец у худых.
    ///
    /// <b>Только на танцах.</b> Замер по остальным состояниям (покой, бег,
    /// прыжок, присед, падение, подъём) даёт ноль у всех восьмерых, а удар —
    /// 3 см у одного Boss: кросс идёт поперёк груди, и это так и задумано.
    /// Удар заморожен отдельным пунктом, поэтому доворот включается только
    /// пока на базовом слое играет <c>Dance_N</c>, и гаснет за
    /// <see cref="FadeTime"/> при выходе из него.
    ///
    /// Чисто визуально и считается на каждой машине сама: поза у всех своя
    /// (аниматор идёт у каждой копии), сети здесь делать нечего — ровно как
    /// с подъёмом ступней.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1)]
    public sealed class CharacterArmClearance : MonoBehaviour
    {
        /// <summary>
        /// Зазор между кожей руки и кожей тела, м. Толщину самой руки
        /// добавляет <see cref="CharacterTorsoShape.Entry.LowerArmRadius"/>
        /// и его соседи, так что запас отвечает только за то, чтобы кожа
        /// руки не сливалась с кожей живота.
        /// </summary>
        private const float Margin = 0.02f;

        /// <summary>
        /// Больше этого плечо не доворачивает, град. Потолок нужен на случай
        /// позы, которую доворотом не спасти: лучше оставить провал, чем
        /// вывернуть руку за спину.
        ///
        /// На толстых телах он упирается почти всюду, и часть провала
        /// остаётся — 15 см из 33 на восьмом танце, где персонаж катается по
        /// полу и рука прижата телом. Поднимать потолок ради этих кадров
        /// нельзя: на стоячих танцах, где доворота хватает с запасом, рука
        /// начнёт отходить от тела дальше, чем нужно.
        /// </summary>
        private const float MaxShoulderAngle = 40f;

        /// <summary>Больше этого локоть не доворачивает, град.</summary>
        private const float MaxElbowAngle = 40f;

        /// <summary>
        /// Сколько раз подряд ищем самую глубокую точку и выталкиваем её.
        /// Первый доворот выводит наружу её, но может завести вторую; трёх
        /// проходов хватает, потому что точек всего четыре и лежат они на
        /// одной прямой.
        /// </summary>
        private const int ShoulderPasses = 3;

        /// <summary>Сколько проходов добирает локоть — ему остаются только точки кисти.</summary>
        private const int ElbowPasses = 2;

        /// <summary>
        /// Постоянная времени сглаживания доворота, с. Цель считается по сырой
        /// позе и потому сама по себе непрерывна; сглаживание убирает скачок в
        /// тот кадр, когда самой глубокой становится другая точка.
        /// </summary>
        private const float SmoothingTime = 0.05f;

        /// <summary>За сколько доворот набирает полную силу на входе в танец и гаснет на выходе, с.</summary>
        private const float FadeTime = 0.15f;

        /// <summary>Меньше этого — касание, а не провал: доворачивать нечего.</summary>
        private const float DeadZone = 0.002f;

        /// <summary>Базовый слой аниматора — танцы живут только на нём.</summary>
        private const int BaseLayer = 0;

        /// <summary>Точек на руке: локоть, три по предплечью до запястья, кончик ладони.</summary>
        private const int PointCount = 5;

        /// <summary>
        /// Где вдоль предплечья стоят первые четыре точки: 0 — локоть,
        /// 1 — запястье.
        ///
        /// Локоть здесь не для красоты. Прежний набор начинался с середины
        /// предплечья, и у Толстого сходилось так: запястье с кулаком уже
        /// снаружи, а локоть и половина предплечья всё ещё в животе —
        /// на картинке рука перечёркивала футболку поперёк. Проверка, не
        /// видящая локтя, объявляла позу вылеченной.
        /// </summary>
        private static readonly float[] ForearmPoints = { 0f, 0.35f, 0.7f, 1f };

        /// <summary>
        /// С какой точки начинает локоть. Локоть — сам сустав, а точку рядом
        /// с суставом поворот в нём почти не двигает: рычаг мал, и доворот
        /// вышел бы огромным ради сантиметра.
        /// </summary>
        private const int ElbowFirstPoint = 2;

        /// <summary>Имена состояний танца в контроллере — по ним и включается доворот.</summary>
        private static readonly int[] EmoteStates = BuildEmoteStates();

        /// <summary>Одна рука: кости, накопленный доворот и то, что мы записали в прошлый кадр.</summary>
        private struct Arm
        {
            public Transform Upper;
            public Transform Lower;
            public Transform Hand;
            public Vector3 HandTip;
            public Quaternion UpperCorrection;
            public Quaternion LowerCorrection;
            public Quaternion WrittenUpper;
            public Quaternion WrittenLower;
        }

        private Animator animator;
        private Transform[] bones;
        private CharacterTorsoShape.Slice[] slices;
        private Vector3[] sliceCenters;
        private Vector3[] sliceAxes;
        private Vector3[] sliceSides;
        private Vector3[] sliceFronts;
        private readonly Arm[] arms = new Arm[2];
        private readonly Vector3[] points = new Vector3[PointCount];

        /// <summary>
        /// Полутолщина руки в каждой точке, м. Кость идёт по середине руки,
        /// и без этой поправки «кость снаружи» означает «половина руки ещё
        /// внутри»: у Толстого это 9.6 см предплечья в животе.
        /// </summary>
        private readonly float[] pointRadii = new float[PointCount];

        private bool ready;
        private bool written;
        private float weight;

        /// <summary>Самый глубокий заход руки в тело в этом кадре до доворота, м.</summary>
        public float WorstDepth { get; private set; }

        /// <summary>Он же после доворота, м. По нему и проверяется правка в тестах.</summary>
        public float RemainingDepth { get; private set; }

        /// <summary>Доворот работает: найден объём торса и обе руки.</summary>
        public bool IsReady => ready;

        /// <summary>Сила доворота сейчас: 0 вне танца, 1 в танце.</summary>
        public float Weight => weight;

        /// <summary>
        /// Подключить к модели. Зовёт <see cref="CharacterAnimatorDriver"/>:
        /// он знает аниматор, а префабы персонажей заморожены — компонент
        /// добавляется кодом, а не в префаб.
        ///
        /// Персонаж без записи объёма торса не ломается: доворота у него
        /// просто нет, поза остаётся такой же, как была до этого компонента.
        /// </summary>
        public void Bind(Animator modelAnimator)
        {
            ready = false;
            written = false;
            weight = 0f;
            animator = modelAnimator;

            if (animator == null || !animator.isHuman)
            {
                return;
            }

            CharacterTorsoShape library = CharacterTorsoShape.Shared;
            var skin = animator.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (library == null || skin == null || !library.TryGet(skin.sharedMesh, out CharacterTorsoShape.Entry entry))
            {
                return;
            }

            bones = skin.bones;
            if (entry.Slices == null || entry.Slices.Length == 0 || entry.BoneCount != bones.Length)
            {
                return;
            }

            for (int i = 0; i < entry.Slices.Length; i++)
            {
                if (entry.Slices[i].Bone < 0 || entry.Slices[i].Bone >= bones.Length)
                {
                    return;
                }
            }

            if (!BindArm(0, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, entry.LeftHandTip) ||
                !BindArm(1, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, entry.RightHandTip))
            {
                return;
            }

            // Локоть толщиной с конец плеча, а не с предплечье: у Толстого
            // это разница в три сантиметра, и берём большее.
            pointRadii[0] = Mathf.Max(entry.UpperArmRadius, entry.LowerArmRadius);
            for (int i = 1; i < ForearmPoints.Length; i++)
            {
                pointRadii[i] = entry.LowerArmRadius;
            }

            pointRadii[ForearmPoints.Length] = entry.HandRadius;

            slices = entry.Slices;
            sliceCenters = new Vector3[slices.Length];
            sliceAxes = new Vector3[slices.Length];
            sliceSides = new Vector3[slices.Length];
            sliceFronts = new Vector3[slices.Length];
            ready = true;
        }

        private bool BindArm(int index, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, Vector3 handTip)
        {
            Transform upperBone = animator.GetBoneTransform(upper);
            Transform lowerBone = animator.GetBoneTransform(lower);
            Transform handBone = animator.GetBoneTransform(hand);
            if (upperBone == null || lowerBone == null || handBone == null)
            {
                return false;
            }

            arms[index] = new Arm
            {
                Upper = upperBone,
                Lower = lowerBone,
                Hand = handBone,
                HandTip = handTip,
                UpperCorrection = Quaternion.identity,
                LowerCorrection = Quaternion.identity,
                WrittenUpper = Quaternion.identity,
                WrittenLower = Quaternion.identity
            };
            return true;
        }

        /// <summary>
        /// После аниматора: он пишет позу до LateUpdate, и кости здесь стоят
        /// ровно там, где их нарисует кадр. Порядком выполнения компонент
        /// поставлен перед <see cref="CharacterFootGrounding"/>: тот считает
        /// нижнюю точку кожи по всему телу, включая ладони.
        /// </summary>
        private void LateUpdate()
        {
            Apply(Time.deltaTime);
        }

        /// <summary>
        /// Посчитать и применить доворот по текущей позе. Открыт для тестов:
        /// в режиме редактора LateUpdate не идёт, а проверить надо ровно эту
        /// математику на настоящем клипе.
        /// </summary>
        public void Apply(float deltaTime)
        {
            WorstDepth = 0f;
            RemainingDepth = 0f;

            if (!ready)
            {
                return;
            }

            // Аниматор выключен — позу ведёт не он, а, например, ragdoll
            // «Рейса на память». Доворот тогда не трогаем вовсе.
            if (animator == null || !animator.isActiveAndEnabled)
            {
                Forget();
                return;
            }

            // Персонажа не видно — у наших стоит CullUpdateTransforms, и
            // аниматор кости не пишет. Крутить второй раз поверх своего же
            // доворота нельзя: рука уедет по кругу.
            if (written && KeptOurPose())
            {
                return;
            }

            float target = IsEmoteRunning() ? 1f : 0f;
            weight = FadeTime > 0f ? Mathf.MoveTowards(weight, target, deltaTime / FadeTime) : target;

            // Вне танца кости остаются ровно такими, какими их написал
            // аниматор: мы их не касаемся и ничего не считаем.
            if (weight <= 0f)
            {
                Forget();
                return;
            }

            CacheSlices();
            float smoothing = SmoothingTime > 0f ? 1f - Mathf.Exp(-deltaTime / SmoothingTime) : 1f;
            for (int i = 0; i < arms.Length; i++)
            {
                SolveArm(i, smoothing);
            }

            written = true;
        }

        /// <summary>Забыть доворот: поза снова целиком за аниматором.</summary>
        private void Forget()
        {
            weight = 0f;
            written = false;
            for (int i = 0; i < arms.Length; i++)
            {
                arms[i].UpperCorrection = Quaternion.identity;
                arms[i].LowerCorrection = Quaternion.identity;
            }
        }

        /// <summary>Кости стоят ровно там, где мы их оставили, — значит аниматор в этом кадре не писал.</summary>
        private bool KeptOurPose()
        {
            for (int i = 0; i < arms.Length; i++)
            {
                if (arms[i].Upper.localRotation != arms[i].WrittenUpper ||
                    arms[i].Lower.localRotation != arms[i].WrittenLower)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// На базовом слое играет танец. Переход считается танцем с первого
        /// кадра: рука входит в тело уже на смешивании, а сила доворота всё
        /// равно набирается плавно.
        /// </summary>
        private bool IsEmoteRunning()
        {
            if (animator.layerCount <= BaseLayer)
            {
                return false;
            }

            if (IsEmoteState(animator.GetCurrentAnimatorStateInfo(BaseLayer).shortNameHash))
            {
                return true;
            }

            return animator.IsInTransition(BaseLayer) &&
                   IsEmoteState(animator.GetNextAnimatorStateInfo(BaseLayer).shortNameHash);
        }

        private static bool IsEmoteState(int hash)
        {
            for (int i = 0; i < EmoteStates.Length; i++)
            {
                if (EmoteStates[i] == hash)
                {
                    return true;
                }
            }

            return false;
        }

        private static int[] BuildEmoteStates()
        {
            var hashes = new int[PlayerEmoteAbility.SectorCount];
            for (int i = 0; i < hashes.Length; i++)
            {
                hashes[i] = Animator.StringToHash(CharacterAnimatorDriver.EmoteStatePrefix + (i + 1));
            }

            return hashes;
        }

        /// <summary>
        /// Перевести срезы торса в мир один раз на кадр: точек на руках
        /// восемь, проходов по ним пять, и пересчитывать кость на каждый
        /// запрос — впустую гонять матрицы.
        /// </summary>
        private void CacheSlices()
        {
            for (int i = 0; i < slices.Length; i++)
            {
                Transform bone = bones[slices[i].Bone];
                sliceCenters[i] = bone.TransformPoint(slices[i].Center);
                sliceAxes[i] = bone.TransformDirection(slices[i].Axis).normalized;
                sliceSides[i] = bone.TransformDirection(slices[i].Side).normalized;
                sliceFronts[i] = bone.TransformDirection(slices[i].Front).normalized;
            }
        }

        /// <summary>
        /// Вывести одну руку наружу. Цель считается на живых костях — их
        /// вертим и меряем заново, — а в конце кости возвращаются в сырую позу
        /// и получают сглаженный доворот.
        /// </summary>
        private void SolveArm(int index, float smoothing)
        {
            Arm arm = arms[index];
            Quaternion rawUpper = arm.Upper.localRotation;
            Quaternion rawLower = arm.Lower.localRotation;

            float used = 0f;
            for (int pass = 0; pass < ShoulderPasses && used < MaxShoulderAngle; pass++)
            {
                used += Push(arm, arm.Upper, 0, MaxShoulderAngle - used, pass == 0);
            }

            float usedElbow = 0f;
            for (int pass = 0; pass < ElbowPasses && usedElbow < MaxElbowAngle; pass++)
            {
                usedElbow += Push(arm, arm.Lower, ElbowFirstPoint, MaxElbowAngle - usedElbow, false);
            }

            Quaternion targetUpper = Quaternion.Inverse(rawUpper) * arm.Upper.localRotation;
            Quaternion targetLower = Quaternion.Inverse(rawLower) * arm.Lower.localRotation;

            arm.UpperCorrection = Quaternion.Slerp(arm.UpperCorrection, targetUpper, smoothing);
            arm.LowerCorrection = Quaternion.Slerp(arm.LowerCorrection, targetLower, smoothing);

            arm.Upper.localRotation = rawUpper * Quaternion.Slerp(Quaternion.identity, arm.UpperCorrection, weight);
            arm.Lower.localRotation = rawLower * Quaternion.Slerp(Quaternion.identity, arm.LowerCorrection, weight);

            arm.WrittenUpper = arm.Upper.localRotation;
            arm.WrittenLower = arm.Lower.localRotation;
            arms[index] = arm;

            FillPoints(arm);
            float left = Deepest(0, out Vector3 _, out Vector3 _);
            if (left > RemainingDepth)
            {
                RemainingDepth = left;
            }
        }

        /// <summary>
        /// Один доворот кости вокруг её сустава: найти самую глубокую точку и
        /// вывести её наружу. Возвращает, на сколько градусов повернули.
        /// </summary>
        private float Push(Arm arm, Transform bone, int from, float budget, bool recordWorst)
        {
            FillPoints(arm);
            float depth = Deepest(from, out Vector3 point, out Vector3 outward);
            if (recordWorst && depth > WorstDepth)
            {
                WorstDepth = depth;
            }

            if (depth <= DeadZone)
            {
                return 0f;
            }

            Vector3 lever = point - bone.position;
            Vector3 axis = Vector3.Cross(lever, outward);
            if (axis.sqrMagnitude < 1e-8f)
            {
                return 0f;
            }

            axis.Normalize();

            // Сколько метров наружу даёт один радиан поворота. У руки,
            // вытянутой вдоль направления выталкивания, он даёт ноль —
            // такую позу доворотом в этом суставе не спасти.
            float gain = Vector3.Dot(Vector3.Cross(axis, lever), outward);
            if (gain < 1e-4f)
            {
                return 0f;
            }

            float angle = Mathf.Min(depth / gain * Mathf.Rad2Deg, budget);
            bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
            return angle;
        }

        /// <summary>Точки руки в мире: локоть, предплечье, запястье, кончик ладони.</summary>
        private void FillPoints(Arm arm)
        {
            Vector3 elbow = arm.Lower.position;
            Vector3 wrist = arm.Hand.position;
            for (int i = 0; i < ForearmPoints.Length; i++)
            {
                points[i] = Vector3.LerpUnclamped(elbow, wrist, ForearmPoints[i]);
            }

            points[ForearmPoints.Length] = arm.Hand.TransformPoint(arm.HandTip);
        }

        /// <summary>
        /// Самый глубокий заход точек руки внутрь торса, м, и куда выталкивать.
        /// Точки до <paramref name="from"/> пропускаются: локтю достаётся
        /// только кисть, предплечье он двигать не умеет.
        /// </summary>
        private float Deepest(int from, out Vector3 point, out Vector3 outward)
        {
            float worst = 0f;
            point = Vector3.zero;
            outward = Vector3.zero;

            for (int i = from; i < PointCount; i++)
            {
                for (int s = 0; s < slices.Length; s++)
                {
                    Vector3 offset = points[i] - sliceCenters[s];
                    float along = Vector3.Dot(offset, sliceAxes[s]);
                    if (along < -slices[s].HalfHeight || along > slices[s].HalfHeight)
                    {
                        continue;
                    }

                    Vector3 radial = offset - sliceAxes[s] * along;
                    float distance = radial.magnitude;
                    float radius = slices[s].Radius(Vector3.Dot(radial, sliceSides[s]), Vector3.Dot(radial, sliceFronts[s]))
                                   + pointRadii[i] + Margin;
                    if (distance >= radius)
                    {
                        continue;
                    }

                    float depth = radius - distance;
                    if (depth <= worst)
                    {
                        continue;
                    }

                    worst = depth;
                    point = points[i];
                    outward = distance > 1e-4f ? radial / distance : sliceFronts[s];
                }
            }

            return worst;
        }
    }
}
