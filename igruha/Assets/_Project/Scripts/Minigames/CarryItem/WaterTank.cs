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
    /// Бутыль внесли в зону — остаток перетекает сам. Перетекает <b>пока она в
    /// зоне</b>: выдернули на середине — перелилось только то, что успело, и
    /// остаток остался в бутыли. Это не придирка, а рабочий приём: под обстрелом
    /// у бака выгоднее слить половину и убежать, чем стоять полтора такта.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WaterTank : MonoBehaviour
    {
        [SerializeField] private CarryItemConfig config;
        [Tooltip("Меш воды в баке. Растягивается по уровню ступенями")]
        [SerializeField] private Transform waterMesh;
        [Tooltip("Что красится в цвет команды: обод бака и прочие метки принадлежности")]
        [SerializeField] private Renderer[] teamTint;
        [Tooltip("Зона слива — триггер вокруг бака. Пусто: возьмём триггер с этого объекта")]
        [SerializeField] private Collider pourZone;

        /// <summary>Команда долила порцию: сколько единиц и в какой момент общих часов.</summary>
        public event Action<int, double> Delivered;

        /// <summary>Бутыль слита целиком и убрана — ходка закрыта.</summary>
        public event Action BottleFinished;

        /// <summary>
        /// Бутыли, фактически пересекающие зону на текущем шаге физики.
        /// </summary>
        private readonly List<WaterBottle> insideZone = new List<WaterBottle>(4);
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
        }

        /// <summary>
        /// Найти зону слива, не тронув тело бака.
        ///
        /// Здесь стояло <c>GetComponent&lt;Collider&gt;().isTrigger = true</c> —
        /// и брало это <b>первый</b> коллайдер объекта. А коллайдеров на баке
        /// два: сплошное тело, об которое игрок останавливается, и широкая
        /// зона слива вокруг него. Первым лежит тело — и код каждый запуск
        /// превращал его в триггер. Отсюда сразу три жалобы с прогона:
        /// сквозь бак можно пройти насквозь, донесённая бутыль его не
        /// наполняет и шкала воды не растёт.
        ///
        /// Последние два — потому, что триггеров становилось два, а
        /// обработчик выхода выкидывал бутыль из зоны по выходу из
        /// <b>любого</b> из них: бутыль стоит в баке, а бак её уже не видит.
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
            insideZone.Clear();
            ApplyLevelVisual();
            ApplyTeamTint();
        }

        /// <summary>
        /// Цвет команды на ободе. Баков на арене два, стоят они симметрично, и
        /// без цвета «свой» от «чужого» отличается только памятью игрока —
        /// а бежать к чужому баку с полной бутылью очень обидно.
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

            RefreshBottlesInZone();
            for (int i = insideZone.Count - 1; i >= 0; i--)
            {
                WaterBottle bottle = insideZone[i];
                if (bottle != null && !bottle.IsGone && bottle.Team == team)
                {
                    Pour(bottle, Time.fixedDeltaTime);
                }
            }
        }

        private void RefreshBottlesInZone()
        {
            insideZone.Clear();
            if (pourZone == null || !pourZone.enabled || !pourZone.gameObject.activeInHierarchy)
            {
                pourAccumulator = 0f;
                return;
            }

            // Enter/Exit can be lost on reset, disable or despawn. Query the
            // actual shape, including sleeping bottles, on the authority.
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
                WaterBottle bottle = other.GetComponentInParent<WaterBottle>();
                if (bottle == null || bottle.IsGone || bottle.Team != team || insideZone.Contains(bottle))
                    continue;

                // Bounds are only the broad phase: rotated zones must not
                // accept bottles in the empty corners of their world AABB.
                if (Physics.ComputePenetration(pourZone, pourZone.transform.position, pourZone.transform.rotation,
                    other, other.transform.position, other.transform.rotation, out _, out _))
                    insideZone.Add(bottle);
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
        /// Один шаг слива. Скорость постоянна: полная бутыль уходит за время из
        /// конфига, полупустая — вдвое быстрее.
        /// </summary>
        private void Pour(WaterBottle bottle, float delta)
        {
            // Донесли пустую — засчитывается ноль, но бутыль всё равно исчезает:
            // ходка закрыта, штабель выдаёт новую.
            if (bottle.Water <= 0)
            {
                FinishBottle(bottle);
                return;
            }

            float rate = config.BottleCapacity / Mathf.Max(0.01f, config.PourSeconds);
            pourAccumulator += rate * delta;

            int whole = Mathf.FloorToInt(pourAccumulator);
            if (whole <= 0)
            {
                return;
            }

            pourAccumulator -= whole;

            int taken = bottle.SpendWater(whole, WaterLossReason.Poured);
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

            if (bottle.Water <= 0)
            {
                FinishBottle(bottle);
            }
        }

        private void FinishBottle(WaterBottle bottle)
        {
            int index = insideZone.IndexOf(bottle);
            if (index >= 0)
            {
                insideZone.RemoveAt(index);
            }

            pourAccumulator = 0f;
            BottleFinished?.Invoke();
            bottle.Vanish();
        }

        /// <summary>Уровень ступенями по пять единиц — как в бутыли, тем же шагом.</summary>
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

            Vector3 scale = waterMesh.localScale;
            scale.y = Mathf.Max(0.001f, fraction);
            waterMesh.localScale = scale;
        }
    }
}
