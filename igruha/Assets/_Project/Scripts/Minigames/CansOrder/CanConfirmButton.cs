using System;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Player;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Кнопка подтверждения на торце полки. Вокруг неё собрана честность игры.
    ///
    /// <b>Кнопка не даёт никакой обратной связи о результате.</b> Лампа говорит
    /// ровно одно — «принято», и выглядит одинаково у того, кто собрал скрытую
    /// расстановку, и у того, кто промахнулся на все пять позиций. Иначе первый
    /// же собравший выдал бы факт соседям раньше табло, а результат, увиденный
    /// раньше остальных, — это преимущество, которого в игре быть не должно.
    /// Тот же принцип, на котором построена <c>CageButton</c> «Секундомера».
    ///
    /// Сама кнопка ничего не оценивает и не считает: она поднимает намерение
    /// «игрок подтвердил вот эту расстановку», а решает всё правила игры.
    /// В фазе 3 это намерение уедет в <c>ServerRpc</c> без переписывания
    /// кнопки — ровно так же, как <c>CageButton.HoldIntent</c>.
    ///
    /// Нажатие разовое, не удержание: <see cref="IInteractable"/>, а не
    /// <c>IHoldInteractable</c>. Держать здесь нечего.
    /// </summary>
    public sealed class CanConfirmButton : MonoBehaviour, IInteractable
    {
        private const string PromptConfirm = "Подтвердить расстановку";

        [Tooltip("Колпак лампы. Единственная обратная связь: погашен или «принято»")]
        [SerializeField] private Renderer lamp;
        [Tooltip("Маячок над кнопкой. Сама кнопка мелкая, и её заслоняет спина персонажа")]
        [SerializeField] private Light beacon;
        [Tooltip("Полка этого же игрока: пока у неё жив курсор, нажатие принадлежит ей")]
        [SerializeField] private CanShelf shelf;

        [Header("Цвета лампы")]
        [Tooltip("Окно открыто, расстановка ещё не подтверждена")]
        [SerializeField] private Color idleColor = new Color(0.22f, 0.22f, 0.25f);
        [Tooltip("Расстановка принята. ОДИН цвет на всех: и на собравшего, и на промахнувшегося — иначе лампа выдаёт ответ")]
        [SerializeField] private Color acceptedColor = new Color(0.95f, 0.86f, 0.42f);

        /// <summary>
        /// Игрок подтвердил расстановку. Это намерение, а не результат: что
        /// с ним делать, решают правила игры.
        /// </summary>
        public event Action<CanConfirmButton, PlayerController> Confirmed;

        private PlayerController owner;
        private MaterialPropertyBlock block;
        private string prompt = PromptConfirm;
        private bool cursorHighlight;

        /// <summary>Идёт ли окно выставления.</summary>
        public bool WindowOpen { get; private set; }

        /// <summary>Игрок уже подтвердил расстановку в этом круге.</summary>
        public bool Accepted { get; private set; }

        /// <summary>
        /// Игрок собрал скрытую расстановку и в следующих кругах не участвует:
        /// полка гаснет, кнопка мертва.
        /// </summary>
        public bool Solved { get; private set; }

        public string InteractionPrompt => prompt;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            ApplyLamp(false);
        }

        /// <summary>Кому эта кнопка принадлежит. Чужую нажать нельзя.</summary>
        public void SetOwner(PlayerController player) => owner = player;

        /// <summary>Полка того же игрока — источник ответа «занята ли рука».</summary>
        public void SetShelf(CanShelf ownerShelf) => shelf = ownerShelf;

        /// <summary>
        /// Начался круг: расстановку снова можно подтвердить. Собравшему окно
        /// не открывается — он уже вне игры до конца раунда.
        /// </summary>
        public void OpenWindow()
        {
            if (Solved)
            {
                return;
            }

            WindowOpen = true;
            Accepted = false;
            ApplyLamp(false);
        }

        /// <summary>
        /// Окно кончилось. Лампы гаснут у всех разом, поэтому по чужой кнопке
        /// нельзя определить, кто подтвердил в последнюю секунду, а кто
        /// не успел вовсе.
        /// </summary>
        public void CloseWindow()
        {
            WindowOpen = false;
            ApplyLamp(false);
        }

        /// <summary>
        /// Расстановка принята. Зовут правила игры, а не сама кнопка: в фазе 3
        /// решение принимает сервер, и лампа обязана загораться от его ответа,
        /// а не от факта нажатия.
        /// </summary>
        public void MarkAccepted()
        {
            Accepted = true;
            ApplyLamp(true);
        }

        /// <summary>Игрок собрал расстановку: кнопка мертва до конца раунда.</summary>
        public void MarkSolved()
        {
            Solved = true;
            WindowOpen = false;
            ApplyLamp(false);
        }

        /// <summary>Новый раунд: игрок снова в игре.</summary>
        public void ResetForRound()
        {
            Solved = false;
            Accepted = false;
            WindowOpen = false;
            ApplyLamp(false);
        }

        public bool CanInteract(PlayerController player)
        {
            if (player == null || player != owner || Solved || !WindowOpen || Accepted)
            {
                return false;
            }

            // Пока у полки жив курсор, нажатие принадлежит ей: кнопка —
            // последняя ячейка её же ряда, и до неё не надо идти ногами.
            // Иначе PlayerInteractor выбирал бы между двумя интерактивами
            // по расстоянию, и E делал бы то одно, то другое при неподвижном
            // курсоре. Полка сама донесёт нажатие сюда, когда курсор на кнопке.
            if (shelf != null && shelf.OwnsInput)
            {
                return false;
            }

            prompt = PromptConfirm;
            return true;
        }

        public void Interact(PlayerController player)
        {
            // CanInteract здесь не годится: он отказывает, пока полка держит
            // ввод, а зовёт нас в этом случае как раз она. Проверяем то, что
            // действительно решает, — окно, владельца и состояние круга.
            if (player == null || player != owner || Solved || !WindowOpen || Accepted)
            {
                return;
            }

            Confirmed?.Invoke(this, player);
        }

        /// <summary>
        /// Курсор полки стоит на кнопке. Лампа подсвечивается, чтобы ячейка
        /// «подтвердить» читалась тем же способом, что и банки, — иначе конец
        /// ряда выглядит как пустое место.
        /// </summary>
        public void SetCursorHighlight(bool underCursor)
        {
            if (cursorHighlight == underCursor)
            {
                return;
            }

            cursorHighlight = underCursor;
            ApplyLamp(Accepted);
        }

        /// <summary>
        /// Лампа. Цвет ставится через <see cref="MaterialPropertyBlock"/>:
        /// обращение к <c>material</c> плодит копию материала на каждую кнопку.
        /// </summary>
        private void ApplyLamp(bool accepted)
        {
            Color color = accepted ? acceptedColor : idleColor;

            // Курсор на кнопке высветляет лампу тем же приёмом, что и банку.
            // Принятую не трогаем: её цвет — это ответ игры, и подмешивать
            // в него состояние курсора нельзя.
            if (cursorHighlight && !accepted)
            {
                color = Color.Lerp(color, Color.white, CursorTint);
            }

            if (lamp != null)
            {
                block ??= new MaterialPropertyBlock();
                lamp.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(LegacyColorId, color);
                block.SetColor(EmissionId, accepted ? color : Color.black);
                lamp.SetPropertyBlock(block);
            }

            if (beacon != null)
            {
                beacon.color = color;
                beacon.enabled = accepted;
            }
        }

        /// <summary>Насколько курсор высветляет лампу. То же число, что у банки, — состояния должны читаться одинаково.</summary>
        private const float CursorTint = 0.35f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    }
}
