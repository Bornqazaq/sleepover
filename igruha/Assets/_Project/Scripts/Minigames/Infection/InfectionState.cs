using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.Infection
{
    /// <summary>Фаза заражения одного игрока. Байтом — чтобы в сетевой фазе лечь в список без переделки.</summary>
    public enum InfectionPhase : byte
    {
        /// <summary>Бежит и надеется.</summary>
        Clean = 0,

        /// <summary>Переходное: всплеск краски, управления нет, заражать и заражаться не может.</summary>
        Infecting = 1,

        /// <summary>Зелёный до конца раунда.</summary>
        Infected = 2
    }

    /// <summary>
    /// Состояние одного игрока в раунде «Заражения»: фаза, чистое время,
    /// счётчик личных заражений, таймеры грейса и кулдауна.
    ///
    /// Вешается на аватар в рантайме — как <c>RunnerState</c> у «Плачущих
    /// ангелов» и по той же причине: класть роль одной мини-игры в общий
    /// <c>Player.prefab</c> значит везти её во все остальные.
    ///
    /// <b>Все переходы — через методы этого класса, а не присваиванием полей.</b>
    /// В фазе 3 ровно эти методы уйдут за <c>IsServer</c>, а поля переедут в
    /// <c>NetworkList</c>: снаружи не изменится ничего.
    ///
    /// <b>Всё, что вешается на персонажа, снимается в <see cref="ResetRole"/>.</b>
    /// Аватар переезжает между сценами живым, и незакрытая блокировка или
    /// потолок скорости уедут с ним в хаб.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InfectionState : MonoBehaviour
    {
        private InfectionConfig config;
        private InfectionPaintView paint;
        private float graceTimer;
        private float cooldownTimer;
        private float infectingTimer;

        /// <summary>Номер игрока в сессии. По нему считаются места.</summary>
        public int PlayerId { get; private set; } = -1;

        public PlayerController Avatar { get; private set; }

        public InfectionPhase Phase { get; private set; } = InfectionPhase.Clean;

        /// <summary>С него всё началось. Нужно для компенсации очков и строки на табло.</summary>
        public bool IsPatientZero { get; private set; }

        /// <summary>Сколько секунд прожил чистым. Растёт только у чистого и только в раунде.</summary>
        public float CleanSeconds { get; private set; }

        /// <summary>Сколько игроков заразил лично. Каждое — бонус к очкам.</summary>
        public int PersonalInfections { get; private set; }

        /// <summary>Мигает и пока не заражает.</summary>
        public bool InGrace => graceTimer > 0f;

        /// <summary>Может заразить чистого прямо сейчас.</summary>
        public bool CanInfect => Phase == InfectionPhase.Infected && graceTimer <= 0f && cooldownTimer <= 0f;

        /// <summary>Может быть заражён: только чистый, переходное состояние неуязвимо.</summary>
        public bool CanBeInfected => Phase == InfectionPhase.Clean;

        /// <summary>Позиция капсулы. По ней меряется касание.</summary>
        public Vector3 Position => Avatar != null ? Avatar.Position : transform.position;

        public void Bind(int playerId, PlayerController avatar, InfectionConfig gameConfig, InfectionPaintView paintView)
        {
            PlayerId = playerId;
            Avatar = avatar;
            config = gameConfig;
            paint = paintView;
            SetClean();
        }

        /// <summary>Начало раунда: все чистые, счётчики на ноль.</summary>
        public void SetClean()
        {
            Phase = InfectionPhase.Clean;
            IsPatientZero = false;
            CleanSeconds = 0f;
            PersonalInfections = 0;
            graceTimer = 0f;
            cooldownTimer = 0f;
            infectingTimer = 0f;

            ClearMovementLock();
            ClearSpeedCap();
            paint?.SetInfected(false);
            paint?.SetBlinking(false);
        }

        /// <summary>
        /// Назначение Нулевого: сразу зелёный, но грейс держит его руки —
        /// две секунды он мигает, и все успевают понять, от кого бежать.
        /// </summary>
        public void MakePatientZero()
        {
            IsPatientZero = true;
            Phase = InfectionPhase.Infected;
            graceTimer = config != null ? config.GraceSeconds : 2f;
            cooldownTimer = 0f;
            infectingTimer = 0f;

            ApplyInfectedSpeed();
            paint?.SetInfected(true);
            paint?.SetBlinking(true);
        }

        /// <summary>
        /// Касание засчитано: всплеск краски, управление отнято на
        /// <see cref="InfectionConfig.InfectingSeconds"/>. В этом состоянии
        /// игрок не заражает и не заражается — только так в толпе не срабатывает
        /// цепочка заражений за один кадр.
        /// </summary>
        public void BeginInfecting()
        {
            if (Phase != InfectionPhase.Clean)
            {
                return;
            }

            Phase = InfectionPhase.Infecting;
            infectingTimer = config != null ? config.InfectingSeconds : 0.7f;

            if (Avatar != null)
            {
                Avatar.MovementLocked = true;
            }

            paint?.SetInfected(true);
        }

        /// <summary>Краска высохла: с этого мига заражает сам, но секунду не трогает того, кто его пятнал.</summary>
        public void CompleteInfection()
        {
            Phase = InfectionPhase.Infected;
            infectingTimer = 0f;
            cooldownTimer = config != null ? config.FreshInfectionCooldown : 1f;

            ClearMovementLock();
            ApplyInfectedSpeed();
            paint?.SetInfected(true);
            paint?.SetBlinking(false);
        }

        /// <summary>Засчитать личное заражение: +бонус к очкам.</summary>
        public void CountInfection() => PersonalInfections++;

        /// <summary>
        /// Такт раунда. Зовёт только авторитет: здесь идут и чистое время, и
        /// переходы состояний, а они — исход раунда.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (graceTimer > 0f)
            {
                graceTimer -= deltaTime;
                if (graceTimer <= 0f)
                {
                    graceTimer = 0f;
                    paint?.SetBlinking(false);
                }
            }

            if (cooldownTimer > 0f)
            {
                cooldownTimer = Mathf.Max(0f, cooldownTimer - deltaTime);
            }

            switch (Phase)
            {
                case InfectionPhase.Clean:
                    CleanSeconds += deltaTime;
                    break;

                case InfectionPhase.Infecting:
                    infectingTimer -= deltaTime;
                    if (infectingTimer <= 0f)
                    {
                        CompleteInfection();
                    }

                    break;
            }
        }

        /// <summary>
        /// Конец раунда: снять с персонажа всё, что повесила мини-игра.
        /// Игрок в переходном состоянии на финальном свистке считается
        /// заражённым — так записано в спеке, и так же считает подсчёт очков.
        /// </summary>
        public void ResetRole()
        {
            if (Phase == InfectionPhase.Infecting)
            {
                Phase = InfectionPhase.Infected;
            }

            graceTimer = 0f;
            cooldownTimer = 0f;
            infectingTimer = 0f;

            ClearMovementLock();
            ClearSpeedCap();
            paint?.SetBlinking(false);
        }

        private void ApplyInfectedSpeed()
        {
            if (Avatar == null || Avatar.Config == null || config == null)
            {
                return;
            }

            Avatar.ApplySpeedCap(this, Avatar.Config.MaxSpeed * config.InfectedSpeedMultiplier);
        }

        private void ClearSpeedCap()
        {
            if (Avatar != null)
            {
                Avatar.ClearSpeedCap(this);
            }
        }

        private void ClearMovementLock()
        {
            if (Avatar != null)
            {
                Avatar.MovementLocked = false;
            }
        }

        /// <summary>
        /// Игрок вышел из матча. По спеке он считается заражённым на момент
        /// выхода: очки замирают, место ему всё равно нужно, а бонус за него
        /// не получает никто — заразил его не игрок, а интернет.
        /// </summary>
        public void MarkLeftRound()
        {
            ResetRole();
            Phase = InfectionPhase.Infected;
            Avatar = null;
            paint = null;
        }

        private void OnDestroy() => ResetRole();
    }
}
