using System.Collections.Generic;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.Exam
{
    /// <summary>
    /// Места по накопленным очкам Экзамена.
    ///
    /// <b>ОЭ — не очки катки.</b> Внутри матча их набирается до пятнадцати,
    /// а <c>SessionManager</c> начисляет по формуле «игроков − место», то есть
    /// максимум семь. Отдай мы ОЭ напрямую — победа в «Экзамене» весила бы
    /// вдвое больше победы в Duck Hunt, и пул мини-игр перестал бы быть равным.
    /// Поэтому здесь ОЭ превращаются в места, и наружу уходят только места.
    ///
    /// <c>EliminationRanking</c> из Core тут не годится принципиально: он
    /// ранжирует по порядку вылета, а в «Экзамене» выбывания нет вовсе —
    /// все играют до конца.
    /// </summary>
    public static class ExamRanking
    {
        /// <summary>
        /// Расставить места всем участникам, включая отключившихся: место
        /// считается по накопленным ОЭ на момент выхода.
        /// </summary>
        public static void Fill(List<ExamEntry> entries, MinigameResults results)
        {
            if (entries == null || results == null)
            {
                return;
            }

            results.Clear();

            var ordered = new List<ExamEntry>(entries);
            ordered.Sort(Compare);

            for (int i = 0; i < ordered.Count; i++)
            {
                results.Add(ordered[i].PlayerId, i + 1);
            }
        }

        /// <summary>
        /// Цепочка тайбрейков. Каждый следующий критерий применяется, только
        /// если предыдущий не развёл:
        ///
        /// 1. больше ОЭ;
        /// 2. больше одиноких угадываний — награждает игру против толпы;
        /// 3. больше верных ответов всего;
        /// 4. раньше набрал свой счёт.
        ///
        /// Четвёртый разводит всегда: два игрока не могут начислиться в одну
        /// миллисекунду серверных часов. Без него места на равном счёте
        /// зависели бы от порядка в списке, то есть от порядка подключения.
        /// </summary>
        private static int Compare(ExamEntry a, ExamEntry b)
        {
            if (a.Score != b.Score)
            {
                return b.Score.CompareTo(a.Score);
            }

            if (a.LonelyHits != b.LonelyHits)
            {
                return b.LonelyHits.CompareTo(a.LonelyHits);
            }

            if (a.CorrectAnswers != b.CorrectAnswers)
            {
                return b.CorrectAnswers.CompareTo(a.CorrectAnswers);
            }

            // Раньше набрал — выше. Тот, кто пришёл к тому же счёту первым,
            // держал его дольше под давлением.
            return a.LastScoredAt.CompareTo(b.LastScoredAt);
        }
    }
}
