using Igruha.Core.Traps;
using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Видимая и слышимая отдача ловушки: сноп сена на летних этажах, снежная
    /// струя на зимних, облако пыли у провала.
    ///
    /// Спека (9.6) не требует отдельного UI — нажавший видит зону эффекта
    /// собственными глазами. Но увидеть там до сих пор было нечего: площадка
    /// гейзера просто подбрасывала, а провал просто исчезал, без единого кадра
    /// на то, что вообще что-то произошло.
    ///
    /// <b>Почему через событие, а не через сеть.</b> <see cref="TrapBase.Fired"/>
    /// поднимается на КАЖДОЙ машине: сервер решает срабатывание сам, а клиентам
    /// его приносит <c>ApplyNetworkTrapFired</c>, который дёргает
    /// <c>PlayFired</c>. Значит эффекту не нужен ни свой RPC, ни своё состояние —
    /// он локальный и повторяет то, что уже синхронизировано.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DuckHuntTrapEffects : MonoBehaviour
    {
        [Tooltip("Ловушка, чьё срабатывание отыгрывается. Обычно лежит на этом же объекте")]
        [SerializeField] private TrapBase trap;

        [Tooltip("Партикл-система эффекта. Проигрывается разово на каждое срабатывание")]
        [SerializeField] private ParticleSystem burst;

        [Tooltip("Звук срабатывания. Не обязателен: без клипа эффект остаётся немым, но не ломается")]
        [SerializeField] private AudioSource sound;

        private void Awake()
        {
            if (trap == null)
            {
                TryGetComponent(out trap);
            }
        }

        private void OnEnable()
        {
            if (trap != null)
            {
                trap.Fired += OnFired;
            }
        }

        private void OnDisable()
        {
            if (trap != null)
            {
                trap.Fired -= OnFired;
            }
        }

        private void OnFired()
        {
            if (burst != null)
            {
                // Stop с очисткой, а не Play поверх: две подряд сработки за
                // время жизни частиц накладывались бы друг на друга, и второй
                // сноп выходил бы гуще первого.
                burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                burst.Play(true);
            }

            if (sound != null && sound.clip != null)
            {
                sound.Play();
            }
        }
    }
}
