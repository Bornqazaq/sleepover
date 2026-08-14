using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Player;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Круглое меню эмоций: восемь секторов, курсор ходит от мыши, выбор —
    /// по отпусканию Tab. Панель — только вид: всё состояние (курсор, сектор
    /// под ним, выбор) считает PlayerEmoteAbility локального игрока, экран его
    /// читает. Привязка к игроку — из HubBootstrap после спавна.
    /// </summary>
    public sealed class EmoteWheel : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [Tooltip("Секторы по часовой стрелке, начиная с верхнего — порядок совпадает с номерами эмоций 1..8")]
        [SerializeField] private EmoteWheelSlot[] slots;
        [Tooltip("Метка-курсор, которая ездит по кругу за мышью")]
        [SerializeField] private RectTransform pointer;
        [Tooltip("Радиус, по которому расходится курсор от центра, px")]
        [SerializeField] private float pointerRadius = 190f;
        [Tooltip("Риг камеры: пока колесо открыто, мышь водит курсор, а не камеру")]
        [SerializeField] private ThirdPersonCameraRig cameraRig;

        private PlayerEmoteAbility ability;

        private void Awake()
        {
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        /// <summary>Привязать колесо к локальному игроку. null — отвязать (смена персонажа, конец раунда).</summary>
        public void BindLocalPlayer(PlayerEmoteAbility localAbility)
        {
            Unsubscribe();
            ability = localAbility;

            if (ability == null)
            {
                return;
            }

            ability.WheelOpened += OnWheelOpened;
            ability.WheelClosed += OnWheelClosed;
        }

        private void OnDisable() => Unsubscribe();

        private void Unsubscribe()
        {
            if (ability == null)
            {
                return;
            }

            ability.WheelOpened -= OnWheelOpened;
            ability.WheelClosed -= OnWheelClosed;
            ability = null;
        }

        private void Update()
        {
            if (ability == null || !ability.IsWheelOpen)
            {
                return;
            }

            int hovered = ability.HoveredEmote;
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].SetHovered(i + 1 == hovered);
            }

            if (pointer != null)
            {
                pointer.anchoredPosition = ability.Pointer * pointerRadius;
            }
        }

        private void OnWheelOpened()
        {
            IReadOnlyList<string> names = ability.EmoteNames;
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].Bind(i < names.Count ? names[i] : null);
            }

            if (pointer != null)
            {
                pointer.anchoredPosition = Vector2.zero;
            }

            if (panel != null)
            {
                panel.SetActive(true);
            }

            cameraRig?.SetLookSuspended(true);
        }

        private void OnWheelClosed()
        {
            if (panel != null)
            {
                panel.SetActive(false);
            }

            cameraRig?.SetLookSuspended(false);
        }
    }
}
