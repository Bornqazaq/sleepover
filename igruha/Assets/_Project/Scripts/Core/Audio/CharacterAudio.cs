using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Звук персонажа помимо шагов — прыжок, приземление, падение, полученный удар.
    /// Общий слой фазы 5, работает во всех пятнадцати играх.
    ///
    /// <b>Всё висит на событиях контроллера цели, а не бьющего.</b> Это не стиль,
    /// а сетевое требование: нокдаун реплицирован, поэтому его слышат все, а удар,
    /// повешенный на замах бьющего, звучал бы только у бьющего — ровно тот случай,
    /// который правила проекта называют неверной привязкой.
    ///
    /// Своего состояния и своих RPC нет.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class CharacterAudio : MonoBehaviour
    {
        /// <summary>Скорость падения, ниже которой приземление не слышно: сход со ступеньки.</summary>
        private const float MinLandSpeed = 2.5f;

        /// <summary>Громкость толчка прыжка. Тише приземления — вверх отталкиваются мягче, чем падают.</summary>
        private const float JumpVolume = 0.55f;

        /// <summary>Скорость падения, на которой приземление звучит в полную силу.</summary>
        private const float FullLandSpeed = 9f;

        [Tooltip("Проигрыватель звука этого персонажа")]
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        [Tooltip("Контроллер персонажа. Не задан — берётся с этого же объекта")]
        [SerializeField] private PlayerController controller;

        /// <summary>
        /// В каком кадре персонаж приземлился. Жёсткое приземление роняет персонажа,
        /// то есть поднимает и <c>Landed</c>, и <c>KnockdownStarted</c> — без этой
        /// отметки на один удар об пол играло бы два разных звука.
        /// </summary>
        private int landedFrame = -1;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            if (controller == null) return;

            controller.Jumped += OnJumped;
            controller.Landed += OnLanded;
            controller.KnockdownStarted += OnKnockdownStarted;
        }

        private void OnDisable()
        {
            if (controller == null) return;

            controller.Jumped -= OnJumped;
            controller.Landed -= OnLanded;
            controller.KnockdownStarted -= OnKnockdownStarted;
        }

        private void OnJumped()
        {
            if (audioPlayer == null) return;
            audioPlayer.PlayAt(CoreSfx.JumpLand, controller.Position, JumpVolume);
        }

        private void OnLanded(float fallSpeed)
        {
            if (audioPlayer == null) return;
            landedFrame = Time.frameCount;

            // Лежачий коснулся пола телом, а не ногами: это падение, а не приземление.
            if (controller.IsKnockedDown)
            {
                audioPlayer.PlayAt(CoreSfx.Bodyfall, controller.Position);
                return;
            }

            float hardSpeed = controller.Config != null ? controller.Config.HardLandingSpeed : FullLandSpeed;
            if (fallSpeed >= hardSpeed)
            {
                audioPlayer.PlayAt(CoreSfx.Bodyfall, controller.Position);
                return;
            }

            if (fallSpeed < MinLandSpeed) return;

            float loudness = Mathf.InverseLerp(MinLandSpeed, FullLandSpeed, fallSpeed);
            audioPlayer.PlayAt(CoreSfx.JumpLand, controller.Position, Mathf.Lerp(0.5f, 1f, loudness));
        }

        private void OnKnockdownStarted(KnockdownType type)
        {
            if (audioPlayer == null) return;

            // Жёсткое приземление уже озвучено падением тела в этом же кадре.
            if (Time.frameCount == landedFrame) return;

            audioPlayer.PlayAt(CoreSfx.PushHit, controller.Position);
        }
    }
}
