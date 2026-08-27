using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Где Утка сейчас: этаж и прогресс вдоль трассы этого этажа. По этим двум
    /// числам раздаются места всем, кто не добежал до крыши.
    ///
    /// Прогресс берётся ТЕКУЩИЙ, а не лучший за раунд — в отличие от «Ангелов»,
    /// где копится лучший радиус. Здесь иначе нельзя: провал пола роняет жертву
    /// на этаж ниже, и весь смысл этой ловушки в откате назад. Считай мы лучший
    /// результат, самая жёсткая подстава в игре не меняла бы ничего.
    ///
    /// Пока Утка в воздухе, записанный этаж не меняется. Это закрывает крайний
    /// случай спеки: провалившемуся ровно на свистке зачитывается тот этаж,
    /// с которого он падал, а не тот, куда не успел приземлиться.
    ///
    /// Состояние собрано здесь целиком, а не разбросано по компонентам, и
    /// считает его только сервер: <see cref="Sample"/> зовётся под авторитетом,
    /// остальные машины получают готовое через <see cref="ApplyNetworkState"/>.
    /// Компонент вешается на аватар в рантайме, поэтому сетевым он быть не может
    /// (NGO индексирует NetworkBehaviour при спавне) — состояние реплицирует
    /// сетевая половина мини-игры списком на всех Уток сразу.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class DuckProgress : MonoBehaviour
    {
        private PlayerController motor;

        /// <summary>Этаж, индекс с нуля. Обновляется только когда Утка стоит на чём-то твёрдом.</summary>
        public int Floor { get; private set; }

        /// <summary>Прогресс вдоль трассы этажа, ШП: 0 у входа, длина этажа у выхода.</summary>
        public float Progress { get; private set; }

        /// <summary>Утка добежала до крыши.</summary>
        public bool Finished { get; private set; }

        /// <summary>Номер по порядку прибытия на финиш, с 1. Им и решаются места дошедших.</summary>
        public int FinishOrder { get; private set; }

        /// <summary>Утка погибла — прогресс заморожен на моменте попадания.</summary>
        public bool Dead { get; private set; }

        /// <summary>Время гибели от начала раунда, с. Решает равенство между погибшими на одной точке.</summary>
        public float DeathTime { get; private set; }

        /// <summary>Утка выбыла из гонки — неважно, добежала или погибла.</summary>
        public bool Retired => Finished || Dead;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
        }

        /// <summary>Начать раунд заново: сбросить всё, встать на этаж под ногами.</summary>
        public void ResetProgress(DuckHuntArena arena)
        {
            Finished = false;
            FinishOrder = 0;
            Dead = false;
            DeathTime = 0f;
            Floor = 0;
            Progress = 0f;

            if (arena != null)
            {
                Floor = arena.GetFloorIndex(transform.position);
                Progress = arena.GetProgressWidths(transform.position, Floor);
            }
        }

        /// <summary>
        /// Перечитать положение. Зовётся мини-игрой под проверкой авторитета:
        /// прогресс решает исход раунда, поэтому считать его вправе только сервер.
        /// </summary>
        public void Sample(DuckHuntArena arena)
        {
            if (arena == null || Retired)
            {
                return;
            }

            // Этаж переписываем только с земли. В полёте — что бы игрока ни
            // подняло и ни уронило — зачёт держится за последний твёрдый пол.
            if (motor != null && motor.IsGrounded)
            {
                Floor = arena.GetFloorIndex(transform.position);
            }

            Progress = arena.GetProgressWidths(transform.position, Floor);
        }

        /// <summary>Утка добежала. Порядок прибытия задаёт мини-игра — он одинаков на всех машинах.</summary>
        public void MarkFinished(int order)
        {
            if (Retired)
            {
                return;
            }

            Finished = true;
            FinishOrder = order;
        }

        /// <summary>Утка погибла: прогресс замирает на том, что успел записаться до попадания.</summary>
        public void MarkDead(float roundTime)
        {
            if (Retired)
            {
                return;
            }

            Dead = true;
            DeathTime = roundTime;
        }

        /// <summary>
        /// Принять состояние, решённое сервером. Присваивается целиком и без
        /// проверок «а не откат ли это»: сервер — единственный источник, и его
        /// свежее слово всегда вернее того, что тут лежало. В том числе поэтому
        /// откат провалом пола доезжает до клиента как есть.
        ///
        /// Финиш отдельным флагом не гоняем: номер по порядку прибытия
        /// начинается с единицы, поэтому ноль и значит «не добежал».
        /// </summary>
        public void ApplyNetworkState(int floor, float progress, int finishOrder, bool dead, float deathTime)
        {
            Floor = floor;
            Progress = progress;
            FinishOrder = finishOrder;
            Finished = finishOrder > 0;
            Dead = dead;
            DeathTime = deathTime;
        }
    }
}
