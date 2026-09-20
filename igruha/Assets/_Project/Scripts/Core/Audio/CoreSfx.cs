namespace Igruha.Core.Audio
{
    /// <summary>
    /// Имена слотов общего звука — персонаж, ловушки, интерфейс.
    ///
    /// Существует, чтобы строку слота нельзя было опечатать. Промах по имени в
    /// <see cref="MinigameAudioPlayer"/> не ломает сборку и не роняет игру: он
    /// один раз пишет предупреждение в консоль, а событие остаётся немым — то
    /// есть выглядит как «звук не приехал», а не как «имя не то».
    ///
    /// Значения совпадают с полем <c>id</c> манифестов <c>docs/art/core-*-sfx.json</c>
    /// и с идентификаторами паспорта поставки. Переименовывать нельзя: следующая
    /// поставка приедет под теми же именами.
    /// </summary>
    public static class CoreSfx
    {
        // ── Персонаж ─────────────────────────────────────────────────────────

        /// <summary>Толчок прыжка и приземление. Шесть вариаций.</summary>
        public const string JumpLand = "SFX_CHR_Jump_Land";

        /// <summary>Тело упало: нокдаун, жёсткое приземление. Четыре вариации.</summary>
        public const string Bodyfall = "SFX_CHR_Bodyfall";

        /// <summary>Удар попал в игрока. Две вариации.</summary>
        public const string PushHit = "SFX_CHR_Push_Hit";

        // ── Ловушки ──────────────────────────────────────────────────────────

        /// <summary>Дверь захлопнулась.</summary>
        public const string TrapDoorSlam = "SFX_TRAP_Door_Slam";

        /// <summary>Пол провалился. Возврат в исходное — тот же звук.</summary>
        public const string TrapFloorCollapse = "SFX_TRAP_Floor_Collapse";

        /// <summary>Рычаг дёрнули: дверь, провал, гейзер.</summary>
        public const string TrapLeverPull = "SFX_TRAP_Lever_Pull";

        // ── Интерфейс ────────────────────────────────────────────────────────

        /// <summary>Курсор или стик встал на кнопку.</summary>
        public const string UiHover = "SFX_UI_Hover";

        /// <summary>Подтверждение в любом меню.</summary>
        public const string UiClick = "SFX_UI_Click";

        /// <summary>Esc, B, кнопка «Назад».</summary>
        public const string UiBack = "SFX_UI_Back";

        /// <summary>Нажали заблокированную кнопку.</summary>
        public const string UiError = "SFX_UI_Error";

        /// <summary>Переключатель включили.</summary>
        public const string UiToggleOn = "SFX_UI_Toggle_On";

        /// <summary>Переключатель выключили.</summary>
        public const string UiToggleOff = "SFX_UI_Toggle_Off";

        /// <summary>Шаг ползунка громкости. Сыплется очередью.</summary>
        public const string UiSliderTick = "SFX_UI_Slider_Tick";

        /// <summary>Вкладки настроек, LB/RB.</summary>
        public const string UiTabSwitch = "SFX_UI_Tab_Switch";

        /// <summary>Символ ника в поле ввода.</summary>
        public const string UiTypeKey = "SFX_UI_Type_Key";

        /// <summary>Перебор персонажей, лента игр на ТВ.</summary>
        public const string UiCharBrowse = "SFX_UI_Char_Browse";

        /// <summary>Персонаж выбран.</summary>
        public const string UiCharSelect = "SFX_UI_Char_Select";

        /// <summary>Диалог «Выйти из игры?» и подобные.</summary>
        public const string UiDialogOpen = "SFX_UI_Dialog_Open";

        /// <summary>Меню открылось. Копия Click — можно играть сам Click.</summary>
        public const string UiMenuOpen = "SFX_UI_Menu_Open";

        /// <summary>Меню закрылось. Копия Back — можно играть сам Back.</summary>
        public const string UiMenuClose = "SFX_UI_Menu_Close";

        /// <summary>Тост, приглашение.</summary>
        public const string UiNotifyPop = "SFX_UI_Notify_Pop";

        /// <summary>Заставка при запуске. Черновик — ждёт замены.</summary>
        public const string UiLogoSting = "SFX_UI_Logo_Sting";

        /// <summary>Слот шага по поверхности. Хвост берётся из <see cref="SurfaceKind"/>.</summary>
        public static string Step(SurfaceKind surface) => surface switch
        {
            SurfaceKind.Wood => "SFX_CHR_Step_Wood",
            SurfaceKind.Carpet => "SFX_CHR_Step_Carpet",
            SurfaceKind.Metal => "SFX_CHR_Step_Metal",
            SurfaceKind.Grass => "SFX_CHR_Step_Grass",
            SurfaceKind.Gravel => "SFX_CHR_Step_Gravel",
            SurfaceKind.Snow => "SFX_CHR_Step_Snow",
            _ => "SFX_CHR_Step_Concrete",
        };
    }
}
