using System;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Четыре позы игрока: клавиши 1–4, смена мгновенная, ходить в позе можно
    /// (спека, раздел 4). Единственное действие игрока в этой игре и
    /// единственное, что решает исход.
    ///
    /// Вешается контроллером на аватар в начале раунда и снимается в конце —
    /// префабы персонажей замороженные, и добавлять в них компонент нельзя
    /// (igruha/CLAUDE.md, раздел 🔒 0).
    /// </summary>
    /// <remarks>
    /// <b>Поза защёлкивается, а не держится клавишей.</b> Нажал — стоишь в позе,
    /// пока не нажал другую. Иначе подстраиваться под вырез пришлось бы с
    /// зажатой цифрой и WASD одновременно, а спека прямо разрешает
    /// перемещаться в позе до последнего кадра.
    ///
    /// <b>Сеть.</b> Важное состояние меняет ровно один метод —
    /// <see cref="SetPose"/>, — и зовёт его не ввод, а мини-игра: ввод уходит
    /// намерением в <see cref="HoleInWallMinigame.SubmitPoseIntent"/>, сервер
    /// проверяет диапазон и рассылает подтверждённую позу. Способность
    /// компонентом <c>NetworkBehaviour</c> быть не может — её навешивают
    /// на уже заспавненный аватар в начале раунда, а <c>NetworkObject</c>
    /// набирает свои <c>NetworkBehaviour</c> при спавне и позже не добирает.
    /// Отсюда и маршрут через мини-игру, у которой сетевая половина есть.
    /// </remarks>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerPoseAbility : MonoBehaviour
    {
        /// <summary>Толщина плашки-силуэта, м. Она стоит вокруг персонажа и не должна его загораживать.</summary>
        private const float SilhouetteThickness = 0.06f;

        /// <summary>Прозрачность силуэта: сквозь него обязан быть виден сам персонаж.</summary>
        private const float SilhouetteAlpha = 0.35f;

        /// <summary>На сколько метров иконка висит над макушкой.</summary>
        private const float IconLift = 0.45f;

        /// <summary>Высота иконки над головой, м. Ширина считается по пропорции силуэта.</summary>
        private const float IconHeight = 0.35f;

        /// <summary>
        /// Имя слоя поз в Animator Controller. Слой строит
        /// <c>Editor/HoleInWallPoseLayerBuilder</c>; строка продублирована здесь,
        /// потому что Editor-сборка недоступна из рантайма.
        /// </summary>
        private const string PoseLayerName = "Pose";

        /// <summary>Вес слоя поз, когда игрок в позе. Слой перекрывает основной целиком: поза — это весь силуэт, а не только руки.</summary>
        private const float PoseLayerWeight = 1f;

        /// <summary>Номер позы для Animator. Тот же параметр, что заводит билдер слоя.</summary>
        private static readonly int PoseParameterHash = Animator.StringToHash("Pose");

        /// <summary>Слоя поз в контроллере нет — у этого персонажа его просто не собрали.</summary>
        private const int NoLayer = -1;

        /// <summary>Цвета поз. Порядок — позы 1…4; на каркасе это единственное, чем они различаются на вид.</summary>
        private static readonly Color[] PoseColors =
        {
            new Color(0.20f, 0.75f, 1.00f),
            new Color(1.00f, 0.78f, 0.15f),
            new Color(0.35f, 0.90f, 0.40f),
            new Color(1.00f, 0.40f, 0.65f)
        };

        /// <summary>Поза сменилась. Визуалу, звуку и строке статуса.</summary>
        public event Action<HoleInWallPose> PoseChanged;

        private HoleInWallConfig config;

        /// <summary>
        /// Формы вырезов <b>этого</b> персонажа, а не дорожки. Рамка отвечает
        /// на вопрос «сколько места я занимаю»: у Карлана ростом 1.61 м и
        /// у Шланги ростом 1.96 м ответы разные, и усреднять их незачем.
        /// </summary>
        private CutoutShapes ownShapes;
        private HoleInWallMinigame owner;
        private int playerId = -1;
        private PlayerController motor;
        private PlayerInputReader reader;

        /// <summary>
        /// Сетевой объект аватара. По нему видно, ведёт ли этого персонажа
        /// сама эта машина: чужую копию нельзя ни двигать, ни читать за неё ввод.
        /// </summary>
        private NetworkObject body;
        private Transform silhouette;
        private Transform icon;
        private Renderer silhouetteRenderer;
        private Renderer iconRenderer;
        private float headHeight = 1.8f;

        /// <summary>
        /// Аниматор модели. Позу отыгрывает он, а не плашка: настоящие клипы поз
        /// приехали в арт-фазе (спека 9.5).
        /// </summary>
        private Animator animator;

        /// <summary>Индекс слоя поз. Ищется один раз: поиск идёт по строке.</summary>
        private int poseLayer = NoLayer;

        /// <summary>Поза, в которой игрок стоит прямо сейчас. <see cref="HoleInWallPose.None"/> — ещё ни одной не нажал.</summary>
        public HoleInWallPose CurrentPose { get; private set; } = HoleInWallPose.None;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            reader = GetComponent<PlayerInputReader>();
            body = GetComponent<NetworkObject>();

            // Аниматор живёт на модели — она ребёнок аватара, а не он сам.
            animator = GetComponentInChildren<Animator>(true);
            poseLayer = animator != null ? animator.GetLayerIndex(PoseLayerName) : NoLayer;

            if (TryGetComponent(out CapsuleCollider capsule))
            {
                headHeight = capsule.center.y + capsule.height * 0.5f;
            }
        }

        private void OnEnable()
        {
            // Ctrl-присед выключен на всё время раунда: иначе в позу 3 ведут
            // два разных пути и «какая сейчас поза» получает два источника
            // правды (спека, раздел 4).
            motor.CrouchInputSuppressed = true;
        }

        private void OnDisable()
        {
            // ⚠️ Снимаем всё, что навесили: персонаж переезжает в хаб живым
            // NetworkObject, и незакрытая роль уедет вместе с ним. У «Ангелов»
            // так уехала блокировка движения Водящего.
            motor.CrouchInputSuppressed = false;
            motor.SetCrouched(false);
            motor.ForceStand();

            CurrentPose = HoleInWallPose.None;
            ApplyPoseToAnimator();
            DestroyVisuals();
        }

        /// <summary>
        /// Выдать числа игры и того, кому уходят намерения. Зовётся сразу после
        /// навешивания компонента.
        /// </summary>
        public void Configure(HoleInWallConfig gameConfig, HoleInWallMinigame game, int id)
        {
            config = gameConfig;
            ownShapes = new CutoutShapes(gameConfig, CutoutShapes.KeyOf(gameObject));
            owner = game;
            playerId = id;
            BuildVisuals();
            ApplyVisuals();
        }

        /// <summary>
        /// Намерение игрока встать в позу. Само по себе оно ничего не меняет:
        /// решение принимает мини-игра, а в сетевой катке — её сервер.
        /// </summary>
        public void RequestPose(HoleInWallPose pose)
        {
            if (pose == HoleInWallPose.None || (int)pose > HoleInWallConfig.PoseCount)
            {
                return;
            }

            // Клавиша зажата, а не нажата: ридер отдаёт номер каждый кадр, пока
            // её держат. Без этой проверки каждая зажатая цифра давала бы
            // шестьдесят пакетов в секунду вместо одного на смену позы.
            if (CurrentPose == pose)
            {
                return;
            }

            if (owner == null)
            {
                // Способность живёт без мини-игры только в редакторских
                // проверках самой способности. Играть в это нельзя, но и падать
                // незачем.
                SetPose(pose);
                return;
            }

            owner.SubmitPoseIntent(playerId, pose);
        }

        /// <summary>
        /// Единственная точка, меняющая позу. В сетевой катке её зовёт только
        /// мини-игра — с подтверждённым сервером значением.
        /// </summary>
        public void SetPose(HoleInWallPose pose)
        {
            if (CurrentPose == pose)
            {
                return;
            }

            CurrentPose = pose;

            // Присед — единственная поза, у которой уже есть настоящий клип и
            // сжатие капсулы. Своего приседа не заводим: спека 8.3 считает
            // высоту выреза именно по нему (абсолютные 0.8 м).
            motor.SetCrouched(pose == HoleInWallPose.Crouch);

            ApplyPoseToAnimator();
            ApplyVisuals();
            PoseChanged?.Invoke(pose);
        }

        /// <summary>
        /// Отдать позу аниматору: номер в параметр, вес — слою.
        ///
        /// Зовётся на КАЖДОЙ машине, а не только у владельца: <see cref="SetPose"/>
        /// приходит всем подтверждённым с сервера, а <c>CharacterAnimatorDriver</c>
        /// у чужих копий выключен и позу за нас не покажет.
        /// </summary>
        private void ApplyPoseToAnimator()
        {
            if (animator == null || poseLayer == NoLayer)
            {
                return;
            }

            animator.SetInteger(PoseParameterHash, (int)CurrentPose);
            animator.SetLayerWeight(poseLayer, PoseLayerVisible ? PoseLayerWeight : 0f);
        }

        /// <summary>
        /// Показывать ли позу прямо сейчас. Сбитого с ног показываем лежащим:
        /// провал в этой игре парный, и партнёр обязан видеть, что случилось,
        /// а не читать позу на теле, которое уже летит в воду.
        /// </summary>
        private bool PoseLayerVisible => CurrentPose != HoleInWallPose.None && !motor.IsKnockedDown;

        private void Update()
        {
            // Нокдаун начинается и кончается не по нашему событию, поэтому вес
            // слоя сверяется каждый кадр. SetLayerWeight с тем же значением
            // ничего не стоит и не аллоцирует.
            ApplyPoseToAnimator();


            // 🔴 Ввод читается только у персонажа, которого ведёт эта машина.
            // У чужой копии ридер отобран, но погонщик болванок умеет подать
            // в него значение напрямую — и тогда эта копия попросила бы позу,
            // а сервер, берущий отправителя из пакета, поставил бы её НАМ.
            // То есть болванка играла бы за живого человека его единственным
            // действием.
            if (reader == null || !WorldAuthority.DrivenHere(body))
            {
                return;
            }

            int requested = reader.PoseRequest;
            if (requested > 0)
            {
                RequestPose((HoleInWallPose)requested);
            }
        }

        // ========== ВИЗУАЛ КАРКАСА ==========

        /// <summary>
        /// Габаритная рамка вокруг персонажа и та же фигура иконкой над головой.
        /// Саму позу с арт-фазы отыгрывает клип на слое <c>Pose</c>, а рамка
        /// осталась подсказкой: она ровно того же размера, что вырез на стене,
        /// и отвечает на вопрос «влезу ли», пока стена ещё далеко.
        ///
        /// Держится полупрозрачной намеренно: сквозь неё обязана быть видна
        /// поза, иначе рамка съедает то, ради чего фаза 4 и делалась.
        /// </summary>
        private void BuildVisuals()
        {
            silhouette = CreateBox("PoseSilhouette", out silhouetteRenderer);
            icon = CreateBox("PoseIcon", out iconRenderer);
        }

        private Transform CreateBox(string boxName, out Renderer boxRenderer)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = boxName;
            box.transform.SetParent(transform, false);

            // Коллайдер снимаем сразу: силуэт — это картинка, а не тело.
            // Оставленный, он ловил бы удары, толчки и лучи камеры.
            Collider blocker = box.GetComponent<Collider>();
            if (blocker != null)
            {
                Destroy(blocker);
            }

            boxRenderer = box.GetComponent<Renderer>();
            boxRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            boxRenderer.sharedMaterial = HoleInWallMaterials.Transparent(Color.white);

            return box.transform;
        }

        private void ApplyVisuals()
        {
            if (config == null || silhouette == null || icon == null)
            {
                return;
            }

            bool posed = CurrentPose != HoleInWallPose.None;
            silhouette.gameObject.SetActive(posed);
            icon.gameObject.SetActive(posed);

            if (!posed)
            {
                return;
            }

            Vector2 size = ownShapes.Size(CurrentPose);
            Color color = PoseColors[Mathf.Clamp((int)CurrentPose - 1, 0, PoseColors.Length - 1)];

            silhouette.localScale = new Vector3(size.x, size.y, SilhouetteThickness);
            silhouette.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            silhouetteRenderer.sharedMaterial =
                HoleInWallMaterials.Transparent(new Color(color.r, color.g, color.b, SilhouetteAlpha));

            float aspect = size.y > 0.01f ? size.x / size.y : 1f;
            icon.localScale = new Vector3(IconHeight * aspect, IconHeight, SilhouetteThickness);
            icon.localPosition = new Vector3(0f, headHeight + IconLift, 0f);
            iconRenderer.sharedMaterial = HoleInWallMaterials.Opaque(color);
        }

        private void DestroyVisuals()
        {
            if (silhouette != null)
            {
                Destroy(silhouette.gameObject);
                silhouette = null;
            }

            if (icon != null)
            {
                Destroy(icon.gameObject);
                icon = null;
            }
        }
    }
}
