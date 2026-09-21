using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>
    /// Шкала силы броска — полоска в мире, а не в Canvas: у хаба нет своего
    /// игрового HUD, а заводить его ради одной полоски незачем.
    ///
    /// Видна только тому, кто целится: станция включает её у себя и ни у кого
    /// больше. Остальным чужая шкала не нужна и только мешала бы смотреть.
    /// </summary>
    public sealed class HubActivityPowerGauge : MonoBehaviour
    {
        [Tooltip("Заполняемая часть шкалы. Тянется по локальной оси X от левого края")]
        [SerializeField] private Transform fill;

        [Tooltip("Корень шкалы — его и прячем целиком")]
        [SerializeField] private GameObject root;

        [Tooltip("Полная длина шкалы, м")]
        [SerializeField] private float length = 0.9f;

        /// <summary>Разворачивать ли шкалу к камере каждый кадр.</summary>
        [SerializeField] private bool faceCamera = true;

        private float power;

        private void Awake()
        {
            if (root == null)
            {
                root = gameObject;
            }

            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (!faceCamera || root == null || !root.activeSelf)
            {
                return;
            }

            Camera view = Camera.main;
            if (view == null)
            {
                return;
            }

            Vector3 toCamera = view.transform.position - transform.position;
            toCamera.y = 0f;

            if (toCamera.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
        }

        public void SetVisible(bool visible)
        {
            if (root != null && root.activeSelf != visible)
            {
                root.SetActive(visible);
            }
        }

        /// <summary>Сила 0…1. Полоска растёт вправо от своего левого края.</summary>
        public void SetPower(float value)
        {
            power = Mathf.Clamp01(value);

            if (fill == null)
            {
                return;
            }

            Vector3 scale = fill.localScale;
            scale.x = Mathf.Max(0.0001f, power);
            fill.localScale = scale;

            // Растягивание идёт от центра, поэтому левый край держим смещением.
            Vector3 position = fill.localPosition;
            position.x = -0.5f * length * (1f - power);
            fill.localPosition = position;
        }
    }
}
