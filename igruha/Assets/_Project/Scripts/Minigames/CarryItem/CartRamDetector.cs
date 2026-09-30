using UnityEngine;
using Igruha.Core.Items;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Таран корпусом между несущими двух команд — единственный способ навредить
    /// сопернику, не бросая поручень.
    ///
    /// <b>Пропорция потерь 20 / 10 — самый важный параметр всей игры.</b>
    /// Поровну — таранить не будут; бесплатно — бросят возить и будут только
    /// таранить. Оба числа лежат в конфиге и крутятся на плейтесте.
    ///
    /// Живёт одним объектом на сцене, а не по одному на тележку: кулдаун здесь
    /// общий <b>на пару команд</b>, а две копии считали бы его каждая по-своему
    /// и списывали бы вдвое.
    ///
    /// Столкновение ищется перебором пар несущих, а не коллизиями: у тележки
    /// в руках коллайдеры с несущими развязаны (иначе команда бульдозерит саму
    /// себя), а капсулы игроков сталкиваются постоянно и по любому поводу.
    /// Пар не больше шестнадцати, перебор дешевле, чем разбор чужих коллизий.
    /// </summary>
    public sealed class CartRamDetector : MonoBehaviour
    {
        [SerializeField] private CarryItemConfig config;

        private WaterCart cartA;
        private WaterCart cartB;
        private float cooldownTimer;

        /// <summary>Подключить числа. Зовут правила раунда на старте.</summary>
        public void Configure(CarryItemConfig gameConfig)
        {
            config = gameConfig;
            cooldownTimer = 0f;
        }

        /// <summary>Тележки команд. Постоянны на весь раунд, но приезжают с сервера не в тот же кадр, что и старт.</summary>
        public void SetCarts(WaterCart a, WaterCart b)
        {
            cartA = a;
            cartB = b;
        }

        private void FixedUpdate()
        {
            // Кто атакующий — решает сервер: у клиента свои скорости и свой
            // порядок кадров, и он назначил бы атакующим кого угодно. Кулдаун
            // тикает там же: клиентский отсчёт всё равно ничего не решает.
            if (config == null || !WorldAuthority.HasAuthority)
            {
                return;
            }

            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.fixedDeltaTime);
            if (cooldownTimer > 0f)
            {
                return;
            }

            if (cartA == null || cartB == null || cartA.IsLost || cartB.IsLost)
            {
                return;
            }

            MultiCarryObject carryA = cartA.Carry;
            MultiCarryObject carryB = cartB.Carry;

            // Таранить некого: если у соперника тележку никто не держит,
            // отнимать у него нечего, а свою воду за наезд на пустое место не берут.
            if (carryA.CarrierCount == 0 || carryB.CarrierCount == 0)
            {
                return;
            }

            for (int i = 0; i < carryA.HandleCount; i++)
            {
                PlayerController left = carryA.CarrierAt(i);
                if (left == null)
                {
                    continue;
                }

                for (int j = 0; j < carryB.HandleCount; j++)
                {
                    PlayerController right = carryB.CarrierAt(j);
                    if (right == null)
                    {
                        continue;
                    }

                    // Скорости берём у самой переноски, а не у тел: чужую копию
                    // персонажа ведёт NetworkTransform, и linearVelocity на
                    // сервере у неё пустой. По телам таран засчитывался бы
                    // только между хостом и болванками — то есть никогда.
                    if (TryRam(left, carryA.CarrierVelocityAt(i), right, carryB.CarrierVelocityAt(j)))
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Проверить одну пару. Возвращает true — таран засчитан, дальше
        /// перебирать нечего: кулдаун общий, и второе столкновение в тот же
        /// такт всё равно ничего не даст.
        /// </summary>
        private bool TryRam(PlayerController left, Vector3 leftVelocity, PlayerController right, Vector3 rightVelocity)
        {
            Vector3 axis = right.transform.position - left.transform.position;
            axis.y = 0f;

            float gap = axis.magnitude;
            if (gap > config.RamContactDistance || gap < 0.0001f)
            {
                return false;
            }

            axis /= gap;

            // Проекция «в сторону соперника» у каждого своя: она и решает, кто
            // наехал, а кто попал под наезд.
            float leftClosing = Vector3.Dot(leftVelocity, axis);
            float rightClosing = Vector3.Dot(rightVelocity, -axis);

            if (leftClosing + rightClosing < config.MinRamSpeed)
            {
                return false;
            }

            cooldownTimer = config.RamCooldown;

            float strongest = Mathf.Max(Mathf.Abs(leftClosing), Mathf.Abs(rightClosing));
            bool headOn = strongest < 0.0001f ||
                          Mathf.Abs(leftClosing - rightClosing) / strongest < config.HeadOnTolerance;

            if (headOn)
            {
                // Лобовое: атакующего нет, обе команды платят одинаково.
                cartA.ChangeWater(-config.RamVictimLoss, WaterLossReason.RamVictim);
                cartB.ChangeWater(-config.RamVictimLoss, WaterLossReason.RamVictim);
                return true;
            }

            bool leftAttacks = leftClosing > rightClosing;
            WaterCart attackerCart = leftAttacks ? cartA : cartB;
            WaterCart victimCart = leftAttacks ? cartB : cartA;

            victimCart.ChangeWater(-config.RamVictimLoss, WaterLossReason.RamVictim);
            attackerCart.ChangeWater(-config.RamAttackerLoss, WaterLossReason.RamAttacker);
            return true;
        }
    }
}
