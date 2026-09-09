using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Player;
using Igruha.Core.Vision;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Водящий: стоит на постаменте и только крутится. Компонент вешается на
    /// аватар того, кому выпала роль, и снимается, когда роль ушла (в соло-тесте
    /// это кнопка пересдачи), поэтому он умеет возвращать персонажа в обычное
    /// состояние — иначе бывший Водящий остался бы обездвиженным навсегда.
    ///
    /// Ограничения роли собраны здесь, а не размазаны по контроллеру раунда:
    /// «что умеет Водящий» — свойство роли, а не свойство раунда.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class AngelKeeper : MonoBehaviour
    {
        [Tooltip("На сколько фонарь ниже макушки, м. От капсулы, а не абсолютом: у персонажей разный рост")]
        [SerializeField] private float beamDropFromTop = 0.15f;

        private PlayerController motor;
        private PlayerPushAbility pushAbility;
        private CapsuleCollider capsule;
        private Rigidbody body;
        private RigidbodyConstraints bodyConstraints;
        private KeeperBeam beam;
        private VisionCone vision;
        private GameObject rigInstance;
        private bool pushWasEnabled;
        private bool beamYawDriven;
        private float beamYaw;

        /// <summary>Конус засветки Водящего. По нему в 14.5 считается попадание луча.</summary>
        public VisionCone Vision => vision;

        /// <summary>Фонарь Водящего: визуал луча. Null, пока риг не навешен.</summary>
        public KeeperBeam Beam => beam;

        /// <summary>Точка, из которой светит фонарь — она же начало лучей проверки.</summary>
        public Transform BeamOrigin => rigInstance != null ? rigInstance.transform : transform;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            pushAbility = GetComponent<PlayerPushAbility>();
            capsule = GetComponent<CapsuleCollider>();
            body = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// Взять персонажа под роль Водящего: обездвижить, сделать неуязвимым
        /// для толчков и повесить фонарь.
        ///
        /// Иммунитет обязателен: без него Водящего сталкивают с постамента, и
        /// раунд ломается — он больше не в центре, а луч светит из угла зала.
        /// </summary>
        public void Attach(GameObject keeperRigPrefab)
        {
            motor.MovementLocked = true;
            motor.ImpulseImmune = true;

            // Блокировка и иммунитет закрывают ввод и удары, но не физику: чужая
            // капсула, вошедшая в Водящего на хосте, выдавливала его с постамента
            // депенетрацией. Горизонталь замораживаем на теле; вертикаль остаётся
            // гравитации, чтобы поставленный чуть выше пола Водящий на него сел.
            if (body != null)
            {
                bodyConstraints = body.constraints;
                body.constraints = bodyConstraints | RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
            }

            if (pushAbility != null)
            {
                pushWasEnabled = pushAbility.enabled;
                pushAbility.enabled = false;
            }

            if (rigInstance == null && keeperRigPrefab != null)
            {
                rigInstance = Instantiate(keeperRigPrefab, transform);
                rigInstance.transform.localPosition = new Vector3(0f, ResolveBeamHeight(), 0f);
                rigInstance.transform.localRotation = Quaternion.identity;
                beam = rigInstance.GetComponentInChildren<KeeperBeam>(true);
                vision = rigInstance.GetComponentInChildren<VisionCone>(true);
            }

            // Свет рисуем ровно по тем числам, по которым считается засветка:
            // иначе игрок прячется по картинке, а ловит его другой конус.
            if (beam != null && vision != null)
            {
                beam.Configure(vision.ConeAngle, vision.Range);
            }

            SetBeamVisible(false);
        }

        /// <summary>
        /// Высота фонаря считается от капсулы персонажа, а не задаётся числом:
        /// ростом персонажи различаются заметно (1.65–2.00), и фиксированная
        /// высота светила бы одному из глаз, другому из живота.
        /// </summary>
        private float ResolveBeamHeight()
        {
            if (capsule == null)
            {
                return 1.5f;
            }

            float top = capsule.center.y + capsule.height * 0.5f;
            return Mathf.Max(top - beamDropFromTop, capsule.radius);
        }

        /// <summary>
        /// Направление луча приходит извне и ставится МИРОВЫМ поворотом, а не
        /// наследуется от тела.
        ///
        /// Это и есть сетевой замок роли. Тело Водящего едет под авторитетом
        /// владельца (ClientNetworkTransform реплицирует и поворот), поэтому
        /// конус, висящий дочерним объектом, смотрел бы туда, куда развернулся
        /// клиент, — и серверный расчёт засветки честно считал бы по подделанному
        /// мгновенному развороту. Сняв луч с тела, мы оставляем клиенту его
        /// картинку, а направление, решающее исход, — серверу.
        ///
        /// Пока метод не позвали, риг ведёт себя как раньше: висит на теле с
        /// нулевым локальным поворотом. Соло-режим и болванка-Водящий этого
        /// не замечают.
        /// </summary>
        public void SetBeamYaw(float worldYaw)
        {
            beamYawDriven = true;
            beamYaw = worldYaw;
            ApplyBeamYaw();
        }

        /// <summary>
        /// Поворот тела приезжает по сети и правится физикой в течение всего
        /// кадра, поэтому мировой поворот рига надо переставлять после всех
        /// движений, а не один раз в момент получения значения.
        /// </summary>
        private void LateUpdate()
        {
            if (beamYawDriven)
            {
                ApplyBeamYaw();
            }
        }

        private void ApplyBeamYaw()
        {
            if (rigInstance != null)
            {
                rigInstance.transform.rotation = Quaternion.Euler(0f, beamYaw, 0f);
            }
        }

        /// <summary>Роль ушла: вернуть персонажу движение, уязвимость и удар.</summary>
        public void Detach()
        {
            motor.MovementLocked = false;
            motor.ImpulseImmune = false;
            beamYawDriven = false;

            if (body != null)
            {
                body.constraints = bodyConstraints;
            }

            if (pushAbility != null)
            {
                pushAbility.enabled = pushWasEnabled;
            }

            if (rigInstance != null)
            {
                Destroy(rigInstance);
                rigInstance = null;
                beam = null;
                vision = null;
            }
        }

        /// <summary>
        /// Риг принадлежит локальному игроку: спрятать линзу перед его камерой и
        /// включить его личную тьму. Раздача ролей идёт на каждой машине, так
        /// что у чужих Водящих метод получает false.
        /// </summary>
        public void SetLocalView(bool ownedLocally)
        {
            if (rigInstance != null && rigInstance.TryGetComponent(out KeeperLocalView view))
            {
                view.Apply(ownedLocally);
            }
        }

        /// <summary>Фонарь горит. Выключен на стартовом отсчёте и после конца раунда.</summary>
        public void SetBeamVisible(bool visible)
        {
            if (beam != null)
            {
                beam.SetVisible(visible);
            }
        }

        /// <summary>Цвет луча — обратная связь по счётчику окаменения самой близкой к нему цели.</summary>
        public void SetBeamColor(Color color)
        {
            if (beam != null)
            {
                beam.SetColor(color);
            }
        }

        /// <summary>Потолок скорости поворота — его задаёт мини-игра по числу Бегущих.</summary>
        public void ApplyTurnSpeed(FirstPersonCameraRig rig, float degreesPerSecond)
        {
            if (rig != null)
            {
                rig.SetMaxTurnSpeed(degreesPerSecond);
            }
        }

        private void OnDestroy()
        {
            if (rigInstance != null)
            {
                Destroy(rigInstance);
            }
        }
    }
}
