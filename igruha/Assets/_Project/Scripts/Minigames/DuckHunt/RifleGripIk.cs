using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Кладёт кисти Охотника на ружьё: правую — на шейку приклада с курком,
    /// левую — на цевьё.
    ///
    /// Почему это вообще нужно. Стойка приходит одним клипом
    /// (<c>Aza@Firing Rifle</c>) на весь ростер, а ретаргет раскладывает её по
    /// восьми разным телам — узкой Girl, широкому Fat, длиннорукому Shlanga.
    /// Кисти при этом расходятся: замеры дали расстояние между ними от 0.296 м
    /// у Shlanga до 0.577 м у Fat. Никакая привязка ружья к рукам такой разброс
    /// не покрывает — либо ствол уезжает вслед за руками, либо руки перестают
    /// его касаться. Поэтому задача решается наоборот: ружьё стоит по лучу
    /// выстрела, а руки приводит к нему IK.
    ///
    /// Компонент обязан висеть на том же объекте, что и <see cref="Animator"/>
    /// (у наших префабов это не корень, а дочерний узел модели), иначе Unity
    /// не вызовет <c>OnAnimatorIK</c>. Слою нужен включённый IK Pass —
    /// его ставит <c>HunterRifleLayerBuilder</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RifleGripIk : MonoBehaviour
    {
        private Animator animator;

        /// <summary>Куда встаёт правая кисть — шейка приклада у курка.</summary>
        public Transform GripPoint { get; set; }

        /// <summary>Куда встаёт левая кисть — цевьё.</summary>
        public Transform ForePoint { get; set; }

        /// <summary>
        /// Сила притяжения кистей, 0…1. Ноль полностью возвращает позу клипа,
        /// поэтому снимать роль достаточно обнулением веса — компонент можно
        /// не удалять.
        /// </summary>
        public float Weight { get; set; }

        private void Awake()
        {
            animator = GetComponent<Animator>();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || GripPoint == null || ForePoint == null)
            {
                return;
            }

            float weight = Mathf.Clamp01(Weight);

            // Позиция притягивается, поворот — нет. Кисти клипа уже сложены
            // под ружьё, и перебивать их разворот значило бы подбирать восемь
            // наборов углов ровно там, откуда эту задачу и убирали.
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
            animator.SetIKPosition(AvatarIKGoal.RightHand, GripPoint.position);

            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, weight);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, ForePoint.position);
        }
    }
}
