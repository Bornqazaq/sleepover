using System;
using UnityEngine;
using Igruha.Core.Session;
using Igruha.Core.UI;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Кран команды: место, где тележка набирает воду, и её стоянка.
    ///
    /// Кран течёт <b>всегда</b> — струя, лужа и журчание не зависят от того,
    /// стоит ли под ним тележка. Воду не выдают, её набирают: тележка в зоне
    /// под изливом получает <c>fillRate</c> единиц в секунду, пока стоит в
    /// ней, и увозит ровно столько, сколько успела набрать. Кнопок нет:
    /// сколько стоишь — столько везёшь (спека v2, разделы 4, 5).
    ///
    /// Зона проверяется на авторитете серверным запросом формы, а не
    /// триггерами: вход и выход теряются на сбросе и телепорте, и слив в бак
    /// на этом уже ломался (IGR-592). Тот же приём, что у <see cref="WaterTank"/>,
    /// только вода идёт в обратную сторону.
    ///
    /// Стоянка — точка, куда тележка ставится на старте и куда возвращается
    /// из пропасти. Она внутри зоны: вернувшаяся тележка сразу набирает.
    /// </summary>
    public sealed class WaterTap : MonoBehaviour
    {
        [SerializeField] private CarryItemConfig config;
        [Tooltip("Зона наполнения — триггер под изливом. Пусто: возьмём триггер с этого объекта")]
        [SerializeField] private Collider fillZone;
        [Tooltip("Стоянка тележки: сюда она ставится на старте и возвращается из пропасти. Пусто — этот объект")]
        [SerializeField] private Transform dock;
        [Tooltip("Что красится в цвет команды: вентиль, табличка и прочие метки принадлежности")]
        [SerializeField] private Renderer[] teamTint;

        /// <summary>Тележка команды начала или перестала набирать воду. Под звук и эффекты.</summary>
        public event Action<bool> FillingChanged;

        private readonly Collider[] overlaps = new Collider[16];
        private TeamSide team = TeamSide.None;
        private MaterialPropertyBlock materialBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private float fillAccumulator;
        private bool filling;
        private WaterCart fillingCart;

        /// <summary>Чей кран.</summary>
        public TeamSide Team => team;

        /// <summary>Тележка команды. Ставят правила раунда; пусто — команда пока без тележки.</summary>
        public WaterCart Cart { get; private set; }

        /// <summary>Стоянка тележки в мире.</summary>
        public Vector3 DockPosition => (dock != null ? dock : transform).position;
        public Quaternion DockRotation => (dock != null ? dock : transform).rotation;

        /// <summary>Набирает ли тележка воду прямо сейчас. На авторитете — из расчёта, у остальных — из состояния тележки.</summary>
        public bool IsFilling => fillingCart != null ? fillingCart.IsFilling : Cart != null && Cart.IsFilling;

        private void Awake()
        {
            materialBlock = new MaterialPropertyBlock();
            ResolveZone();
        }

        /// <summary>
        /// Зона наполнения. Берём явную ссылку, иначе первый триггер объекта:
        /// зона лежит на дочернем объекте, а сплошной коллайдер стояка
        /// триггером делать нельзя — об него игрок останавливается. Разбор — в
        /// <see cref="WaterTank"/>, там это уже стоило трёх жалоб.
        /// </summary>
        private void ResolveZone()
        {
            if (fillZone != null)
            {
                fillZone.isTrigger = true;
                return;
            }

            var colliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].isTrigger)
                {
                    fillZone = colliders[i];
                    return;
                }
            }

            Debug.LogError($"{name}: у крана нет коллайдера-триггера — зону наполнения брать неоткуда", this);
        }

        /// <summary>Подключить кран к команде. Зовут правила раунда на старте.</summary>
        public void Configure(CarryItemConfig gameConfig, TeamSide side)
        {
            config = gameConfig;
            team = side;
            fillAccumulator = 0f;
            SetFilling(false);
            ApplyTeamTint();
        }

        /// <summary>Записать тележку команды. Зовут правила раунда: у сервера при спавне, у клиента при усыновлении.</summary>
        public void AttachCart(WaterCart cart)
        {
            if (Cart == cart)
            {
                return;
            }

            SetFilling(false);
            Cart = cart;
            fillingCart = null;
            fillAccumulator = 0f;
        }

        /// <summary>Отвязать тележку — конец раунда.</summary>
        public void DetachCart()
        {
            SetFilling(false);
            Cart = null;
            fillingCart = null;
        }

        /// <summary>Тележка в зоне наполнения. Считается формой, а не триггером, поэтому верно и после телепорта.</summary>
        public bool Contains(WaterCart cart)
        {
            if (cart == null || fillZone == null || !fillZone.enabled || !fillZone.gameObject.activeInHierarchy)
            {
                return false;
            }

            Bounds bounds = fillZone.bounds;
            int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, overlaps,
                Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider other = overlaps[i];
                overlaps[i] = null;
                if (other == null || other.GetComponentInParent<WaterCart>() != cart)
                {
                    continue;
                }

                if (Physics.ComputePenetration(fillZone, fillZone.transform.position, fillZone.transform.rotation,
                    other, other.transform.position, other.transform.rotation, out _, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private void FixedUpdate()
        {
            if (config == null || Cart == null || !WorldAuthority.HasAuthority)
            {
                return;
            }

            // Either vessel can refill here after a capture. The physical stream chooses
            // the vessel; original spawn ownership never prevents water entering it.
            WaterCart target = Contains(Cart) ? Cart : null;
            if (Igruha.Core.Minigame.MinigameControllerBase.Current is CarryItemMinigame game)
            {
                WaterCart other = game.CartOf(team == TeamSide.A ? TeamSide.B : TeamSide.A);
                if (target == null && Contains(other)) target = other;
            }
            if (fillingCart != target)
            {
                SetFilling(false); fillingCart = target; fillAccumulator = 0f;
            }
            bool inZone = target != null && !target.IsLost && target.Water < config.CartCapacity;
            SetFilling(inZone);
            if (!inZone) { fillAccumulator = 0f; return; }

            fillAccumulator += config.FillRate * Time.fixedDeltaTime;
            int whole = Mathf.FloorToInt(fillAccumulator);
            if (whole <= 0)
            {
                return;
            }

            fillAccumulator -= whole;
            fillingCart.ChangeWater(whole, WaterLossReason.Filled);
        }

        /// <summary>
        /// Флаг наполнения живёт на тележке: он едет по сети вместе с уровнем,
        /// и клиент видит струю в бак тележки по нему, а не по своим догадкам.
        /// </summary>
        private void SetFilling(bool value)
        {
            if (fillingCart != null && WorldAuthority.HasAuthority)
            {
                fillingCart.SetFilling(value);
            }

            if (filling == value)
            {
                return;
            }

            filling = value;
            FillingChanged?.Invoke(value);
        }

        /// <summary>Цвет команды на кране: свой кран от чужого иначе отличается только памятью игрока.</summary>
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
    }
}
