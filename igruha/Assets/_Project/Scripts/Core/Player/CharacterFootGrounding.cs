using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Держит ступни персонажа над полом: после того как аниматор поставил
    /// позу, модель приподнимается ровно на столько, на сколько ступня ушла
    /// под пол. Выше пола ступня — подъёма нет, поза не меняется вовсе.
    ///
    /// <b>Зачем.</b> Клипы Mixamo, пересаженные на восемь разных тел, в
    /// нескольких местах опускают таз ниже, чем позволяет рост персонажа.
    /// Замер 21.09 по всем клипам всех восьмерых: танцы уводят ступни под
    /// пол до 10 см (хуже всех <c>dance8</c>, <c>dance4</c>, <c>dance6</c>),
    /// ходьба в приседе — до 7 см у шестерых из восьми. Капсула при этом
    /// стоит на полу честно: проваливается только картинка.
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
        /// Больше этого не поднимаем никогда, м. Самый глубокий провал по
        /// замеру — 9.7 см (<c>dance8</c> у Boss); запас на склон под ступнёй.
        /// </summary>
        private const float MaxLift = 0.15f;

        /// <summary>Меньше этого — шум позы, а не провал. Иначе модель дрожит на миллиметры.</summary>
        private const float LiftDeadZone = 0.003f;

        private readonly Transform[] feet = new Transform[4];
        private int footCount;

        private Animator animator;
        private Transform visualRoot;
        private LayerMask groundLayers;
        private Vector3 baseLocalPosition;
        private float lift;

        /// <summary>На сколько модель приподнята в этом кадре, м. Ноль — ступни и так над полом.</summary>
        public float CurrentLift => lift;

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
            float next = needed < LiftDeadZone ? 0f : Mathf.Min(needed, MaxLift);
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
    }
}
