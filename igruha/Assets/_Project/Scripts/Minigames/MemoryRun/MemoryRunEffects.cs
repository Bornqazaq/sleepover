using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>
    /// Эффекты «Рейса на память» — подфаза 4.4.
    ///
    /// <b>Своего состояния и своих RPC здесь нет.</b> Компонент висит на
    /// событии <see cref="MemoryRunMinigame.MineDetonated"/>, которое игра уже
    /// поднимает на каждой машине матча и которое несёт точку взрыва. Ничего
    /// сетевого доописывать не понадобилось, и <c>Core/</c> эта подфаза не
    /// трогает вовсе.
    ///
    /// 🔴 <b>На плите после взрыва не остаётся ничего.</b> Ни копоти, ни
    /// подпалины, ни следа, ни временной смены материала — маршрут не
    /// расчищается по ходу игры, вся информация живёт только в головах игроков.
    /// Поэтому эффект и не является ребёнком плиты: он живёт в своём пуле на
    /// арене, прилетает в точку, отыгрывает и гаснет, а плиту не касается ни
    /// иерархией, ни материалом.
    ///
    /// Копоть — <b>только на лице персонажа</b>, и снимается вместе с респавном.
    /// Префабы персонажей при этом не трогаются: копоть цепляется к кости
    /// головы в рантайме и удаляется по таймеру (`igruha/CLAUDE.md`, раздел 0).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MemoryRunEffects : MonoBehaviour
    {
        [Tooltip("Игра, чьи взрывы отыгрываем. Пусто — найдётся на этом же объекте")]
        [SerializeField] private MemoryRunMinigame game;

        [Tooltip("Пул облаков взрыва. Заполняет билдер арены, руками не набивать")]
        [SerializeField] private ParticleSystem[] blasts;

        [Tooltip("Выключенный образец копоти: его копия цепляется к голове погибшего")]
        [SerializeField] private GameObject sootTemplate;

        [Tooltip("Сколько копоть держится на лице. Ровно до возврата в стартовую зону")]
        [SerializeField] private float sootSeconds = 2.4f;

        [Tooltip("На сколько облако поднято над поверхностью плиты: взрыв идёт из-под ног, а не из пола")]
        [SerializeField] private float blastLift = 0.55f;

        private int nextBlast;
        private GameObject soot;
        private float sootClearsAt;

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

            ClearSoot();
        }

        private void Update()
        {
            if (soot != null && Time.time >= sootClearsAt)
            {
                ClearSoot();
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
            AttachSoot(game != null ? game.CurrentWalker : null);
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

        /// <summary>
        /// Посадить копоть на голову погибшего.
        ///
        /// Кость головы берётся у гуманоидного аниматора; если рига нет,
        /// копоть садится над капсулой. Второй путь существует не для красоты:
        /// молча пропущенная копоть выглядит как «эффекта не сделали», а не
        /// как «риг оказался другим».
        /// </summary>
        private void AttachSoot(PlayerController victim)
        {
            ClearSoot();

            if (sootTemplate == null || victim == null)
            {
                return;
            }

            Transform anchor = FindHead(victim);
            if (anchor == null)
            {
                return;
            }

            soot = Instantiate(sootTemplate, anchor);
            soot.transform.localPosition = Vector3.zero;
            soot.transform.localRotation = Quaternion.identity;
            soot.SetActive(true);
            sootClearsAt = Time.time + sootSeconds;
        }

        private static Transform FindHead(PlayerController victim)
        {
            var animator = victim.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    return head;
                }
            }

            return victim.transform;
        }

        private void ClearSoot()
        {
            if (soot == null)
            {
                return;
            }

            Destroy(soot);
            soot = null;
        }
    }
}
