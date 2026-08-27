using UnityEngine;
using Igruha.Core.Items;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Таран корпусом между несущими двух команд — единственный способ навредить
    /// сопернику, не бросая ручку.
    ///
    /// <b>Пропорция потерь 20 / 10 — самый важный параметр всей игры.</b>
    /// Поровну — таранить не будут; бесплатно — бросят носить и будут только
    /// таранить. Оба числа лежат в конфиге и крутятся на плейтесте.
    ///
    /// Живёт одним объектом на сцене, а не по одному на бутыль: кулдаун здесь
    /// общий <b>на пару команд</b>, а две копии на двух бутылях считали бы его
    /// каждая по-своему и списывали бы вдвое.
    ///
    /// Столкновение ищется перебором пар несущих, а не коллизиями: у бутыли
    /// в руках коллайдеры с несущими развязаны (иначе команда бульдозерит саму
    /// себя), а капсулы игроков сталкиваются постоянно и по любому поводу.
    /// Пар не больше шестнадцати, перебор дешевле, чем разбор чужих коллизий.
    /// </summary>
    public sealed class BottleRamDetector : MonoBehaviour
    {
        [SerializeField] private CarryItemConfig config;

        private WaterBottle bottleA;
        private WaterBottle bottleB;
        private float cooldownTimer;

        /// <summary>Подключить числа. Зовут правила раунда на старте.</summary>
        public void Configure(CarryItemConfig gameConfig)
        {
            config = gameConfig;
            cooldownTimer = 0f;
        }

        /// <summary>Какие бутыли сейчас в игре. Меняется каждую ходку — тару выдают заново.</summary>
        public void SetBottles(WaterBottle a, WaterBottle b)
        {
            bottleA = a;
            bottleB = b;
        }

        private void FixedUpdate()
        {
            if (config == null)
            {
                return;
            }

            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.fixedDeltaTime);

            // Кто атакующий — решает сервер: у клиента свои скорости и свой
            // порядок кадров, и он назначил бы атакующим кого угодно.
            if (!WorldAuthority.HasAuthority || cooldownTimer > 0f)
            {
                return;
            }

            if (bottleA == null || bottleB == null || bottleA.IsGone || bottleB.IsGone)
            {
                return;
            }

            MultiCarryObject carryA = bottleA.Carry;
            MultiCarryObject carryB = bottleB.Carry;

            // Таранить некого: если у соперника тару никто не держит, отнимать
            // у него нечего, а свою воду за наезд на пустое место не берут.
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

                    if (TryRam(left, carryA.CarrierBodyAt(i), right, carryB.CarrierBodyAt(j)))
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
        private bool TryRam(PlayerController left, Rigidbody leftBody, PlayerController right, Rigidbody rightBody)
        {
            Vector3 axis = right.transform.position - left.transform.position;
            axis.y = 0f;

            float gap = axis.magnitude;
            if (gap > config.RamContactDistance || gap < 0.0001f)
            {
                return false;
            }

            axis /= gap;

            Vector3 leftVelocity = HorizontalVelocity(leftBody);
            Vector3 rightVelocity = HorizontalVelocity(rightBody);

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
                bottleA.SpendWater(config.RamVictimLoss, WaterLossReason.RamVictim);
                bottleB.SpendWater(config.RamVictimLoss, WaterLossReason.RamVictim);
                return true;
            }

            bool leftAttacks = leftClosing > rightClosing;
            WaterBottle attackerBottle = leftAttacks ? bottleA : bottleB;
            WaterBottle victimBottle = leftAttacks ? bottleB : bottleA;

            victimBottle.SpendWater(config.RamVictimLoss, WaterLossReason.RamVictim);
            attackerBottle.SpendWater(config.RamAttackerLoss, WaterLossReason.RamAttacker);
            return true;
        }

        private static Vector3 HorizontalVelocity(Rigidbody body)
        {
            if (body == null)
            {
                return Vector3.zero;
            }

            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;
            return velocity;
        }
    }
}
