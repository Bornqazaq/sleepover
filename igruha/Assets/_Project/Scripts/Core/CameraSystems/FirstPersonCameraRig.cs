using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using Igruha.Core.Player;

namespace Igruha.Core.CameraSystems
{
    /// <summary>
    /// Обзор от первого лица с потолком угловой скорости. Мышь и стик задают
    /// желаемое направление, а камера доезжает до него с постоянной скоростью
    /// maxTurnSpeed — это и есть главный рычаг баланса ведущего в асимметричных
    /// играх: он физически не может развернуться мгновенно и переловить всех.
    ///
    /// Процедурных компонентов Cinemachine на риге нет: позицию и поворот пишет
    /// этот скрипт в Update, а мозг камеры читает их в LateUpdate — порядок
    /// гарантирован, ничего не перетирается.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class FirstPersonCameraRig : MonoBehaviour
    {
        [SerializeField] private InputActionReference lookAction;

        [Header("Чувствительность")]
        [Tooltip("Мышь: градусов на пиксель смещения")]
        [SerializeField] private float mouseYawSensitivity = 0.16f;
        [SerializeField] private float mousePitchSensitivity = 0.11f;
        [Tooltip("Стик: градусов в секунду при полном отклонении")]
        [SerializeField] private float stickYawSpeed = 200f;
        [SerializeField] private float stickPitchSpeed = 130f;
        [SerializeField] private bool invertPitch;

        [Header("Ограничение обзора")]
        [Tooltip("Потолок угловой скорости поворота по горизонтали, °/с")]
        [SerializeField] private float maxTurnSpeed = 90f;
        [Tooltip("Насколько низко можно опустить взгляд, °")]
        [SerializeField] private float minPitch = -20f;
        [Tooltip("Насколько высоко можно поднять взгляд, °")]
        [SerializeField] private float maxPitch = 20f;

        [Header("Тело и глаз")]
        [Tooltip("Разворачивать тело персонажа вслед за обзором")]
        [SerializeField] private bool lockBodyRotation = true;
        [Tooltip("Запасная высота глаза, м. Берётся только если у персонажа нет капсулы")]
        [SerializeField] private float eyeHeight = 1.5f;
        [Tooltip("На сколько глаз ниже макушки капсулы, м. Персонажи разного роста получают свою высоту")]
        [SerializeField] private float eyeDropFromTop = 0.15f;
        [Tooltip("Прятать собственную модель: иначе камера стоит внутри головы и видно изнанку текстур")]
        [SerializeField] private bool hideOwnModel = true;
        [Tooltip("Сдвиг камеры от глаза, м: X вбок, Y вверх (минус — вниз), Z назад. Ноль — камера ровно в глазу. Опускание нужно роли с оружием в руках: от макушки собственные кисти на полсотни градусов ниже оси взгляда и в кадр не попадают ни при каком поле зрения")]
        [SerializeField] private Vector3 shoulderOffset;
        [Tooltip("Поле зрения, °. Вместе с потолком скорости снижает риск укачивания")]
        [Range(40f, 110f)]
        [SerializeField] private float fieldOfView = 75f;

        [Header("Курсор")]
        [Tooltip("Захватывать курсор в центре экрана (Esc освобождает)")]
        [SerializeField] private bool lockCursor = true;

        /// <summary>
        /// Где на самом деле глаз — без отвода камеры назад. По нему считается
        /// выстрел: отодвинув камеру, луч пришлось бы пускать сквозь собственное
        /// тело, и первым же попаданием стала бы своя капсула.
        /// </summary>
        public Vector3 EyePosition => trackedTarget != null
            ? trackedTarget.position + Vector3.up * resolvedEyeHeight
            : transform.position;

        /// <summary>Куда камера смотрит сейчас — уже с учётом потолка скорости.</summary>
        public float Yaw => yaw;

        /// <summary>Наклон взгляда. Чисто визуальный: на игровые проверки не влияет.</summary>
        public float Pitch => pitch;

        /// <summary>
        /// Куда игрок просит смотреть — до потолка скорости. Именно это значение
        /// уходит на сервер в асимметричных играх, где направление взгляда решает
        /// исход: подрезанный <see cref="Yaw"/> слать нельзя, иначе клэмп считается
        /// дважды (у клиента и у сервера) и разворот отстаёт вдвое.
        /// </summary>
        public float DesiredYaw => desiredYaw;

        private CinemachineCamera cam;
        private Transform trackedTarget;
        private PlayerController trackedBody;
        private float yaw;
        private float desiredYaw;
        private float pitch;
        private bool viewDrivenExternally;
        private float resolvedEyeHeight;
        private float horizontalFieldOfView;
        private float appliedAspect;
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>(8);

        /// <summary>
        /// Голова того, чьими глазами смотрим, и её исходный масштаб. Прячется
        /// отдельно от остального тела: камера стоит внутри черепа, и в кадр
        /// лезет его изнанка — ровно та причина, по которой модель раньше
        /// гасили целиком. Схлопнутая кость уносит с собой и всё, что на ней
        /// висит: волосы, уши, шапку.
        /// </summary>
        private Transform headBone;
        private Vector3 headBoneScale = Vector3.one;

        private void Awake()
        {
            EnsureCamera();
        }

        /// <summary>
        /// Достать камеру, не дожидаясь Awake. Настройки рига задаёт мини-игра
        /// при раздаче ролей, а риг в этот момент ещё выключен — Awake у него
        /// не отработал, и ссылка пустая. Компонент лежит на том же объекте,
        /// так что это разовый GetComponent, а не поиск по сцене.
        /// </summary>
        private bool EnsureCamera()
        {
            if (cam == null)
            {
                cam = GetComponent<CinemachineCamera>();
            }

            return cam != null;
        }

        private void OnEnable()
        {
            lookAction?.action.Enable();

            LensSettings lens = cam.Lens;
            lens.FieldOfView = fieldOfView;
            cam.Lens = lens;

            // Обзор по горизонтали, если игра его задала, обязан пересчитаться
            // заново: строкой выше поле зрения только что вернули к вертикальному.
            appliedAspect = 0f;
            ApplyHorizontalFieldOfView();

            // Риг включается уже наведённым на своего персонажа: подхватываем его
            // разворот, иначе камера стартует со случайного направления.
            ResolveTarget();
            if (trackedTarget != null)
            {
                yaw = desiredYaw = trackedTarget.eulerAngles.y;
            }

            pitch = 0f;
            ApplyTransform();

            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void OnDisable()
        {
            lookAction?.action.Disable();
            RestoreOwnModel();
            RestoreHeadBone();

            // Сбрасываем цель, иначе при следующем включении ResolveTarget решит,
            // что она не менялась, и модель останется видимой.
            trackedTarget = null;

            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>
        /// Потолок угловой скорости под правила конкретной мини-игры: в
        /// асимметричных играх он зависит от числа убегающих и потому не может
        /// быть зашит в риг. Значение ≤ 0 игнорируется — риг остаётся на своём.
        /// </summary>
        public void SetMaxTurnSpeed(float degreesPerSecond)
        {
            if (degreesPerSecond > 0f)
            {
                maxTurnSpeed = degreesPerSecond;
            }
        }

        /// <summary>
        /// Поставить обзор напрямую, минуя ввод и потолок скорости. Через это
        /// в сетевой фазе серверное значение yaw затирает локальное.
        /// </summary>
        public void SetYaw(float value)
        {
            yaw = desiredYaw = value;
            ApplyTransform();
        }

        /// <summary>
        /// Обзор задаётся снаружи, а не мышью этой машины. Так наблюдатель
        /// смотрит чужими глазами: свой ввод в этом режиме молчит, иначе он
        /// уводил бы кадр с чужого прицела, а доводка до желаемого угла
        /// тянула бы камеру обратно к последнему локальному направлению.
        ///
        /// Тело в этом режиме не поворачиваем: оно чужое и живёт под
        /// NetworkTransform.
        /// </summary>
        public void SetViewDrivenExternally(bool driven) => viewDrivenExternally = driven;

        /// <summary>
        /// Поставить обзор целиком — и азимут, и наклон. Для трансляции
        /// чужого взгляда: у наблюдаемого важны оба угла, одного yaw мало.
        /// </summary>
        public void SetView(float newYaw, float newPitch)
        {
            yaw = desiredYaw = newYaw;
            pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
            ApplyTransform();
        }

        /// <summary>
        /// Пределы наклона взгляда под конкретную игру. Стандартные ±20° годятся
        /// для ровной арены, но роль, которая обязана простреливать башню от
        /// первого этажа до крыши, ими не обходится.
        /// </summary>
        public void SetPitchLimits(float min, float max)
        {
            minPitch = Mathf.Min(min, max);
            maxPitch = Mathf.Max(min, max);
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        /// <summary>
        /// Держать заданный обзор по ГОРИЗОНТАЛИ, °, независимо от соотношения
        /// сторон экрана. Cinemachine задаёт поле зрения по вертикали, поэтому
        /// на ультрашироком мониторе горизонтальный обзор молча вырастает —
        /// а там, где ширина кадра и есть баланс роли (сколько арены видно
        /// разом), это отдаёт игроку с широким экраном чужое преимущество.
        ///
        /// Ноль возвращает риг к обычному вертикальному полю зрения.
        /// </summary>
        public void SetHorizontalFieldOfView(float degrees)
        {
            horizontalFieldOfView = Mathf.Max(0f, degrees);
            appliedAspect = 0f;
            ApplyHorizontalFieldOfView();
        }

        /// <summary>
        /// Пересчёт вертикального поля зрения под текущее соотношение сторон.
        /// Аспект меняется при смене размера окна, поэтому проверяется каждый
        /// кадр, а сама тригонометрия считается только когда он реально поехал.
        /// </summary>
        private void ApplyHorizontalFieldOfView()
        {
            if (horizontalFieldOfView <= 0f || !EnsureCamera())
            {
                return;
            }

            float aspect = cam.Lens.Aspect;
            if (aspect <= 0f || Mathf.Approximately(aspect, appliedAspect))
            {
                return;
            }

            appliedAspect = aspect;

            float halfHorizontal = horizontalFieldOfView * 0.5f * Mathf.Deg2Rad;
            float vertical = 2f * Mathf.Atan(Mathf.Tan(halfHorizontal) / aspect) * Mathf.Rad2Deg;

            LensSettings lens = cam.Lens;
            lens.FieldOfView = vertical;
            cam.Lens = lens;
        }

        private void Update()
        {
            ResolveTarget();
            ApplyHorizontalFieldOfView();

            // Углы придут снаружи — своим вводом их не трогаем, тело чужое.
            if (viewDrivenExternally)
            {
                ApplyTransform();
                return;
            }

            ReadLook();

            // Желаемое направление копится без ограничений, а камера доезжает до него
            // с постоянной скоростью. Мгновенный рывок мышью превращается в доводку,
            // и разворот на 180° честно занимает своё время.
            yaw = Mathf.MoveTowardsAngle(yaw, desiredYaw, maxTurnSpeed * Time.deltaTime);
            ApplyTransform();

            if (lockBodyRotation && trackedBody != null)
            {
                trackedBody.SetFacing(yaw);
            }
        }

        private void ReadLook()
        {
            if (lookAction == null)
            {
                return;
            }

            Vector2 look = lookAction.action.ReadValue<Vector2>();
            if (look.sqrMagnitude <= 0f)
            {
                return;
            }

            // Мышь даёт смещение в пикселях за кадр, стик — отклонение -1..1:
            // первое нельзя умножать на deltaTime, второе — обязательно.
            float yawDelta, pitchDelta;
            if (IsMouseLook())
            {
                yawDelta = look.x * mouseYawSensitivity;
                pitchDelta = look.y * mousePitchSensitivity;
            }
            else
            {
                yawDelta = look.x * stickYawSpeed * Time.deltaTime;
                pitchDelta = look.y * stickPitchSpeed * Time.deltaTime;
            }

            if (!invertPitch)
            {
                pitchDelta = -pitchDelta;
            }

            desiredYaw = Mathf.Repeat(desiredYaw + yawDelta + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitch + pitchDelta, minPitch, maxPitch);
        }

        /// <summary>
        /// Отвести камеру назад от глаза. Ноль — честное первое лицо, и так
        /// живут все остальные роли проекта. Больше нуля просит роль с оружием
        /// в руках: из глаза собственные кисти не видны — они в двадцати
        /// сантиметрах от камеры и на полметра ниже, то есть вне пирамиды
        /// обзора при любом разумном FOV. На выстрел это не влияет, он
        /// по-прежнему считается от <see cref="EyePosition"/>.
        /// </summary>
        public void SetShoulderOffset(Vector3 offset)
        {
            shoulderOffset = offset;
            ApplyHeadVisibility();
            ApplyTransform();
        }

        /// <summary>Камера отведена от глаза — обзор перестал быть строго от первого лица.</summary>
        public bool IsShoulderView => shoulderOffset.sqrMagnitude > 0f;

        private void ApplyTransform()
        {
            if (trackedTarget != null)
            {
                transform.position = trackedTarget.position + Vector3.up * resolvedEyeHeight;
            }

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            if (!IsShoulderView)
            {
                return;
            }

            // Вбок и назад считаются от взгляда, а не от осей мира, иначе камера
            // уезжает не туда на любом повороте. А вот высота — строго по мировой
            // вертикали: «опустить глаз» обязано значить одну и ту же высоту и
            // когда Охотник смотрит прямо, и когда задирает ствол на крышу.
            transform.position += Vector3.up * shoulderOffset.y
                                + transform.right * shoulderOffset.x
                                - transform.forward * shoulderOffset.z;
        }

        /// <summary>
        /// Цель ставит MinigameCameraController, а в сетевой сцене переставляет
        /// LocalPlayerCameraBinder после загрузки. GetComponent дёргается только
        /// в момент смены цели, а не каждый кадр.
        /// </summary>
        private void ResolveTarget()
        {
            Transform target = cam.Target.TrackingTarget;
            if (target == trackedTarget)
            {
                return;
            }

            RestoreOwnModel();
            RestoreHeadBone();

            trackedTarget = target;
            trackedBody = target != null ? target.GetComponent<PlayerController>() : null;
            resolvedEyeHeight = ResolveEyeHeight(target);
            HideOwnModel(target);

            ApplyHeadVisibility();
        }

        /// <summary>
        /// Высота глаза считается от капсулы персонажа, а не задаётся числом:
        /// точка персонажа лежит на уровне ступней, а ростом персонажи
        /// различаются заметно. Фиксированная высота ставила бы камеру одному
        /// в глаза, другому в живот.
        /// </summary>
        private float ResolveEyeHeight(Transform target)
        {
            if (target == null || !target.TryGetComponent(out CapsuleCollider capsule))
            {
                return eyeHeight;
            }

            float top = capsule.center.y + capsule.height * 0.5f;
            return Mathf.Max(top - eyeDropFromTop, capsule.radius);
        }

        /// <summary>
        /// Показывать ли собственную модель от первого лица.
        ///
        /// По умолчанию она спрятана: камера стоит внутри головы, и в кадр
        /// лезет изнанка текстур. Но роли, у которой в руках оружие, модель
        /// нужна — без неё не видно ни рук, ни ствола, и вся анимация выстрела
        /// играет мимо игрока. Просит её сама роль, на время роли: остальным
        /// поведение рига не меняется.
        ///
        /// Применяется сразу, а не с ближайшей сменой цели: роль выдаётся
        /// посреди раунда, когда камера уже смотрит куда надо.
        /// </summary>
        public void SetOwnModelVisible(bool visible)
        {
            // Повтор того же требования пропускаем, и это не оптимизация.
            // HideOwnModel пропускает уже погашенные рендереры, вычёркивая их
            // из списка спрятанных, — значит второй «спрятать» подряд оставляет
            // список пустым, и следующий «показать» не возвращает ничего.
            // Роль выдаётся и снимается по нескольку раз за раунд (отсчёт,
            // пересдача), так что в это упираешься сразу.
            if (hideOwnModel == !visible)
            {
                return;
            }

            hideOwnModel = !visible;

            if (visible)
            {
                RestoreOwnModel();
                ApplyHeadVisibility();
                return;
            }

            RestoreHeadBone();
            HideOwnModel(trackedTarget);
        }

        /// <summary>
        /// Голову прячем, когда тело видно, а камера не отведена назад. Опущенная
        /// или сдвинутая вбок камера всё ещё стоит внутри персонажа, и голова лезет
        /// в кадр сверху. А вот отодвинутой назад камере схлопнутая голова только
        /// вредит: вместо затылка игрок видит культю шеи.
        /// </summary>
        private void ApplyHeadVisibility()
        {
            if (!hideOwnModel && shoulderOffset.z <= 0f)
            {
                HideHeadBone(trackedTarget);
                return;
            }

            RestoreHeadBone();
        }

        /// <summary>
        /// Схлопнуть кость головы. Способ грубый, но единственный доступный:
        /// тело персонажа — один скиннед-меш, отдельного рендерера у головы нет,
        /// и погасить её иначе нечем. Отодвигать камеру вперёд, за лицо, хуже:
        /// точка выстрела уезжает от глаза, а сама камера начинает протыкать
        /// стены там, где тело ещё не касается их.
        /// </summary>
        private void HideHeadBone(Transform target)
        {
            RestoreHeadBone();

            if (target == null)
            {
                return;
            }

            Animator body = target.GetComponentInChildren<Animator>();
            if (body == null || !body.isHuman)
            {
                return;
            }

            Transform head = body.GetBoneTransform(HumanBodyBones.Head);
            if (head == null)
            {
                return;
            }

            headBone = head;
            headBoneScale = head.localScale;
            head.localScale = Vector3.zero;
        }

        private void RestoreHeadBone()
        {
            if (headBone == null)
            {
                return;
            }

            headBone.localScale = headBoneScale;
            headBone = null;
        }

        /// <summary>
        /// Спрятать модель того, чьими глазами смотрим. Камера стоит внутри
        /// головы, и без этого в кадр лезет изнанка собственных текстур.
        /// Прячем только у себя: остальные игроки видят это тело как обычно.
        /// </summary>
        private void HideOwnModel(Transform target)
        {
            if (!hideOwnModel || target == null)
            {
                return;
            }

            target.GetComponentsInChildren(true, hiddenRenderers);
            for (int i = hiddenRenderers.Count - 1; i >= 0; i--)
            {
                Renderer renderer = hiddenRenderers[i];

                // Выключенное не нашей рукой возвращать потом нельзя, а
                // помеченное как «оставить видимым» гасить нельзя вовсе:
                // так на персонаже живёт фонарь ведущего, который ему и нужен.
                if (!renderer.enabled || renderer.GetComponentInParent<KeepVisibleInFirstPerson>() != null)
                {
                    hiddenRenderers.RemoveAt(i);
                    continue;
                }

                renderer.enabled = false;
            }
        }

        private void RestoreOwnModel()
        {
            for (int i = 0; i < hiddenRenderers.Count; i++)
            {
                if (hiddenRenderers[i] != null)
                {
                    hiddenRenderers[i].enabled = true;
                }
            }

            hiddenRenderers.Clear();
        }

        private bool IsMouseLook()
        {
            InputControl control = lookAction.action.activeControl;
            return control == null || control.device is Mouse;
        }
    }
}
