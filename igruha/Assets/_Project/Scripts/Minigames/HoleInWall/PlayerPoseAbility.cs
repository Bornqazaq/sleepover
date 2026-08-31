using System;
using UnityEngine;
using Igruha.Core.Player;

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
    /// <see cref="SetPose"/>. Ввод в него не попадает напрямую: он идёт через
    /// <see cref="RequestPose"/>, которое в фазе 3 станет
    /// <c>SetPoseServerRpc(byte)</c> с проверкой диапазона на сервере, а сама
    /// поза уедет в <c>NetworkVariable</c>. Логика ниже от этого не изменится.
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
        private PlayerController motor;
        private PlayerInputReader reader;
        private Transform silhouette;
        private Transform icon;
        private Renderer silhouetteRenderer;
        private Renderer iconRenderer;
        private float headHeight = 1.8f;

        /// <summary>Поза, в которой игрок стоит прямо сейчас. <see cref="HoleInWallPose.None"/> — ещё ни одной не нажал.</summary>
        public HoleInWallPose CurrentPose { get; private set; } = HoleInWallPose.None;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            reader = GetComponent<PlayerInputReader>();

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
            DestroyVisuals();
        }

        /// <summary>Выдать числа игры. Зовётся сразу после навешивания компонента.</summary>
        public void Configure(HoleInWallConfig gameConfig)
        {
            config = gameConfig;
            BuildVisuals();
            ApplyVisuals();
        }

        /// <summary>
        /// Намерение игрока встать в позу. В фазе 3 отсюда уйдёт
        /// <c>SetPoseServerRpc</c>: клиент шлёт номер, сервер проверяет диапазон
        /// и применяет. Пока сервера нет — применяем на месте.
        /// </summary>
        public void RequestPose(HoleInWallPose pose)
        {
            if (pose == HoleInWallPose.None || (int)pose > HoleInWallConfig.PoseCount)
            {
                return;
            }

            SetPose(pose);
        }

        /// <summary>
        /// Единственная точка, меняющая позу. В фазе 3 уйдёт за <c>IsServer</c>
        /// и станет записью в <c>NetworkVariable</c>.
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

            ApplyVisuals();
            PoseChanged?.Invoke(pose);
        }

        private void Update()
        {
            if (reader == null)
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
        /// Заглушка на время каркаса: плашка ростом с вырез вокруг персонажа и
        /// та же фигура иконкой над головой. Настоящие клипы поз — фаза 4
        /// (спека 9.5): добавлять состояния в восемь замороженных
        /// <c>.controller</c> на каркасе нельзя и не нужно.
        ///
        /// Габарит берётся из таблицы силуэтов, то есть ровно тот же, что
        /// у выреза на стене: игрок видит, войдёт он в дырку или нет, ещё
        /// на подъезде.
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

            Vector2 size = config.SilhouetteSize(CurrentPose);
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
