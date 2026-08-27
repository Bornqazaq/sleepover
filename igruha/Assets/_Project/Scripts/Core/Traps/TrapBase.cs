using System;
using UnityEngine;
using Igruha.Core.Session;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// База ловушки: Activate — публичная точка входа, кулдаун общий.
    /// Конкретные ловушки реализуют OnActivated.
    ///
    /// Срабатывание — исход раунда, поэтому решает его только сервер.
    /// Намерение игрока доезжает сюда через серверное взаимодействие
    /// (PlayerInteractor → сервер → TrapActivationButton.Interact).
    ///
    /// Ловушки, которые держат состояние (закрытая створка, убранный пол),
    /// отдают его наружу парой <see cref="IsSprung"/> / <see cref="ApplySprung"/>:
    /// сетевая половина мини-игры реплицирует его одним общим способом и не
    /// знает, какая именно ловушка перед ней.
    /// </summary>
    public abstract class TrapBase : MonoBehaviour
    {
        [Tooltip("Кулдаун повторной активации, с")]
        [SerializeField] private float cooldown = 3f;

        /// <summary>
        /// Ловушка снова готова (true) или ушла на кулдаун (false).
        /// Кнопке это нужно, чтобы показывать игроку готовность: без индикации
        /// нажатие превращается в лотерею — жмущий не знает, сработает ли.
        /// </summary>
        public event Action<bool> ReadyChanged;

        /// <summary>
        /// Ловушка сработала прямо сейчас — под звук, вспышку и тряску.
        ///
        /// Отдельно от состояния, и это не дублирование. Состояние отвечает
        /// на «закрыта ли дверь», а событие — на «в этот миг захлопнулась»,
        /// и у мгновенных ловушек второе есть, а первого нет вовсе: гейзер
        /// подбрасывает и в тот же кадр снова свободен. Без этого события
        /// остальные машины про его срабатывание не узнают никогда.
        /// </summary>
        public event Action Fired;

        private float cooldownTimer;

        /// <summary>
        /// На клиенте всегда true: кулдаун тикает только у авторитета, и клиент
        /// про него не знает. Это подсказка для UI, а не решение — отказ по
        /// кулдауну выносит сервер в <see cref="Activate"/>.
        /// </summary>
        public bool IsReady => cooldownTimer <= 0f;

        /// <summary>Сколько осталось до готовности, с. Ноль — готова.</summary>
        public float CooldownRemaining => cooldownTimer;

        /// <summary>
        /// Ловушка сейчас в сработавшем состоянии: створка закрыта, участок пола
        /// убран. У мгновенных ловушек (гейзер, падающий ящик) состояния нет —
        /// они отыгрывают эффект и в тот же миг снова свободны, поэтому здесь
        /// умолчание, а не абстрактный член.
        /// </summary>
        public virtual bool IsSprung => false;

        /// <summary>Применить состояние, решённое сервером. У мгновенных ловушек применять нечего.</summary>
        public virtual void ApplySprung(bool sprung) { }

        protected virtual void Update()
        {
            // Обе проверки обязательны. Авторитет — потому что кулдаун тикает
            // только у сервера. Нулевой таймер — потому что иначе строка ниже
            // каждый кадр объявляет ловушку снова готовой, и кнопка мигает
            // событием ReadyChanged бесконечно.
            if (!WorldAuthority.HasAuthority || cooldownTimer <= 0f)
            {
                return;
            }

            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.deltaTime);
            if (cooldownTimer <= 0f)
            {
                ReadyChanged?.Invoke(true);
            }
        }

        public void Activate()
        {
            // Клиент сюда попасть может — например, своим локальным нажатием
            // до того, как намерение уйдёт на сервер. Молча выходим: сработает
            // серверная копия, и её результат приедет всем.
            if (!WorldAuthority.HasAuthority)
            {
                return;
            }

            if (!IsReady)
            {
                return;
            }

            cooldownTimer = cooldown;

            // Ловушка без кулдауна не бывает «не готова» ни на кадр. Объявить
            // её занятой всё равно означало бы соврать: вернуть готовность
            // некому — обратное событие шлёт отсчёт, а его нет.
            if (cooldownTimer > 0f)
            {
                ReadyChanged?.Invoke(false);
            }

            OnActivated();
            Fired?.Invoke();
        }

        /// <summary>
        /// Отыграть срабатывание, ничего не решая. Зовут машины, которые исход
        /// не считали: сервер объявил, что ловушка сработала, — им остаётся
        /// только показать это.
        /// </summary>
        public void PlayFired() => Fired?.Invoke();

        /// <summary>Снять кулдаун и вернуть ловушку в исходное состояние. Для старта раунда.</summary>
        public virtual void ResetTrap()
        {
            bool wasBusy = cooldownTimer > 0f;
            cooldownTimer = 0f;

            if (wasBusy)
            {
                ReadyChanged?.Invoke(true);
            }
        }

        protected abstract void OnActivated();
    }
}
