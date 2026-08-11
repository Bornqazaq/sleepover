using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Прогресс утки по высоте башни: максимальная достигнутая высота и
    /// момент финиша. По этим данным контроллер раздаёт места: сначала
    /// добравшиеся до крыши по времени, затем остальные по высоте.
    /// </summary>
    public sealed class DuckProgress : MonoBehaviour
    {
        public int PlayerId { get; set; }
        public float BestHeight { get; private set; }
        public bool Finished { get; private set; }
        public float FinishTime { get; private set; }

        private PlayerController motor;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            BestHeight = transform.position.y;
        }

        private void Update()
        {
            if (Finished)
            {
                return;
            }

            float y = transform.position.y;
            if (y > BestHeight)
            {
                BestHeight = y;
            }
        }

        public void MarkFinished(float time)
        {
            if (Finished)
            {
                return;
            }

            Finished = true;
            FinishTime = time;
        }

        /// <summary>
        /// Попадание от охотника. Само падение уже вызвал импульс снаряда через
        /// ApplyPush — он же выбрал сторону (в лицо или в спину). Здесь остаётся
        /// подстраховка на случай слабого выстрела: утка обязана потерять время.
        /// </summary>
        public void RegisterHit()
        {
            if (motor != null && !motor.IsKnockedDown)
            {
                motor.Knockdown(KnockdownType.FallForward);
            }
        }
    }
}
