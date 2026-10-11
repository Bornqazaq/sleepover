using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Локальная реакция финиша: каждый участник видит конфетти у той Утки,
    /// которая действительно вошла в общую финишную зону. Места и очки этот
    /// компонент не считает — их по-прежнему решает DuckHuntMinigame.
    /// </summary>
    [RequireComponent(typeof(DuckHuntFinishZone))]
    public sealed class DuckHuntFinishCelebration : MonoBehaviour
    {
        [SerializeField] private ParticleSystem confetti;
        [SerializeField] private int burstCount = 90;

        private readonly HashSet<int> celebrated = new HashSet<int>();
        private DuckHuntFinishZone finishZone;

        private void Awake() => finishZone = GetComponent<DuckHuntFinishZone>();

        private void OnEnable()
        {
            celebrated.Clear();
            if (finishZone != null)
            {
                finishZone.DuckArrived += Celebrate;
            }
        }

        private void OnDisable()
        {
            if (finishZone != null)
            {
                finishZone.DuckArrived -= Celebrate;
            }
            celebrated.Clear();
        }

        private void Celebrate(DuckProgress duck)
        {
            if (duck == null || confetti == null || !celebrated.Add(duck.GetInstanceID()))
            {
                return;
            }

            confetti.Emit(Mathf.Max(1, burstCount));
        }
    }
}
