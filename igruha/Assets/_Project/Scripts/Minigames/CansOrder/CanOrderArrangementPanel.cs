using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Кассета расстановок под табло: три лучшие расстановки круга крупными
    /// значками, на четырёх гранях одновременно.
    ///
    /// <b>Зачем отдельный объект, а не строки табло.</b> Строка
    /// <c>WorldScoreboardFace</c> — 0.30 м, и в её подпись уходит имя игрока
    /// плюс пять символов: значок выходит ≈ 0.17 м, то есть ≈ 20 px с восьми
    /// метров, а прутья внутренней стены клетки режут строку на куски. Три
    /// расстановки — это вся игра (спека 5.3), и читаться они обязаны за пять
    /// секунд. Кассета даёт значок 0.34 м: прут перекрывает такой частично,
    /// двадцатипиксельный — целиком.
    ///
    /// Второе, ради чего кассета висит <b>под</b> табло: из своей клетки на
    /// верхней ступени верх грани закрывает собственная полка — она стоит
    /// ровно на луче зрения. Раунд начинается именно на верхней ступени.
    ///
    /// Спека это предусмотрела заранее: 9.2 — «табло не трогаем», 9.3 —
    /// «три цветные строки расстановок — собственными гранями». <c>Core/UI</c>
    /// здесь не задет ни строкой.
    ///
    /// <b>Своего состояния и своих RPC у кассеты нет.</b> Что показать, решает
    /// <see cref="CanOrderBoard"/> по состоянию, которое сервер уже опубликовал.
    /// </summary>
    public sealed class CanOrderArrangementPanel : MonoBehaviour
    {
        /// <summary>Одна строка одной грани: подпись, ячейки под значки, счёт.</summary>
        [Serializable]
        public struct Row
        {
            public TMP_Text label;
            public TMP_Text score;
            public MeshFilter[] cells;
        }

        [Tooltip("Строки всех граней подряд: сначала три строки первой грани, потом второй и так далее")]
        [SerializeField] private Row[] rows;

        [Tooltip("Сколько строк на одной грани")]
        [SerializeField] private int rowsPerFace = 3;

        [Tooltip("Контуры значков по индексу палитры — те же, что стоят на банках")]
        [SerializeField] private Mesh[] symbolMeshes;

        [Tooltip("Материалы значков по индексу палитры")]
        [SerializeField] private Material[] symbolMaterials;

        /// <summary>Сколько расстановок кассета вмещает.</summary>
        public int Capacity => rowsPerFace;

        /// <summary>
        /// Погасить кассету. Вызывается вместе с гашением табло: результаты
        /// круга исчезают навсегда, истории попыток в игре нет (спека 5.2).
        /// </summary>
        public void Clear()
        {
            if (rows == null)
            {
                return;
            }

            for (int i = 0; i < rows.Length; i++)
            {
                HideRow(i);
            }
        }

        /// <summary>
        /// Показать расстановку в строке <paramref name="index"/> на всех
        /// гранях сразу. Порядок строк задаёт вызывающий — он же и держит
        /// правило сортировки, одинаковое на всех машинах.
        /// </summary>
        public void SetRow(int index, string playerName, IReadOnlyList<int> arrangement, int matches, bool leader)
        {
            if (rows == null || index < 0 || index >= rowsPerFace)
            {
                return;
            }

            int faces = rows.Length / Mathf.Max(1, rowsPerFace);
            for (int face = 0; face < faces; face++)
            {
                FillRow(face * rowsPerFace + index, playerName, arrangement, matches, leader);
            }
        }

        /// <summary>Погасить строку на всех гранях: расстановок в круге может оказаться меньше трёх.</summary>
        public void HideRowOnAllFaces(int index)
        {
            if (rows == null || index < 0 || index >= rowsPerFace)
            {
                return;
            }

            int faces = rows.Length / Mathf.Max(1, rowsPerFace);
            for (int face = 0; face < faces; face++)
            {
                HideRow(face * rowsPerFace + index);
            }
        }

        private void FillRow(int i, string playerName, IReadOnlyList<int> arrangement, int matches, bool leader)
        {
            Row row = rows[i];
            if (row.label != null)
            {
                row.label.text = playerName;
                row.label.fontStyle = leader ? FontStyles.Bold : FontStyles.Normal;
                row.label.enabled = true;
            }

            if (row.score != null)
            {
                row.score.text = matches.ToString();
                row.score.fontStyle = leader ? FontStyles.Bold : FontStyles.Normal;
                row.score.enabled = true;
            }

            if (row.cells == null)
            {
                return;
            }

            for (int c = 0; c < row.cells.Length; c++)
            {
                MeshFilter cell = row.cells[c];
                if (cell == null)
                {
                    continue;
                }

                if (arrangement == null || c >= arrangement.Count)
                {
                    cell.gameObject.SetActive(false);
                    continue;
                }

                int canId = arrangement[c];
                // Меш и материал ставятся ссылкой на общий ассет, а не копией:
                // ячеек по двенадцать на грань, и копия материала на каждую
                // означала бы шестьдесят материалов ради пяти цветов.
                cell.sharedMesh = MeshFor(canId);
                if (cell.TryGetComponent(out MeshRenderer renderer))
                {
                    renderer.sharedMaterial = MaterialFor(canId);
                }

                cell.gameObject.SetActive(true);
            }
        }

        private void HideRow(int i)
        {
            Row row = rows[i];
            if (row.label != null)
            {
                row.label.enabled = false;
            }

            if (row.score != null)
            {
                row.score.enabled = false;
            }

            if (row.cells == null)
            {
                return;
            }

            for (int c = 0; c < row.cells.Length; c++)
            {
                if (row.cells[c] != null)
                {
                    row.cells[c].gameObject.SetActive(false);
                }
            }
        }

        private Mesh MeshFor(int canId)
        {
            return symbolMeshes != null && canId >= 0 && canId < symbolMeshes.Length
                ? symbolMeshes[canId]
                : null;
        }

        private Material MaterialFor(int canId)
        {
            return symbolMaterials != null && canId >= 0 && canId < symbolMaterials.Length
                ? symbolMaterials[canId]
                : null;
        }
    }
}
