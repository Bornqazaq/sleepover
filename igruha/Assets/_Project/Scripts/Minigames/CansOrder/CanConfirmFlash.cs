using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Вспышка на колоколе подтверждения — подфаза 4.4.
    ///
    /// <b>Эффект локальный, и это не недосмотр.</b> «Принято» до стадии показа
    /// не должно быть известно никому, кроме самого игрока: иначе по чужому
    /// колоколу видно, кто уже подтвердил, а это преимущество, которого в игре
    /// быть не должно (спека 4, тот же принцип, по которому
    /// <see cref="CanConfirmButton.MarkAccepted"/> зажигает лампу только
    /// у владельца). Поэтому эффект и висит на
    /// <see cref="CanConfirmButton.Confirmed"/> — событии, которое поднимается
    /// на машине нажавшего и больше нигде.
    ///
    /// Своего состояния у вспышки нет: она читает событие и запускает партикл.
    /// </summary>
    [RequireComponent(typeof(CanConfirmButton))]
    public sealed class CanConfirmFlash : MonoBehaviour
    {
        [Tooltip("Кнопка этой же клетки")]
        [SerializeField] private CanConfirmButton button;

        [Tooltip("Всплеск в момент удара по колоколу")]
        [SerializeField] private ParticleSystem burst;

        private void Awake()
        {
            if (button == null)
            {
                button = GetComponent<CanConfirmButton>();
            }
        }

        private void OnEnable()
        {
            if (button != null)
            {
                button.Confirmed += HandleConfirmed;
            }
        }

        private void OnDisable()
        {
            if (button != null)
            {
                button.Confirmed -= HandleConfirmed;
            }
        }

        private void HandleConfirmed(CanConfirmButton source, PlayerController player)
        {
            if (burst != null)
            {
                burst.Play(true);
            }
        }
    }
}
