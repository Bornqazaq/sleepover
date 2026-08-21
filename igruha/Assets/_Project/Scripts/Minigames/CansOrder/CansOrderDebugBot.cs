using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Болванка, которая играет за того, кем никто не управляет. Нужна ровно
    /// для соло-прогона: без неё проверяется только вылет молчунов, а правила
    /// конца раунда, деление мест и спуск клеток по доле остаются
    /// непроверенными — собирать расстановку некому.
    ///
    /// <b>Болванка подглядывает в ответ, и это намеренно.</b> Честно
    /// дедуцировать она не умеет, а случайный перебор 120 перестановок упирался
    /// бы в потолок кругов каждый раунд — то есть проверял бы ровно одну ветку
    /// из трёх. Вместо этого она с заданной вероятностью выставляет верную
    /// расстановку, иначе случайную: так собирают в разные круги, и все три
    /// случая правила 5.6 встречаются сами собой.
    ///
    /// <b>В сети болванок быть не должно.</b> <c>OnPlayersReady</c> идёт на всех
    /// машинах, и «этой машиной не управляется» верно для каждого чужого
    /// игрока — каждый клиент навесил бы бота на всех остальных и подтверждал
    /// бы за них. Проверку делает вызывающий, здесь она продублирована.
    /// </summary>
    public sealed class CansOrderDebugBot : MonoBehaviour
    {
        private CansOrderMinigame game;
        private CanShelf shelf;
        private CanConfirmButton button;
        private PlayerController avatar;
        private System.Random random;
        private float chanceToSolve;

        private readonly List<int> arrangement = new List<int>(8);

        /// <summary>Круг, в котором болванка уже отработала. Дважды за круг не подтверждает.</summary>
        private int actedCircle = -1;

        /// <summary>Через сколько секунд после открытия окна нажать. Разброс — чтобы времена подтверждения различались.</summary>
        private float delay;
        private float elapsed;
        private bool armed;

        public void Bind(CansOrderMinigame minigame, CanShelf ownerShelf, CanConfirmButton ownerButton,
            PlayerController owner, int seed, float solveChance)
        {
            game = minigame;
            shelf = ownerShelf;
            button = ownerButton;
            avatar = owner;
            random = new System.Random(seed);
            chanceToSolve = Mathf.Clamp01(solveChance);
        }

        /// <summary>Снять болванку с игрока: матч кончился или он выбыл.</summary>
        public void Disarm()
        {
            armed = false;
            enabled = false;
        }

        private void Update()
        {
            if (game == null || button == null || shelf == null)
            {
                return;
            }

            if (!button.WindowOpen || button.Accepted || button.Solved)
            {
                armed = false;
                return;
            }

            if (!armed)
            {
                if (actedCircle == game.Round.Circle)
                {
                    return;
                }

                armed = true;
                elapsed = 0f;
                // Разброс внутри окна: одинаковые времена подтверждения дают
                // полное равенство, и правило «выбывают все на линии отсечения»
                // срабатывало бы каждый раунд.
                delay = (float)random.NextDouble() * Mathf.Max(0.1f, game.PlacementWindowSeconds * 0.6f);
                return;
            }

            elapsed += Time.deltaTime;
            if (elapsed < delay)
            {
                return;
            }

            armed = false;
            actedCircle = game.Round.Circle;
            Submit();
        }

        private void Submit()
        {
            bool solve = random.NextDouble() < chanceToSolve;
            if (!game.TryGetBotArrangement(solve, arrangement))
            {
                return;
            }

            shelf.SetArrangement(arrangement);
            button.Interact(avatar);
        }
    }
}
