using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.Exam
{
    /// <summary>
    /// Болванка-Ученик: в фазе выбора уходит на одну из платформ, иногда
    /// передумывает в нагнетании. Нужна затем, чтобы матч играл себя сам —
    /// у «Экзамена» ротация, и без болванок каждый ход пришлось бы отыгрывать
    /// руками.
    ///
    /// Роль Ведущего болванка тоже тянет, но не отсюда: вопрос за неё берёт
    /// контроллер из заготовок, потому что печатать ей нечем.
    ///
    /// ⚠️ <b>В сетевой сессии болванок не бывает.</b> Их вешает только
    /// одиночный прогон; в сети <c>OnPlayersReady</c> их не создаёт.
    /// </summary>
    public sealed class ExamDebugBot : MonoBehaviour
    {
        [Tooltip("Насколько близко к центру платформы считать, что дошёл")]
        [SerializeField] private float arriveDistance = 1.2f;
        [Tooltip("Шанс передумать и перебежать на другую платформу в нагнетании")]
        [SerializeField] private float changeMindChance = 0.35f;

        private PlayerController avatar;
        private PlayerInputReader reader;
        private Transform target;
        private bool decidedThisQuestion;
        private bool mindChanged;

        private void Awake()
        {
            avatar = GetComponent<PlayerController>();
            reader = GetComponent<PlayerInputReader>();
        }

        /// <summary>Новый вопрос — решение принимается заново.</summary>
        public void ResetForQuestion()
        {
            decidedThisQuestion = false;
            mindChanged = false;
            target = null;
        }

        /// <summary>Выбрать платформу. Зовёт контроллер, когда открывается фаза выбора.</summary>
        public void ChooseSide(Transform platformA, Transform platformB)
        {
            if (decidedThisQuestion)
            {
                return;
            }

            decidedThisQuestion = true;
            target = Random.value < 0.5f ? platformA : platformB;
        }

        /// <summary>
        /// Нагнетание: болванка может передумать. Ровно то, что делают живые
        /// игроки, и ровно то, из-за чего фаза существует.
        /// </summary>
        public void MaybeChangeMind(Transform platformA, Transform platformB)
        {
            if (mindChanged || Random.value > changeMindChance)
            {
                return;
            }

            mindChanged = true;
            target = target == platformA ? platformB : platformA;
        }

        /// <summary>Перестать идти: вопрос кончился.</summary>
        public void Halt()
        {
            target = null;
            reader?.DriveMove(Vector2.zero);
        }

        private void Update()
        {
            if (reader == null || avatar == null)
            {
                return;
            }

            if (target == null)
            {
                reader.DriveMove(Vector2.zero);
                return;
            }

            Vector3 delta = target.position - avatar.transform.position;
            delta.y = 0f;

            if (delta.sqrMagnitude <= arriveDistance * arriveDistance)
            {
                reader.DriveMove(Vector2.zero);
                return;
            }

            // Ввод болванки — в мировых осях: контроллер сам развернёт его
            // относительно камеры, как и у живого игрока.
            Vector3 dir = delta.normalized;
            reader.DriveMove(new Vector2(dir.x, dir.z));
        }
    }
}
