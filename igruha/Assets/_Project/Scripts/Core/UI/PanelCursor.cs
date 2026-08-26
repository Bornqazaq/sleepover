using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Курсор на время игровой панели: отдать его панели при открытии
    /// и вернуть как было при закрытии.
    ///
    /// Живёт в Core, потому что нужен уже второй игре: панель Ведущего
    /// «Экзамена» и панель решения «Верю / не верю» решают одну задачу —
    /// по кнопкам надо кликать, а камера держит курсор захваченным
    /// и невидимым.
    ///
    /// ⚠️ В «Экзамене» этого не было вовсе, и мимо прошли обе приёмки:
    /// панель открывалась, но попасть мышью ни в поле, ни в тумблер было
    /// нельзя. Болванка автопрогона мышью не пользуется, а сетевая приёмка
    /// крафтила пакеты мимо панели.
    /// </summary>
    public sealed class PanelCursor
    {
        private CursorLockMode restoreLockMode;
        private bool restoreVisible;
        private bool taken;

        /// <summary>Запомнить состояние курсора и освободить его для панели.</summary>
        public void Release()
        {
            // Повторный вызов не должен запоминать уже освобождённый курсор
            // как исходное состояние — иначе после закрытия панели игрок
            // остаётся с видимой мышью вместо управления камерой.
            if (taken)
            {
                return;
            }

            restoreLockMode = Cursor.lockState;
            restoreVisible = Cursor.visible;
            taken = true;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Вернуть курсор в то состояние, в котором его взяли.</summary>
        public void Restore()
        {
            if (!taken)
            {
                return;
            }

            taken = false;
            Cursor.lockState = restoreLockMode;
            Cursor.visible = restoreVisible;
        }
    }
}
