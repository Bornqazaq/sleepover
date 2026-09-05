using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Эффекты, уникальные для «Порядка банок» — подфаза 4.4. Здесь ровно
    /// одно событие: табло загорается в начале стадии показа.
    ///
    /// Всё остальное на арене — спуск клеток, створки, лапа медведя, конфетти
    /// на итогах — общее и уже сделано в <see cref="Circus.CircusEffects"/>.
    /// Дублировать его тут значило бы получить два облака опилок на одну
    /// створку и правку, которая ломает «Секундомер».
    ///
    /// <b>Своего состояния и своих RPC нет.</b> Эффект висит на
    /// <see cref="MinigameStageState.StageStarted"/> — стадия приезжает с
    /// сервера и одинакова на каждой машине, поэтому вспышка идёт у всех
    /// в один момент и без единого пакета.
    ///
    /// <b>Почему именно этот момент.</b> Табло — единственный источник
    /// информации в игре, и оно горит семь секунд из двадцати двух. Игрок
    /// в момент показа смотрит на свою полку: без вспышки он замечает табло
    /// с опозданием в секунду-две, а это четверть окна чтения.
    /// </summary>
    public sealed class CansOrderEffects : MonoBehaviour
    {
        [Tooltip("Состояние стадий мини-игры. Заполняет билдер реквизита")]
        [SerializeField] private MinigameStageState stageState;

        [Tooltip("Вспышка ламп табло в начале показа результатов")]
        [SerializeField] private ParticleSystem boardFlash;

        [Tooltip("Свет, подхватывающий вспышку. Гаснет сам")]
        [SerializeField] private Light boardGlow;

        [Tooltip("Яркость света на пике вспышки")]
        [SerializeField] private float glowIntensity = 3.2f;

        [Tooltip("За сколько секунд свет гаснет")]
        [SerializeField] private float glowFade = 0.9f;

        private float glowTimer;

        private void OnEnable()
        {
            if (stageState != null)
            {
                stageState.StageStarted += HandleStageStarted;
            }

            if (boardGlow != null)
            {
                boardGlow.enabled = false;
            }
        }

        private void OnDisable()
        {
            if (stageState != null)
            {
                stageState.StageStarted -= HandleStageStarted;
            }
        }

        private void Update()
        {
            if (glowTimer <= 0f || boardGlow == null)
            {
                return;
            }

            glowTimer -= Time.deltaTime;
            if (glowTimer <= 0f)
            {
                boardGlow.enabled = false;
                return;
            }

            boardGlow.intensity = glowIntensity * (glowTimer / glowFade);
        }

        private void HandleStageStarted(byte stage)
        {
            if (stage != CansOrderMinigame.StageReveal)
            {
                return;
            }

            if (boardFlash != null)
            {
                boardFlash.Play(true);
            }

            if (boardGlow != null)
            {
                boardGlow.enabled = true;
                boardGlow.intensity = glowIntensity;
                glowTimer = glowFade;
            }
        }
    }
}
