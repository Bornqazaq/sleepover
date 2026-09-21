using TMPro;
using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>
    /// Табличка над мебелью: что происходит на станции и какой результат.
    /// Читает состояние станции, своего не хранит — поэтому у хоста и клиентов
    /// показывает одно и то же.
    /// </summary>
    public sealed class HubActivityBoard : MonoBehaviour
    {
        [Tooltip("Строка состояния: свободно, занято, результат броска")]
        [SerializeField] private TMP_Text statusLine;

        [Tooltip("Строка лучшего результата за вечер")]
        [SerializeField] private TMP_Text bestLine;

        [Tooltip("Текст, когда за станцией никого")]
        [SerializeField] private string idleText = "СВОБОДНО";

        [Tooltip("Текст, пока станция занята и бросок ещё не сделан")]
        [SerializeField] private string busyText = "ИГРАЕТ";

        /// <summary>
        /// Показать состояние станции. Зовётся не каждый кадр, а на изменение
        /// сетевого состояния: строки тут собираются заново, и в <c>Update</c>
        /// это было бы мусором для сборщика.
        /// </summary>
        public void Render(HubActivityStation station)
        {
            if (station == null)
            {
                return;
            }

            if (statusLine != null)
            {
                statusLine.text = StatusOf(station);
            }

            if (bestLine != null)
            {
                bestLine.text = station.BestScore > 0
                    ? $"ЛУЧШИЙ: {station.BestName} — {station.BestScore}"
                    : string.Empty;
            }
        }

        private string StatusOf(HubActivityStation station)
        {
            switch (station.Phase)
            {
                case HubActivityPhase.Counting:
                    return $"СБИТО: {station.LastScore}";

                case HubActivityPhase.Launched:
                    return busyText;

                case HubActivityPhase.Occupied:
                    return busyText;

                default:
                    return station.LastScore > 0 ? $"СБИТО: {station.LastScore}" : idleText;
            }
        }
    }
}
