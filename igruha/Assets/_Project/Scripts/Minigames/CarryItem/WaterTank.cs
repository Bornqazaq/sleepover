using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Session;

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

        /// <summary>Команда долила порцию: сколько единиц и в какой момент общих часов.</summary>
        public event Action<int, double> Delivered;

        /// <summary>Бутыль слита целиком и убрана — ходка закрыта.</summary>
        public event Action BottleFinished;

        private readonly List<WaterBottle> insideZone = new List<WaterBottle>(4);

        private TeamSide team = TeamSide.None;
        private int water;
        private float pourAccumulator;
        private int shownStep = -1;

        /// <summary>Воды в баке, единиц.</summary>
        public int Water => water;

        /// <summary>Чей бак.</summary>
        public TeamSide Team => team;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
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
        }

        private void OnTriggerEnter(Collider other)
        {
            WaterBottle bottle = other.GetComponentInParent<WaterBottle>();
            if (bottle != null && !insideZone.Contains(bottle))
            {
                insideZone.Add(bottle);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            WaterBottle bottle = other.GetComponentInParent<WaterBottle>();
            if (bottle != null)
            {
                insideZone.Remove(bottle);

                // Вынесли на середине — накопленная доля единицы пропадает
                // вместе с попыткой, а не ждёт следующего захода.
                pourAccumulator = 0f;
            }
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

        private void Update()
        {
            // Слив — потеря воды и прибавка к счёту разом, то есть исход раунда.
            // Считает его только авторитет: у клиента зона бака та же, и без
            // этой проверки каждая машина долила бы свою порцию.
            if (config == null || insideZone.Count == 0 || !WorldAuthority.HasAuthority)
            {
                return;
            }

            for (int i = insideZone.Count - 1; i >= 0; i--)
            {
                WaterBottle bottle = insideZone[i];
                if (bottle == null || bottle.IsGone)
                {
                    insideZone.RemoveAt(i);
                    continue;
                }

                // Чужую тару бак не принимает: иначе донести соперника до своего
                // бака было бы выгоднее, чем нести своё.
                if (bottle.Team != team)
                {
                    continue;
                }

                Pour(bottle, Time.deltaTime);
            }
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
            insideZone.Remove(bottle);
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
