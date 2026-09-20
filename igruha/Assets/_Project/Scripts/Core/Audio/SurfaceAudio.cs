using UnityEngine;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Метка поверхности на геометрии пола: по ней шаг узнаёт, чем звучать.
    ///
    /// Висит на объекте с коллайдером или на любом его родителе — метка ищется
    /// вверх по иерархии, чтобы целый павильон помечался одним компонентом,
    /// а не каждой доской по отдельности.
    ///
    /// <b>Почему метка, а не слой или тег.</b> Слои в проекте заняты физикой и
    /// маской препятствий камеры (<c>Ground</c>, <c>Cover</c>, <c>PlayerBarrier</c>),
    /// и делить их ещё и по материалу нельзя: камера перестанет видеть стены.
    /// Теги — одна строка на объект, а поверхности нужны рядом с другими пометками.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceAudio : MonoBehaviour
    {
        [Tooltip("Чем звучит шаг по этой поверхности")]
        [SerializeField] private SurfaceKind kind = SurfaceKind.Concrete;

        [Tooltip("Слот шага в обход поверхности: каменный зал ангелов, мокрый пол. Пусто — слот берётся по поверхности")]
        [SerializeField] private string stepSlotOverride;

        /// <summary>Чем звучит шаг по этой поверхности.</summary>
        public SurfaceKind Kind => kind;

        /// <summary>Слот шага для этой поверхности — свой, если задан, иначе по материалу.</summary>
        public string StepSlot => string.IsNullOrEmpty(stepSlotOverride) ? CoreSfx.Step(kind) : stepSlotOverride;

        /// <summary>
        /// Слот шага для коллайдера, на котором стоит персонаж.
        /// Без метки — бетон: в блокауте пол серый и бетонный, и это честный звук
        /// по умолчанию, а не молчание.
        /// </summary>
        public static string ResolveStepSlot(Collider ground, SurfaceKind fallback)
        {
            if (ground == null) return CoreSfx.Step(fallback);

            SurfaceAudio marker = ground.GetComponentInParent<SurfaceAudio>();
            return marker != null ? marker.StepSlot : CoreSfx.Step(fallback);
        }
    }
}
