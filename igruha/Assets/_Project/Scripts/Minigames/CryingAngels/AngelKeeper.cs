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
        private KeeperBeam beam;
        private VisionCone vision;
        private GameObject rigInstance;
        private bool pushWasEnabled;

        /// <summary>Конус засветки Водящего. По нему в 14.5 считается попадание луча.</summary>
        public VisionCone Vision => vision;

        /// <summary>Точка, из которой светит фонарь — она же начало лучей проверки.</summary>
        public Transform BeamOrigin => rigInstance != null ? rigInstance.transform : transform;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            pushAbility = GetComponent<PlayerPushAbility>();
            capsule = GetComponent<CapsuleCollider>();
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

        /// <summary>Роль ушла: вернуть персонажу движение, уязвимость и удар.</summary>
        public void Detach()
        {
            motor.MovementLocked = false;
            motor.ImpulseImmune = false;

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

        /// <summary>Фонарь горит. Выключен на стартовом отсчёте и после конца раунда.</summary>
        public void SetBeamVisible(bool visible)
        {
            if (beam != null)
            {
                beam.SetVisible(visible);
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
