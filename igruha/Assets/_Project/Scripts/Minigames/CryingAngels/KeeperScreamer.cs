using System;
using System.Collections;
using Igruha.Core.CameraSystems;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Скример касания: Бегущий дотянулся до Водящего — и на долю секунды
    /// вылетает ему в лицо из темноты. Фундамент момента: призрак тела с
    /// ударом, рывок к камере, вспышка, дрожь взгляда и вспышка фонаря.
    /// Звук цепляется за <see cref="Started"/> позже.
    ///
    /// Коротко намеренно: раунд не останавливается, остальные Бегущие
    /// продолжают идти к постаменту, и Водящий обязан вернуться к лучу
    /// меньше чем через секунду.
    ///
    /// Призрак — копия модели дошедшего, а не сам аватар: аватар едет под
    /// NetworkTransform и снимается с арены в тот же тик. Появляется он там,
    /// куда Водящий смотрит, а не там, где реально стоял игрок: пугает лицо
    /// из темноты перед глазами, а не поворот камеры, который к тому же
    /// потащил бы за собой серверный луч. Азимут взгляда не трогается —
    /// дрожит только наклон, он на засветку не влияет.
    /// </summary>
    public sealed class KeeperScreamer : MonoBehaviour
    {
        private static readonly int PunchParameterHash = Animator.StringToHash("Punch");

        [Header("Тайминг")]
        [Tooltip("Сколько живёт призрак и вся сцена, с")]
        [SerializeField] private float duration = 0.85f;
        [Tooltip("Расстояние призрака от глаз в начале рывка, м")]
        [SerializeField] private float lungeFrom = 1.7f;
        [Tooltip("Расстояние призрака от глаз в конце рывка, м")]
        [SerializeField] private float lungeTo = 0.8f;
        [Tooltip("Длительность рывка, с")]
        [SerializeField] private float lungeTime = 0.22f;

        [Header("Вспышка на экране")]
        [SerializeField] private Color flashColor = new Color(1f, 0.93f, 0.8f, 1f);
        [Tooltip("Непрозрачность вспышки у Водящего")]
        [Range(0f, 1f)]
        [SerializeField] private float keeperFlashAlpha = 1f;
        [Tooltip("Непрозрачность вспышки у остальных: они видят только всполох фонаря")]
        [Range(0f, 1f)]
        [SerializeField] private float othersFlashAlpha = 0.35f;
        [SerializeField] private float flashIn = 0.05f;
        [SerializeField] private float flashOut = 0.45f;
        [Tooltip("Канвас, на который кладётся вспышка. Заполняет билдер арта")]
        [SerializeField] private Canvas overlayCanvas;

        [Header("Фонарь и камера")]
        [Tooltip("Фонарь в момент касания гаснет до этой доли яркости: в упор он выжигал призрака в белое пятно, а погасший фонарь страшнее")]
        [Range(0f, 1f)]
        [SerializeField] private float torchDim = 0.03f;
        [Tooltip("Свой свет призрака: тёплый, снизу, как фонарик под подбородком")]
        [SerializeField] private Color ghostLightColor = new Color(1f, 0.82f, 0.62f);
        [SerializeField] private float ghostLightIntensity = 1.4f;
        [SerializeField] private float ghostLightRange = 2.5f;
        [Tooltip("Где стоит лампа относительно глаз Водящего: вперёд к призраку и вниз, м")]
        [SerializeField] private float lampForward = 0.5f;
        [SerializeField] private float lampDrop = 0.55f;
        [Tooltip("Поправка разворота модели: у персонажей визуальное «лицо» не совпадает с forward корня")]
        [SerializeField] private float facingOffset = 0f;
        [Tooltip("Насколько лицо призрака ниже линии глаз Водящего, м. Голова ставится по кости, так что рост и присед персонажа роли не играют")]
        [SerializeField] private float faceDrop = 0.12f;
        [Tooltip("Высота головы над корнем модели, если кости Head нет, м")]
        [SerializeField] private float fallbackHeadHeight = 1.6f;
        [Tooltip("Амплитуда дрожи наклона взгляда Водящего, °")]
        [SerializeField] private float shakeAmplitude = 2.4f;
        [SerializeField] private float shakeFrequency = 28f;

        /// <summary>Скример пошёл: номер игрока, который дотронулся. Точка для звука.</summary>
        public event Action<int> Started;

        private Image flash;
        private Coroutine running;
        private GameObject ghost;
        private Animator ghostAnimator;
        private GameObject lamp;

        public void Play(int playerId, GameObject victimAvatar, Transform keeperRoot, FirstPersonCameraRig rig, KeeperBeam beam, bool localIsKeeper)
        {
            Stop();
            Started?.Invoke(playerId);
            running = StartCoroutine(Run(victimAvatar, keeperRoot, rig, beam, localIsKeeper));
        }

        /// <summary>Оборвать сцену: конец раунда или смена ролей посреди неё.</summary>
        public void Stop()
        {
            if (running != null)
            {
                StopCoroutine(running);
                running = null;
            }

            DestroyGhost();

            if (flash != null)
            {
                flash.enabled = false;
            }
        }

        private void OnDisable()
        {
            Stop();
        }

        private IEnumerator Run(GameObject victimAvatar, Transform keeperRoot, FirstPersonCameraRig rig, KeeperBeam beam, bool localIsKeeper)
        {
            bool eyes = localIsKeeper && rig != null && keeperRoot != null;
            Vector3 forward = Vector3.forward;
            Vector3 eye = Vector3.zero;
            Vector3 modelScale = Vector3.one;
            Quaternion modelTwist = Quaternion.identity;
            float basePitch = 0f;

            if (eyes)
            {
                forward = rig.transform.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : keeperRoot.forward;
                eye = rig.EyePosition;
                basePitch = rig.Pitch;
                ghost = BuildGhost(victimAvatar, out modelScale, out modelTwist);
                if (ghost != null)
                {
                    LightGhost(forward, eye);
                }
            }

            float flashAlpha = localIsKeeper ? keeperFlashAlpha : othersFlashAlpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                if (flash != null)
                {
                    float alpha = elapsed < flashIn
                        ? elapsed / flashIn
                        : 1f - Mathf.Clamp01((elapsed - flashIn) / flashOut);
                    SetFlash(alpha * flashAlpha);
                }

                beam?.SetIntensityScale(torchDim);

                if (eyes)
                {
                    float lunge = Mathf.SmoothStep(lungeFrom, lungeTo, Mathf.Clamp01(elapsed / lungeTime));
                    if (ghost != null)
                    {
                        // Призрак растёт из темноты: первые кадры он меньше, чем настоящий.
                        float pop = Mathf.SmoothStep(0.6f, 1f, Mathf.Clamp01(elapsed / lungeTime));
                        ghost.transform.rotation = Quaternion.LookRotation(-forward) * Quaternion.Euler(0f, facingOffset, 0f) * modelTwist;
                        ghost.transform.localScale = modelScale * pop;
                        // Лицо — в центр кадра: ставим по кости головы, а не по корню,
                        // иначе низкий или присевший персонаж уезжает под нижний край экрана.
                        Vector3 targetHead = eye + forward * lunge + Vector3.down * faceDrop;
                        ghost.transform.position += targetHead - HeadAnchor();
                    }

                    float fade = 1f - Mathf.Clamp01(elapsed / duration);
                    float shake = Mathf.Sin(elapsed * shakeFrequency * Mathf.PI * 2f) * shakeAmplitude * fade;
                    rig.SetView(rig.Yaw, basePitch + shake);
                }

                yield return null;
            }

            if (eyes)
            {
                rig.SetView(rig.Yaw, basePitch);
            }

            beam?.SetIntensityScale(1f);
            SetFlash(0f);
            DestroyGhost();
            running = null;
        }

        /// <summary>
        /// Копия модели дошедшего без сетевых и игровых компонентов: остаются
        /// скелет, скины и аниматор. Аниматор стартует с idle, и удар на нём
        /// запускается тем же триггером, что у живого персонажа.
        /// </summary>
        private GameObject BuildGhost(GameObject victimAvatar, out Vector3 modelScale, out Quaternion modelTwist)
        {
            modelScale = Vector3.one;
            modelTwist = Quaternion.identity;
            if (victimAvatar == null)
            {
                return null;
            }

            Animator animator = victimAvatar.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                return null;
            }

            Transform model = animator.transform;
            // Клон живёт без родителя: масштаб берётся мировой, иначе модель под масштабированным корнем съёживается.
            modelScale = model.lossyScale;
            modelTwist = Quaternion.Inverse(victimAvatar.transform.rotation) * model.rotation;

            GameObject clone = Instantiate(model.gameObject);
            clone.name = "KeeperScreamerGhost";
            clone.SetActive(true);
            foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Destroy(behaviour);
            }

            ghostAnimator = clone.GetComponent<Animator>();
            ghostAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ghostAnimator.SetTrigger(PunchParameterHash);
            return clone;
        }

        private Vector3 HeadAnchor()
        {
            Transform head = ghostAnimator != null && ghostAnimator.isHuman ? ghostAnimator.GetBoneTransform(HumanBodyBones.Head) : null;
            return head != null ? head.position : ghost.transform.position + Vector3.up * fallbackHeadHeight * ghost.transform.localScale.y;
        }

        private void DestroyGhost()
        {
            if (ghost != null)
            {
                Destroy(ghost);
                ghost = null;
                ghostAnimator = null;
            }

            if (lamp != null)
            {
                Destroy(lamp);
                lamp = null;
            }
        }

        /// <summary>
        /// Лампа под подбородком призрака: единственное, что его освещает,
        /// когда фонарь погас. Стоит в мире между глазами и призраком, ниже
        /// линии взгляда — свет снизу, как фонарик под подбородком.
        /// </summary>
        private void LightGhost(Vector3 towardGhost, Vector3 eye)
        {
            lamp = new GameObject("KeeperScreamerLamp");
            lamp.transform.SetParent(transform, false);
            lamp.transform.position = eye + towardGhost * lampForward + Vector3.down * lampDrop;
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = ghostLightColor;
            light.intensity = ghostLightIntensity;
            light.range = ghostLightRange;
            light.shadows = LightShadows.None;
        }

        private void SetFlash(float alpha)
        {
            if (flash == null)
            {
                if (!EnsureFlash())
                {
                    return;
                }
            }

            Color c = flashColor;
            c.a = alpha;
            flash.color = c;
            flash.enabled = alpha > 0.001f;
        }

        private bool EnsureFlash()
        {
            if (overlayCanvas == null)
            {
                return false;
            }

            var go = new GameObject("KeeperScreamerFlash", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(overlayCanvas.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            flash = go.GetComponent<Image>();
            flash.raycastTarget = false;
            flash.enabled = false;
            return true;
        }
    }
}
