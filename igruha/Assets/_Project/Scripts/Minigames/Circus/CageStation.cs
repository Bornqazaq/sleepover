using System;
using System.Collections;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>
    /// Одна клетка на цепи. Главный носитель напряжения: весь статус игрока
    /// читается по высоте клетки, без единой цифры на экране.
    ///
    /// Про содержимое слота реквизита не знает ничего — туда встаёт кнопка
    /// «Секундомера» или полка с банками «Порядка банок». Про правила игры
    /// тоже не знает: ей говорят «спустись на ступень» и «открой дно».
    ///
    /// Уровень задаётся числом ступеней над нижней, а высота считается формулой
    /// из <see cref="CircusArenaConfig.GetCageBottomHeight"/>. Правила игры
    /// считают ступень как «лимит ошибок − сделано ошибок», поэтому последняя
    /// ступень перед вылетом — всегда нижняя, при любом лимите.
    /// </summary>
    [RequireComponent(typeof(RidePlatform))]
    public sealed class CageStation : MonoBehaviour
    {
        [Tooltip("Створки дна: петли по внешним краям, распахиваются вниз")]
        [SerializeField] private Transform doorLeft;
        [SerializeField] private Transform doorRight;
        [Tooltip("Слот реквизита в центре клетки")]
        [SerializeField] private Transform propSlot;
        [Tooltip("Точка респавна внутри клетки — едет вниз вместе с ней")]
        [SerializeField] private Transform respawnPoint;
        [Tooltip("На сколько градусов распахиваются створки")]
        [SerializeField] private float doorOpenAngle = 110f;
        [Tooltip("С какого угла створки перестают держать игрока. Раньше — он съезжает по наклонной и его подбрасывает")]
        [SerializeField] private float doorReleaseAngle = 25f;

        /// <summary>Клетка приехала на заданный уровень.</summary>
        public event Action<CageStation> Arrived;

        /// <summary>Створки распахнулись — пассажир полетел вниз.</summary>
        public event Action<CageStation> DoorsOpened;

        private RidePlatform platform;
        private CircusArenaConfig config;
        private Collider[] doorColliders = Array.Empty<Collider>();
        private Coroutine doorRoutine;
        private PlayerController occupant;
        private Transform previousRespawnPoint;
        private bool lockedByDescent;

        /// <summary>Ступеней над нижней. Ноль — последняя ступень перед вылетом.</summary>
        public int Level { get; private set; }

        public bool DoorsOpen { get; private set; }

        public bool Descending => platform != null && platform.Moving;

        public PlayerController Occupant => occupant;

        /// <summary>Куда «Секундомер» ставит кнопку, а «Порядок банок» — полку с банками.</summary>
        public Transform PropSlot => propSlot;

        private void Awake()
        {
            platform = GetComponent<RidePlatform>();
            platform.Mode = RidePlatform.DriveMode.Scripted;
            platform.Arrived += HandleArrived;
            CacheDoorColliders();
        }

        private void OnDestroy()
        {
            if (platform != null)
            {
                platform.Arrived -= HandleArrived;
            }
        }

        private void CacheDoorColliders()
        {
            var left = doorLeft != null ? doorLeft.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
            var right = doorRight != null ? doorRight.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
            doorColliders = new Collider[left.Length + right.Length];
            left.CopyTo(doorColliders, 0);
            right.CopyTo(doorColliders, left.Length);
        }

        /// <summary>
        /// Подготовить клетку к матчу: границы хода — от нижней ступени
        /// до верхней, клетка встаёт на верхнюю.
        /// </summary>
        public void Configure(CircusArenaConfig arenaConfig, int errorLimit)
        {
            config = arenaConfig;
            if (config == null)
            {
                Debug.LogError($"{name}: не задан CircusArenaConfig — уровни считать нечем", this);
                return;
            }

            platform.SetLimits(config.GetCageBottomHeight(0), config.GetCageBottomHeight(errorLimit));
            CloseDoors();
            SnapToLevel(errorLimit);
        }

        /// <summary>Кто сидит в этой клетке. Ставит точку респавна внутрь клетки.</summary>
        public void SetOccupant(PlayerController player)
        {
            occupant = player;
            platform.SetPassenger(player);

            if (player == null || respawnPoint == null)
            {
                return;
            }

            // Респавн внутри клетки: застрявший вернётся в центр, а не улетит
            // на стартовую точку арены. Точка — ребёнок клетки, поэтому едет
            // вниз вместе с ней сама собой.
            if (player.TryGetComponent(out PlayerRespawner respawner))
            {
                previousRespawnPoint = respawner.RespawnPoint;
                respawner.SetRespawnPoint(respawnPoint);
            }
        }

        public void SnapToLevel(int levelSteps)
        {
            Level = Mathf.Max(0, levelSteps);
            platform.SnapTo(config.GetCageBottomHeight(Level));
        }

        /// <summary>
        /// Спуститься на заданную ступень. Управление на время спуска отключено:
        /// две секунды игрок только смотрит, как медведь становится ближе, —
        /// это и есть смешное, а не помеха.
        /// </summary>
        public void DescendTo(int levelSteps, float duration)
        {
            if (config == null)
            {
                return;
            }

            Level = Mathf.Max(0, levelSteps);
            LockOccupant(true);
            platform.MoveTo(config.GetCageBottomHeight(Level), duration);
        }

        /// <summary>
        /// Тот же спуск, но отсчитанный от общего момента. По сети клетка обязана
        /// быть на одной высоте у всех, а команда на разные машины приходит
        /// не одновременно: считая от момента, а не от своего первого кадра,
        /// опоздавшая машина сразу встаёт на верную высоту.
        /// </summary>
        public void DescendTo(int levelSteps, float duration, double startTime)
        {
            if (config == null)
            {
                return;
            }

            Level = Mathf.Max(0, levelSteps);
            LockOccupant(true);
            platform.MoveTo(config.GetCageBottomHeight(Level), duration, startTime);
        }

        private void HandleArrived()
        {
            LockOccupant(false);
            Arrived?.Invoke(this);
        }

        /// <summary>
        /// Распахнуть дно. Клетка при этом не разрушается и не падает — она
        /// остаётся висеть пустой на своём уровне, и это единственный смысл
        /// пустой клетки на арене: отсюда уже кто-то выпал.
        /// </summary>
        public void OpenDoors(float duration)
        {
            if (DoorsOpen)
            {
                return;
            }

            DoorsOpen = true;
            if (doorRoutine != null)
            {
                StopCoroutine(doorRoutine);
            }

            doorRoutine = StartCoroutine(SwingDoors(duration));
        }

        public void CloseDoors()
        {
            if (doorRoutine != null)
            {
                StopCoroutine(doorRoutine);
                doorRoutine = null;
            }

            DoorsOpen = false;
            SetDoorAngle(0f);
            SetDoorCollidersEnabled(true);
        }

        private IEnumerator SwingDoors(float duration)
        {
            float elapsed = 0f;
            float span = Mathf.Max(0.01f, duration);
            bool released = false;

            while (elapsed < span)
            {
                elapsed += Time.deltaTime;
                float angle = Mathf.Lerp(0f, doorOpenAngle, elapsed / span);
                SetDoorAngle(angle);

                // Коллайдеры снимаются, как только створки заметно наклонились:
                // дальше игрок съезжал бы по наклонной плоскости, и вращающийся
                // коллайдер подбрасывал бы его вбок вместо падения вниз.
                if (!released && angle >= doorReleaseAngle)
                {
                    released = true;
                    SetDoorCollidersEnabled(false);
                    DoorsOpened?.Invoke(this);
                }

                yield return null;
            }

            SetDoorAngle(doorOpenAngle);
            if (!released)
            {
                SetDoorCollidersEnabled(false);
                DoorsOpened?.Invoke(this);
            }

            doorRoutine = null;
        }

        private void SetDoorAngle(float angle)
        {
            if (doorLeft != null)
            {
                doorLeft.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }

            if (doorRight != null)
            {
                doorRight.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private void SetDoorCollidersEnabled(bool value)
        {
            for (int i = 0; i < doorColliders.Length; i++)
            {
                if (doorColliders[i] != null)
                {
                    doorColliders[i].enabled = value;
                }
            }
        }

        private void LockOccupant(bool locked)
        {
            if (occupant == null)
            {
                return;
            }

            // Клетка снимает ровно ту блокировку, которую сама поставила:
            // если игрока держит что-то ещё, чужое состояние трогать нельзя.
            if (locked)
            {
                lockedByDescent = true;
                occupant.MovementLocked = true;
                return;
            }

            if (!lockedByDescent)
            {
                return;
            }

            lockedByDescent = false;
            occupant.MovementLocked = false;
        }

        /// <summary>
        /// Отпустить игрока: снять блокировку и вернуть точку респавна.
        /// Зовётся в конце раунда — персонаж переезжает между сценами живым,
        /// и незакрытая блокировка уедет в хаб вместе с ним.
        /// </summary>
        public void ReleaseOccupant()
        {
            LockOccupant(false);

            if (occupant != null && occupant.TryGetComponent(out PlayerRespawner respawner))
            {
                respawner.SetRespawnPoint(previousRespawnPoint);
            }

            occupant = null;
            platform.SetPassenger(null);
        }
    }
}
