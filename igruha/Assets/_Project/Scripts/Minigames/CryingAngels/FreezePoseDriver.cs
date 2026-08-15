using UnityEngine;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Показывает заморозку: персонаж застывает нелепым стоп-кадром.
    /// Отдельный компонент от RunnerState, потому что состояние — это правило
    /// (оно поедет в сеть), а стоп-кадр — картинка. В сетевой фазе состояние
    /// приедет с сервера, а этот компонент отработает у всех одинаково.
    ///
    /// Отдельных клипов поз пока нет — их завозит арт-фаза (IGR-292). До тех пор
    /// поза это остановленный в случайной точке клип: выглядит нелепо ровно так,
    /// как и задумано, и позволяет щупать механику уже на каркасе.
    /// </summary>
    [RequireComponent(typeof(RunnerState))]
    public sealed class FreezePoseDriver : MonoBehaviour
    {
        private RunnerState state;
        private Animator animator;

        private void Awake()
        {
            state = GetComponent<RunnerState>();
            animator = GetComponentInChildren<Animator>(true);
        }

        private void OnEnable()
        {
            state.Changed += OnStateChanged;
            OnStateChanged(state.Current);
        }

        private void OnDisable()
        {
            state.Changed -= OnStateChanged;
            Resume();
        }

        private void OnStateChanged(RunnerState.Phase phase)
        {
            if (animator == null)
            {
                return;
            }

            // Окаменение тоже держит стоп-кадр: игрок уже не управляется,
            // и живая анимация бега на замершем теле читается как баг.
            if (phase == RunnerState.Phase.Free)
            {
                Resume();
                return;
            }

            Freeze();
        }

        private void Freeze()
        {
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(current.fullPathHash, 0, state.FreezePoseTime);
            animator.Update(0f);
            animator.speed = 0f;
        }

        private void Resume()
        {
            if (animator != null)
            {
                animator.speed = 1f;
            }
        }
    }
}
