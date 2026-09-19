using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.Infection
{
    /// <summary>
    /// Кто кого достал в этот физический такт.
    ///
    /// Отдельный класс, а не метод в мини-игре, по двум причинам: правило
    /// разбора спорных касаний — самостоятельное решение, которое читается
    /// целиком, и в сетевой фазе именно оно уходит на сервер без изменений.
    ///
    /// <b>Спор решается дистанцией, а не случайностью.</b> Два заражённых,
    /// дотянувшихся до одного чистого в одном такте, — обычное дело в толпе.
    /// Серверного времени между ними нет: такт один. Ближайший — правило,
    /// которое видно глазами и повторяется; жребий в такой момент выглядит
    /// как враньё. При равной дистанции берём меньший номер игрока: в этом
    /// случае важно только то, чтобы ответ был один и тот же у всех машин.
    ///
    /// Чистого заражают ровно один раз за такт — остальные претенденты уходят
    /// ни с чем, и цепочка «заразился и тут же заразил соседа» не срабатывает
    /// в одном кадре.
    ///
    /// Не аллоцирует между вызовами: список результатов переиспользуется.
    /// </summary>
    public sealed class InfectionContactResolver
    {
        private readonly List<Contact> contacts = new List<Contact>(8);

        public readonly struct Contact
        {
            /// <summary>Кто заразил.</summary>
            public readonly InfectionState Carrier;

            /// <summary>Кого заразили.</summary>
            public readonly InfectionState Victim;

            public Contact(InfectionState carrier, InfectionState victim)
            {
                Carrier = carrier;
                Victim = victim;
            }
        }

        /// <summary>
        /// Разобрать касания. Результат живёт до следующего вызова — копировать
        /// его не нужно, пережёвывать после следующего разбора нельзя.
        /// </summary>
        public IReadOnlyList<Contact> Resolve(IReadOnlyList<InfectionState> states, float radius)
        {
            contacts.Clear();
            float squared = radius * radius;

            for (int v = 0; v < states.Count; v++)
            {
                InfectionState victim = states[v];
                if (victim == null || !victim.CanBeInfected || victim.Avatar == null)
                {
                    continue;
                }

                InfectionState best = null;
                float bestDistance = float.MaxValue;

                for (int c = 0; c < states.Count; c++)
                {
                    InfectionState carrier = states[c];
                    if (carrier == null || carrier == victim || !carrier.CanInfect || carrier.Avatar == null)
                    {
                        continue;
                    }

                    float distance = (carrier.Position - victim.Position).sqrMagnitude;
                    if (distance > squared)
                    {
                        continue;
                    }

                    if (distance < bestDistance ||
                        (Mathf.Approximately(distance, bestDistance) && best != null && carrier.PlayerId < best.PlayerId))
                    {
                        best = carrier;
                        bestDistance = distance;
                    }
                }

                if (best != null)
                {
                    contacts.Add(new Contact(best, victim));
                }
            }

            return contacts;
        }
    }
}
