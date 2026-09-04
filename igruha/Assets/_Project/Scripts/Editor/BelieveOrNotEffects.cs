using UnityEditor;
using UnityEngine;
using Igruha.Minigames.BelieveOrNot;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Эффекты «Верю / не верю» — подфаза 4.4: пыль в луче лампы, облачко
    /// в лицо проигравшему и свечение под поднятой карточкой.
    ///
    /// <b>Ни одного нового события.</b> Пыль висит в луче всегда и никого не
    /// ждёт; облачко уже вызывается из <see cref="BelieveBox.PlayGag"/>, и
    /// здесь только подменяется сам партикл; свечение — ребёнок карточки,
    /// поэтому оно включается ровно тогда, когда карточка поднимается,
    /// и гаснет вместе с ней. Ни своих RPC, ни своего состояния, ни правок
    /// в <c>Core/</c>.
    ///
    /// <b>Партиклы пака переводятся в ручной запуск.</b> У всех у них стоит
    /// автостарт: положенный в сцену гэг пыхнул бы на старте кона, до того
    /// как кто-то открыл коробку.
    ///
    /// <b>Без паков всё остаётся рабочим.</b> Паки Synty в репозиторий не
    /// кладутся, и на машине без них эффект просто не создаётся, а вызывающий
    /// оставляет свою заглушку из фазы 2 — сцена не разваливается.
    /// </summary>
    internal static class BelieveOrNotEffects
    {
        private const string ParticleFx = "Assets/Synty/PolygonParticleFX/Prefabs/";
        private const string CarnivalFx = "Assets/Synty/PolygonHorrorCarnival/Prefabs/FX/";

        private const string DustPath = ParticleFx + "FX_Dust_Small_01.prefab";
        private const string PuffPath = CarnivalFx + "FX_Smoke_Blast_01.prefab";
        private const string GlowPath = ParticleFx + "FX_GlowSpot_02.prefab";

        /// <summary>Размер частицы облачка, м.</summary>
        private const float PuffSize = 0.075f;

        /// <summary>Скорость разлёта облачка, м/с.</summary>
        private const float PuffSpeed = 0.9f;

        /// <summary>Сколько живёт облачко, с. Дальше его сносит и оно мешает смотреть на карточки.</summary>
        private const float PuffSeconds = 1.3f;

        /// <summary>
        /// Плотность пыли, частиц в секунду. При времени жизни 5 с это около
        /// семидесяти пылинок в воздухе — луч читается, а кадр не рябит.
        /// </summary>
        private const float DustRate = 14f;

        /// <summary>
        /// Размер пылинки, м. У пака стоит 0.15 — это снежные хлопья в ладонь,
        /// пак рассчитан на улицу. Проверено рендером 04.09: на 0.15 кадр
        /// засыпало белыми пятнами по всему залу.
        /// </summary>
        private const float DustSize = 0.025f;

        /// <summary>
        /// Полуширина облака пыли, м. Пыль <b>не подсвечивается лампой</b> —
        /// у партиклов пака аддитивный неосвещаемый материал, — поэтому «пыль
        /// в луче» делается не светом, а геометрией: облако держится в узкой
        /// колонне над столом, и за её пределами пылинок нет вовсе. Ширина
        /// взята по столу, а не по световому кругу: у пола луч вчетверо шире,
        /// и пылинки там висели бы просто в темноте.
        /// </summary>
        private const float DustHalfWidth = 0.8f;

        /// <summary>Тон пылинки: тёплая и полупрозрачная, под цвет лампы.</summary>
        private static readonly Color DustColor = new Color(1f, 0.9f, 0.72f, 0.5f);

        /// <summary>
        /// Пыль в луче лампы. Строится один раз и живёт всегда: это не событие,
        /// а воздух зала.
        ///
        /// Ставится <b>не в саму лампу</b>, а отдельным объектом под ней:
        /// у лампы своя роль (источник света, выверенный в фазе 2), и партикл
        /// внутри неё пришлось бы двигать вместе с любой правкой света.
        /// </summary>
        internal static void BeamDust(Transform parent, BelieveOrNotConfig config)
        {
            if (parent == null || config == null || !DressKit.TryLoad(DustPath, out GameObject prefab))
            {
                return;
            }

            var dust = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            dust.name = "BeamDust";
            dust.transform.localPosition = new Vector3(0f, config.LampHeight * 0.5f, 0f);
            dust.transform.localRotation = Quaternion.identity;

            var system = dust.GetComponent<ParticleSystem>();
            if (system == null)
            {
                return;
            }

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(DustHalfWidth * 2f, config.LampHeight, DustHalfWidth * 2f);

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = DustRate;

            ParticleSystem.MainModule dustMain = system.main;
            dustMain.startSize = DustSize;
            dustMain.startColor = DustColor;

            Scenery(dust);
        }

        /// <summary>
        /// Облачко в лицо проигравшему. Отдаёт систему, которую билдер кладёт
        /// в поле <see cref="BelieveBox"/>: коробка сама зовёт её на раскрытии.
        ///
        /// Ставится <b>у коробки, а не внутрь крышки</b>: крышка на раскрытии
        /// откидывается на 108°, и вложенный партикл пыхнул бы в потолок.
        /// </summary>
        internal static ParticleSystem GagPuff(Transform boxRoot, float size)
        {
            if (boxRoot == null || !DressKit.TryLoad(PuffPath, out GameObject prefab))
            {
                return null;
            }

            var puff = (GameObject)PrefabUtility.InstantiatePrefab(prefab, boxRoot);
            puff.name = "GagPuff";
            puff.transform.localPosition = new Vector3(0f, size * 0.45f, 0f);

            // Наклон в лицо владельцу коробки: локальная ось Z у неё смотрит
            // на соперника, значит облачко идёт назад и вверх.
            puff.transform.localRotation = Quaternion.Euler(-60f, 180f, 0f);

            var system = puff.GetComponent<ParticleSystem>();
            if (system == null)
            {
                return null;
            }

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;

            // Пак рассчитан на взрыв бочки: 0.30 м на частицу и три метра
            // в секунду накрывают облаком всю коробку и половину стола.
            // Здесь нужен пшик в лицо — вчетверо мельче и втрое медленнее.
            main.startSize = PuffSize;
            main.startSpeed = PuffSpeed;
            main.startLifetime = PuffSeconds;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            Scenery(puff);
            return system;
        }

        /// <summary>
        /// Свечение под знаком исхода. Ребёнок самого знака: знак включается
        /// на раскрытии, вместе с ним включается и свечение — отдельного
        /// события не нужно.
        /// </summary>
        internal static void CardGlow(Transform sign, float size, bool win)
        {
            if (sign == null || !DressKit.TryLoad(GlowPath, out GameObject prefab))
            {
                return;
            }

            var glow = (GameObject)PrefabUtility.InstantiatePrefab(prefab, sign);
            glow.name = "Glow";
            glow.transform.localPosition = new Vector3(0f, 0f, -size * 0.12f);
            glow.transform.localRotation = Quaternion.identity;

            var system = glow.GetComponent<ParticleSystem>();
            if (system != null)
            {
                // Зелёное на галочке, красное на кресте — тот же язык цвета,
                // что у самих знаков: с десяти метров зритель читает цвет
                // раньше, чем форму.
                ParticleSystem.MainModule main = system.main;
                main.startColor = win
                    ? new Color(0.45f, 1f, 0.55f, 1f)
                    : new Color(1f, 0.45f, 0.4f, 1f);
                main.startSize = size * 0.8f;
            }

            Scenery(glow);
        }

        /// <summary>
        /// Пометить эффект декорацией: коллайдеров у партиклов не бывает, но
        /// слой важен — <c>Default</c> вне маски деоклюдера, и камера сквозь
        /// эффект проходит, а не упирается в него.
        /// </summary>
        private static void Scenery(GameObject go)
        {
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].gameObject.layer = 0;
            }
        }
    }
}
