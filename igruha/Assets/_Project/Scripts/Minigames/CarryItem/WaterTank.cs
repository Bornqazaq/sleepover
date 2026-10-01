using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Session;
using Igruha.Core.UI;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Бак команды: он же счёт. Уровень виден снаружи и читается с игровой
    /// камеры без наведения — ради этого интерфейс не нужен вовсе.
    ///
    /// Тележку подвезли к насосу — он постепенно откачивает воду.
    /// Отъехали на середине — остаток сохраняется в тележке.
    ///
    /// Ходка закрыта, когда тележка в зоне опустела. Тележка при этом никуда
    /// не исчезает — стоит пустая, и её надо укатить обратно к крану.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WaterTank : MonoBehaviour
    {
        [SerializeField] private CarryItemConfig config;
        [SerializeField] private Transform waterInlet;
        public Vector3 PourPoint => waterInlet != null ? waterInlet.position : transform.position;
        [SerializeField] private Transform cartDock;
        public Vector3 DockPoint => cartDock != null ? cartDock.position : transform.position;
        [Tooltip("Пивот воды у дна бака; WaterVolumeVisual сглаживает уровень")]
        [SerializeField] private Transform waterMesh;
        private WaterVolumeVisual waterVisual;
        [Tooltip("Что красится в цвет команды: обод бака и прочие метки принадлежности")]
        [SerializeField] private Renderer[] teamTint;
        [Tooltip("Зона слива — триггер вокруг бака. Пусто: возьмём триггер с этого объекта")]
        [SerializeField] private Collider pourZone;

        /// <summary>Команда долила порцию: сколько единиц и в какой момент общих часов.</summary>
        public event Action<int, double> Delivered;

        /// <summary>Тележка слита до дна — ходка закрыта.</summary>
        public event Action TripFinished;

        /// <summary>Тележки, фактически пересекающие зону на текущем шаге физики.</summary>
        private readonly List<WaterCart> insideZone = new List<WaterCart>(2);
        private readonly List<WaterCart> pouring = new List<WaterCart>(2);
        private Collider[] overlaps = new Collider[32];

        private TeamSide team = TeamSide.None;
        private MaterialPropertyBlock materialBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private int water;
        private float pourAccumulator;
        private int shownStep = -1;

        /// <summary>Воды в баке, единиц.</summary>
        public int Water => water;

        /// <summary>Чей бак.</summary>
        public TeamSide Team => team;

        private void Awake()
        {
            ResolvePourZone();
            materialBlock = new MaterialPropertyBlock();
            if (waterMesh != null) waterVisual = waterMesh.GetComponent<WaterVolumeVisual>();
        }

        /// <summary>
        /// Найти зону слива, не тронув тело бака.
        ///
        /// Здесь стояло <c>GetComponent&lt;Collider&gt;().isTrigger = true</c> —
        /// и брало это <b>первый</b> коллайдер объекта. А коллайдеров на баке
        /// два: сплошное тело, об которое игрок останавливается, и широкая
        /// зона слива вокруг него. Первым лежит тело — и код каждый запуск
        /// превращал его в триггер. Отсюда сразу три жалобы с прогона:
        /// сквозь бак можно пройти насквозь, донесённая тара его не
        /// наполняет и шкала воды не растёт.
        ///
        /// Берём явную ссылку, иначе — тот коллайдер, который уже размечен
        /// триггером. Триггера нет вовсе (старые сцены с одним коллайдером) —
        /// делаем триггером единственный, как раньше, но говорим об этом.
        /// </summary>
        private void ResolvePourZone()
        {
            if (pourZone != null)
            {
                pourZone.isTrigger = true;
                return;
            }

            var colliders = GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].isTrigger)
                {
                    pourZone = colliders[i];
                    return;
                }
            }

            if (colliders.Length == 1)
            {
                pourZone = colliders[0];
                pourZone.isTrigger = true;
                Debug.LogWarning($"{name}: у бака один коллайдер — он стал зоной слива, " +
                                 "и сквозь бак можно пройти. Добавь телу отдельный сплошной коллайдер", this);
                return;
            }

            Debug.LogError($"{name}: у бака нет коллайдера-триггера — зону слива брать неоткуда", this);
        }

        /// <summary>Подключить бак к команде. Зовут правила раунда на старте.</summary>
        public void Configure(CarryItemConfig gameConfig, TeamSide side)
        {
            config = gameConfig;
            team = side;
            water = 0;
            pourAccumulator = 0f;
            shownStep = -1;
            ReleasePouring();
            insideZone.Clear();
            ApplyLevelVisual();
            ApplyTeamTint();
        }

        /// <summary>
        /// Цвет команды на ободе. Баков на арене два, стоят они симметрично, и
        /// без цвета «свой» от «чужого» отличается только памятью игрока —
        /// а везти к чужому баку полную тележку очень обидно.
        /// </summary>
        private void ApplyTeamTint()
        {
            if (teamTint == null || teamTint.Length == 0)
            {
                return;
            }

            Color color = TeamPalette.ColorOf(team);

            for (int i = 0; i < teamTint.Length; i++)
            {
                Renderer target = teamTint[i];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(materialBlock);
                materialBlock.SetColor(BaseColorId, color);
                materialBlock.SetColor(ColorId, color);
                target.SetPropertyBlock(materialBlock);
            }
        }

        private void FixedUpdate()
        {
            if (config == null || !WorldAuthority.HasAuthority)
            {
                return;
            }

            RefreshCartsInZone();

            // Насос работает только пока тележка в зоне и с водой.
            // Выкатилась или опустела — откачка прекращается, и это состояние, а не
            // событие: клиент видит его флагом тележки.
            for (int i = pouring.Count - 1; i >= 0; i--)
            {
                WaterCart cart = pouring[i];
                if (cart == null || !insideZone.Contains(cart) || cart.Water <= 0 || water >= config.TankCapacity)
                {
                    if (cart != null)
                    {
                        cart.SetPouring(false);
                    }

                    pouring.RemoveAt(i);
                }
            }

            for (int i = insideZone.Count - 1; i >= 0; i--)
            {
                WaterCart cart = insideZone[i];
                if (cart == null || cart.IsLost || cart.ControlTeam != team || cart.Water <= 0 || water >= config.TankCapacity)
                {
                    continue;
                }

                // One hose, one vessel: retain the connected vessel until empty or gone.
                if (pouring.Count > 0 && !pouring.Contains(cart)) continue;
                if (!pouring.Contains(cart))
                {
                    pouring.Add(cart);
                    cart.SetPouring(true);
                }

                Pour(cart, Time.fixedDeltaTime);
                break;
            }
        }

        // Spatial eligibility is decided by the authoritative collider query below.
        // Presentation follows IsPouring, including a vessel only partly inside the zone.
        public bool CanReceive(WaterCart cart) => cart != null && !cart.IsLost && cart.ControlTeam == team;

        private void RefreshCartsInZone()
        {
            insideZone.Clear();
            if (pourZone == null || !pourZone.enabled || !pourZone.gameObject.activeInHierarchy)
            {
                pourAccumulator = 0f;
                return;
            }

            // Enter/Exit теряются на сбросе, выключении и телепорте. Спрашиваем
            // настоящую форму, включая спящие тела, у авторитета.
            Bounds bounds = pourZone.bounds;
            int count;
            while (true)
            {
                count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, overlaps,
                    Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                if (count < overlaps.Length) break;
                Array.Resize(ref overlaps, overlaps.Length * 2);
            }

            for (int i = 0; i < count; i++)
            {
                Collider other = overlaps[i];
                overlaps[i] = null;
                WaterCart cart = other.GetComponentInParent<WaterCart>();
                if (cart == null || cart.IsLost || cart.ControlTeam != team || insideZone.Contains(cart))
                    continue;

                // Границы — только грубый отбор: повёрнутая зона не должна
                // принимать тележку в пустых углах своего мирового AABB.
                if (Physics.ComputePenetration(pourZone, pourZone.transform.position, pourZone.transform.rotation,
                    other, other.transform.position, other.transform.rotation, out _, out _))
                    insideZone.Add(cart);
            }

            if (insideZone.Count == 0) pourAccumulator = 0f;
        }

        /// <summary>
        /// Показать уровень, решённый сервером. Бак — это счёт, и считает его
        /// только авторитет; остальным приезжает готовое число внутри
        /// <see cref="CarryItemState"/>.
        /// </summary>
        public void ApplyNetworkLevel(int value)
        {
            if (WorldAuthority.HasAuthority)
            {
                return;
            }

            water = Mathf.Max(0, value);
            ApplyLevelVisual();
        }

        /// <summary>
        /// Один шаг слива. Темп постоянный: полная тележка уходит за
        /// <c>cartCapacity / pourRate</c> секунд, полупустая — вдвое быстрее.
        /// </summary>
        private void Pour(WaterCart cart, float delta)
        {
            pourAccumulator += config.PourRate * delta;

            int whole = Mathf.FloorToInt(pourAccumulator);
            if (whole <= 0)
            {
                return;
            }

            pourAccumulator -= whole;

            int taken = -cart.ChangeWater(-Mathf.Min(whole, config.TankCapacity - water), WaterLossReason.Poured);
            if (taken <= 0)
            {
                return;
            }

            int before = water;
            water = Mathf.Min(config.TankCapacity, water + taken);
            ApplyLevelVisual();

            if (water != before)
            {
                Delivered?.Invoke(water - before, Igruha.Core.Minigame.NetworkClock.Now);
            }

            if (cart.Water <= 0)
            {
                FinishTrip(cart);
            }
        }

        /// <summary>Тележка слита до дна — ходка закрыта. Тележка остаётся стоять пустой.</summary>
        private void FinishTrip(WaterCart cart)
        {
            pourAccumulator = 0f;
            cart.SetPouring(false);
            pouring.Remove(cart);
            TripFinished?.Invoke();
        }

        /// <summary>Остановить откачку — конец раунда или сброс бака.</summary>
        private void ReleasePouring()
        {
            for (int i = 0; i < pouring.Count; i++)
            {
                if (pouring[i] != null)
                {
                    pouring[i].SetPouring(false);
                }
            }

            pouring.Clear();
        }

        private void OnDisable() => ReleasePouring();

        /// <summary>Уровень ступенями по пять единиц — как в тележке, тем же шагом.</summary>
        private void ApplyLevelVisual()
        {
            if (waterMesh == null || config == null)
            {
                return;
            }

            int step = Mathf.CeilToInt(water / (float)config.WaterStep);
            if (step == shownStep)
            {
                return;
            }

            shownStep = step;

            int totalSteps = Mathf.Max(1, config.TankCapacity / config.WaterStep);
            float fraction = Mathf.Clamp01(step / (float)totalSteps);

            if (waterVisual != null)
            {
                waterVisual.SetLevel(fraction);
                return;
            }

            Vector3 scale = waterMesh.localScale;
            scale.y = Mathf.Max(0.001f, fraction);
            waterMesh.localScale = scale;
        }
    }
}
