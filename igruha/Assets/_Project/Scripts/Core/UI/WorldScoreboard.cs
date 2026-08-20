using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Многогранное табло в мире: одно и то же содержимое на всех гранях,
    /// поэтому читается с любой стороны арены без поворота головы.
    ///
    /// Про правила конкретной игры не знает ничего: заголовок, строка задания
    /// и список пар «подпись — значение». Что туда класть, решает игра.
    ///
    /// Экранного HUD у «Секундомера» нет намеренно — напряжение должно
    /// читаться по миру, а не по цифрам в углу. <see cref="RoundHud"/> при этом
    /// остаётся на своём месте: он про таймер раунда и экран результатов
    /// мини-игры, а не про её содержимое.
    ///
    /// Строки задаются данными, а не собираются конкатенацией: подпись и
    /// значение — разные поля, поэтому обновление не аллоцирует.
    /// </summary>
    public sealed class WorldScoreboard : MonoBehaviour
    {
        [Tooltip("Грани табло. Содержимое пишется во все сразу")]
        [SerializeField] private WorldScoreboardFace[] faces = System.Array.Empty<WorldScoreboardFace>();

        private int rowCursor;

        /// <summary>Сколько строк помещается на грань.</summary>
        public int RowCapacity
        {
            get
            {
                int capacity = int.MaxValue;
                for (int i = 0; i < faces.Length; i++)
                {
                    if (faces[i] != null)
                    {
                        capacity = Mathf.Min(capacity, faces[i].RowCapacity);
                    }
                }

                return capacity == int.MaxValue ? 0 : capacity;
            }
        }

        /// <summary>Грани, найденные в детях. Зовётся билдером арены после сборки.</summary>
        public void SetFaces(IReadOnlyList<WorldScoreboardFace> collected)
        {
            faces = new WorldScoreboardFace[collected.Count];
            for (int i = 0; i < collected.Count; i++)
            {
                faces[i] = collected[i];
            }
        }

        public void SetHeader(string title, string subtitle)
        {
            for (int i = 0; i < faces.Length; i++)
            {
                if (faces[i] == null)
                {
                    continue;
                }

                faces[i].SetTitle(title);
                faces[i].SetSubtitle(subtitle);
            }
        }

        /// <summary>Начать заполнение строк. Строки, которые не заполнили, скроет <see cref="EndRows"/>.</summary>
        public void BeginRows()
        {
            rowCursor = 0;
        }

        public void AddRow(string label, string value)
        {
            for (int i = 0; i < faces.Length; i++)
            {
                if (faces[i] != null)
                {
                    faces[i].SetRow(rowCursor, label, value);
                }
            }

            rowCursor++;
        }

        public void EndRows()
        {
            for (int i = 0; i < faces.Length; i++)
            {
                if (faces[i] != null)
                {
                    faces[i].HideRowsFrom(rowCursor);
                }
            }
        }

        public void Clear()
        {
            SetHeader(string.Empty, string.Empty);
            BeginRows();
            EndRows();
        }
    }
}
