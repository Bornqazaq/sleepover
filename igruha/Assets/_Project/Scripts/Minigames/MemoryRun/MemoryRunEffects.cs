using UnityEngine;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>Pooled pressure effects driven by the server's detonation event.
    /// No lasting marks on plates: the route must be remembered by players.</summary>
    [DisallowMultipleComponent]
    public sealed class MemoryRunEffects : MonoBehaviour
    {
        [Tooltip("Игра, чьи взрывы отыгрываем. Пусто — найдётся на этом же объекте")]
        [SerializeField] private MemoryRunMinigame game;

        [Tooltip("Пул облаков взрыва. Заполняет билдер арены, руками не набивать")]
        [SerializeField] private ParticleSystem[] blasts;

        [Tooltip("На сколько облако поднято над поверхностью плиты: взрыв идёт из-под ног, а не из пола")]
        [SerializeField] private float blastLift = 0.55f;

        private int nextBlast;

        private void Awake()
        {
            if (game == null)
            {
                game = GetComponent<MemoryRunMinigame>();
            }
        }

        private void OnEnable()
        {
            if (game != null)
            {
                game.MineDetonated += OnMineDetonated;
            }
        }

        private void OnDisable()
        {
            if (game != null)
            {
                game.MineDetonated -= OnMineDetonated;
            }

        }

        /// <summary>
        /// Взрыв под ногами. Событие приходит на каждой машине, поэтому облако
        /// видят все восемь человек, а не только пострадавший, — ровно на этом
        /// игра и построена.
        /// </summary>
        private void OnMineDetonated(Vector3 center)
        {
            PlayBlast(center + Vector3.up * blastLift);
        }

        /// <summary>
        /// Отыграть облако из пула.
        ///
        /// Пул идёт по кругу, а не ищет свободный: восемь одновременных взрывов
        /// в этой игре невозможны — ходят строго по одному, — а перебор списка
        /// каждый раз был бы работой ради случая, которого не бывает.
        /// </summary>
        private void PlayBlast(Vector3 point)
        {
            if (blasts == null || blasts.Length == 0)
            {
                return;
            }

            ParticleSystem blast = blasts[nextBlast];
            nextBlast = (nextBlast + 1) % blasts.Length;
            if (blast == null)
            {
                return;
            }

            blast.transform.position = point;
            blast.Clear(true);
            blast.Play(true);
        }

    }
}
