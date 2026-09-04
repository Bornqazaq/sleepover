using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>Подвох стены. Объявляется вместе с рисунком, а не бросается в момент срабатывания.</summary>
    public enum WallTrick : byte
    {
        /// <summary>Обычная стена: что видно на подъезде, то и будет.</summary>
        None = 0,

        /// <summary>Зеркальный переворот: вырезы меняются местами, позы остаются.</summary>
        Mirror = 1,

        /// <summary>Смена формы: меняется поза одного или обоих вырезов, места остаются.</summary>
        Morph = 2
    }

    /// <summary>Один вырез: поза и место по ширине дорожки, м от её центра.</summary>
    public readonly struct WallCutoutSpec
    {
        public HoleInWallPose Pose { get; }
        public float Offset { get; }

        public WallCutoutSpec(HoleInWallPose pose, float offset)
        {
            Pose = pose;
            Offset = offset;
        }
    }

    /// <summary>
    /// Рисунок одной стены целиком, включая подвох и то, во что он превратит
    /// вырезы. Простая структура из значений: в фазе 3 она станет
    /// <c>INetworkSerializable</c> и уедет в <c>NetworkVariable</c> как есть.
    /// </summary>
    public readonly struct WallPattern
    {
        /// <summary>Левый вырез — либо единственный, если <see cref="HasSecond"/> ложь.</summary>
        public WallCutoutSpec First { get; }

        /// <summary>Правый вырез. Значим только при <see cref="HasSecond"/>.</summary>
        public WallCutoutSpec Second { get; }

        /// <summary>Вырезов два: дорожка пары. Ложь — дорожка одиночки.</summary>
        public bool HasSecond { get; }

        public WallTrick Trick { get; }

        /// <summary>Во что превращается первый вырез при <see cref="WallTrick.Morph"/>.</summary>
        public HoleInWallPose MorphFirst { get; }

        /// <summary>Во что превращается второй вырез при <see cref="WallTrick.Morph"/>.</summary>
        public HoleInWallPose MorphSecond { get; }

        public WallPattern(WallCutoutSpec first, WallCutoutSpec second, bool hasSecond,
            WallTrick trick, HoleInWallPose morphFirst, HoleInWallPose morphSecond)
        {
            First = first;
            Second = second;
            HasSecond = hasSecond;
            Trick = trick;
            MorphFirst = morphFirst;
            MorphSecond = morphSecond;
        }

        public int CutoutCount => HasSecond ? 2 : 1;
    }

    /// <summary>
    /// Генератор рисунка стен: задача, которую игроки читают с дистанции.
    /// Всё остальное в игре — реакция на этот рисунок.
    /// </summary>
    /// <remarks>
    /// <b>Генерирует сервер по сиду раунда, детерминированно.</b> Сид берётся
    /// <c>System.Random</c>, а не <c>UnityEngine.Random</c>: у второго состояние
    /// глобальное, и любой посторонний вызов посреди генерации сдвинул бы
    /// расписание. Тот же приём в <c>MemoryRunRoute</c> и «Порядке банок».
    ///
    /// <b>Геометрия важнее полосы разноса.</b> Спека задаёт разнос 2–5 ШП и
    /// узкую полосу 2–3 на первых стенах, но два выреза «руки в стороны» шириной
    /// 3.6 ШП каждый на разносе 3 просто перекрылись бы, и стена развалилась бы
    /// в один широкий проём, где не читается ни один силуэт. Поэтому полоса
    /// поднимается до минимально возможного разноса, если иначе вырезы
    /// не помещаются. Верхнего предела 5 ШП это не задевает: самый широкий
    /// вынужденный разнос — 4.0 ШП.
    ///
    /// <b>Ширина берётся у форм игрока, а не у конфига.</b> Вырез закреплён
    /// за конкретным игроком, и у Карлана он уже, чем у Шланги. Значит
    /// и разнос, и предельное смещение считаются по этим двоим: одним числом
    /// на всех они либо разводили бы вырезы шире нужного, либо — что хуже —
    /// позволили бы им налезть друг на друга.
    ///
    /// <b>Каждый вырез стоит над половиной пола своего хозяина.</b> Нулевое
    /// место на платформе левее центра, первое правее
    /// (<c>HoleInWallTrack.ArrangeSlots</c>), и пол под ними покрашен теми же
    /// двумя цветами, что контуры вырезов
    /// (<c>HoleInWallArenaBuilder.BuildFloorHalves</c>). До 04.09 генератор
    /// выбирал случайный центр пары по всей ширине платформы, и оба выреза
    /// регулярно оказывались над одной половиной: розовый контур висел над
    /// голубым полом, его хозяин бежал через всю платформу, тащил напарника
    /// на тросе и не успевал. Это ломало не сложность, а сам способ читать
    /// стену — цвет переставал что-либо значить.
    ///
    /// Замер стендом, 51 200 стен (100 сидов × 64 пары персонажей):
    /// вырез залезал на чужую половину в <b>37 046</b> случаях из 51 200,
    /// оба центра стояли по одну сторону от нуля в <b>18 500</b>. После правки
    /// и то, и другое — ноль. Максимальный пробег до своего выреза упал
    /// с 3.21 м до 2.26 м: на самой быстрой стене это 0.46 с бега при
    /// подъезде 3 с.
    ///
    /// Правило действует <b>целиком</b>, а не по центру выреза: у самой широкой
    /// позы 2.55 м, и вырез с центром у нуля залез бы на чужую половину
    /// на 1.27 м. Отсюда у смещения появилась ближняя граница — полуширина
    /// выреза; дальняя осталась прежней, и это то же число, потому что
    /// половина платформы и есть половина её ширины.
    ///
    /// <b>Зеркальный переворот — единственное исключение, и оно намеренное.</b>
    /// <see cref="WallTrick.Mirror"/> меняет вырезы местами уже в полёте
    /// (<c>SweepingWall.ResolveCutout</c>), то есть нарушает это правило
    /// на седьмой стене — ради чего он и заведён. После закрепления вырезов
    /// за игроками это стало перебежкой крест-накрест на тросе: расстояние
    /// между хозяевами при этом не меняется вовсе (оно равно разносу и всегда
    /// короче троса), меняются стороны. Замеры — в стенде
    /// <c>Igruha/Дырка в стене/Стенд: прогнать генератор</c>.
    /// </remarks>
    public sealed class WallPatternGenerator
    {
        /// <summary>Сколько раз пытаться подобрать пару поз, не совпавшую с предыдущей стеной.</summary>
        private const int PoseAttempts = 16;

        /// <summary>Позы простых стен: без выпада (спека 8.6, правило 1).</summary>
        private const int EasyPoseCount = 3;

        /// <summary>
        /// Разложить восемь стен по сиду. Заполняет переданный список: рисунок
        /// считается раз в раунд, но плодить мусор незачем.
        /// </summary>
        /// <param name="first">Формы нулевого выреза — по ним считается его ширина</param>
        /// <param name="second">Формы первого выреза. У одиночки не нужны</param>
        /// <param name="solo">Дорожка одиночки: вырез один</param>
        public void Generate(HoleInWallConfig config, CutoutShapes first, CutoutShapes second,
            int seed, bool solo, List<WallPattern> destination)
        {
            if (config == null || first == null || destination == null || (!solo && second == null))
            {
                return;
            }

            destination.Clear();

            var random = new System.Random(seed);
            HoleInWallPose previousFirst = HoleInWallPose.None;
            HoleInWallPose previousSecond = HoleInWallPose.None;

            for (int wall = 0; wall < config.WallCount; wall++)
            {
                WallPattern pattern = BuildWall(config, first, second, random, wall, solo,
                    previousFirst, previousSecond);
                destination.Add(pattern);

                previousFirst = pattern.First.Pose;
                previousSecond = pattern.HasSecond ? pattern.Second.Pose : HoleInWallPose.None;
            }
        }

        private WallPattern BuildWall(HoleInWallConfig config, CutoutShapes firstShapes,
            CutoutShapes secondShapes, System.Random random, int wall, bool solo,
            HoleInWallPose previousFirst, HoleInWallPose previousSecond)
        {
            bool easy = wall < config.EasyWallCount;
            int poseLimit = easy ? EasyPoseCount : HoleInWallConfig.PoseCount;

            PickPoses(random, poseLimit, solo, previousFirst, previousSecond,
                out HoleInWallPose first, out HoleInWallPose second);

            WallTrick trick = ResolveTrick(config, wall);
            HoleInWallPose morphFirst = first;
            HoleInWallPose morphSecond = second;

            if (trick == WallTrick.Morph)
            {
                // Новая поза не может совпасть со старой, иначе подвоха не видно.
                // Ограничение простых стен здесь не действует: смена формы стоит
                // на последней стене, а она заведомо не простая.
                morphFirst = DifferentPose(random, first, HoleInWallConfig.PoseCount);
                if (!solo)
                {
                    morphSecond = DifferentPose(random, second, HoleInWallConfig.PoseCount);
                }
            }

            // Габарит, под который считаются и разнос, и предельное смещение,
            // берётся по самому широкому состоянию выреза: после смены формы
            // позиции не двигаются, и новый силуэт обязан поместиться там же.
            float firstWidth = WidestWidth(firstShapes, first, morphFirst);
            float secondWidth = solo ? 0f : WidestWidth(secondShapes, second, morphSecond);

            if (solo)
            {
                float soloLimit = OffsetLimit(config, firstWidth);
                float soloOffset = NextFloat(random, -soloLimit, soloLimit);
                return new WallPattern(
                    new WallCutoutSpec(first, soloOffset), default, false,
                    trick, morphFirst, morphSecond);
            }

            float minSpread = (firstWidth + secondWidth) * 0.5f + config.CutoutBridge;
            float bandMin = easy ? config.SpreadMin : BandMin(config, wall);
            float bandMax = easy ? config.EarlySpreadMax : config.SpreadMax;

            float firstLimit = OffsetLimit(config, firstWidth);
            float secondLimit = OffsetLimit(config, secondWidth);

            float spread = NextFloat(random, Mathf.Max(bandMin, minSpread), Mathf.Max(bandMax, minSpread));
            spread = Mathf.Min(spread, firstLimit + secondLimit);

            // Центр пары. Ограничений на него два, и оба про место выреза
            // целиком, а не про его центр:
            //
            //   нулевой вырез  — внутри платформы и не правее нуля:
            //                    −firstLimit ≤ offset₀ ≤ −firstWidth/2
            //   первый вырез   — внутри платформы и не левее нуля:
            //                    +secondWidth/2 ≤ offset₁ ≤ +secondLimit
            //
            // offset₀ = center − spread/2, offset₁ = center + spread/2, отсюда
            // и границы ниже. Полоса непуста при любом разносе не меньше
            // minSpread — он как раз и складывается из двух полуширин плюс
            // перемычка.
            float centerMin = Mathf.Max(-firstLimit + spread * 0.5f, secondWidth * 0.5f - spread * 0.5f);
            float centerMax = Mathf.Min(secondLimit - spread * 0.5f, spread * 0.5f - firstWidth * 0.5f);
            float center = centerMax > centerMin
                ? NextFloat(random, centerMin, centerMax)
                : (centerMin + centerMax) * 0.5f;

            return new WallPattern(
                new WallCutoutSpec(first, center - spread * 0.5f),
                new WallCutoutSpec(second, center + spread * 0.5f),
                true, trick, morphFirst, morphSecond);
        }

        /// <summary>Нижняя граница разноса: широкая полоса на стенах сразу после простых.</summary>
        private static float BandMin(HoleInWallConfig config, int wall) =>
            wall < config.EasyWallCount + config.WideWallCount ? config.WideSpreadMin : config.SpreadMin;

        private static WallTrick ResolveTrick(HoleInWallConfig config, int wall)
        {
            if (wall == config.MirrorWallIndex)
            {
                return WallTrick.Mirror;
            }

            return wall == config.MorphWallIndex ? WallTrick.Morph : WallTrick.None;
        }

        /// <summary>
        /// Пара поз, не повторяющая предыдущую стену.
        ///
        /// Сравнение <b>без учёта порядка</b>: те же две позы, поменянные
        /// местами, для игрока почти та же задача, и подряд они читаются как
        /// повтор. Правило 3 спеки от этого только строже.
        /// </summary>
        private static void PickPoses(System.Random random, int poseLimit, bool solo,
            HoleInWallPose previousFirst, HoleInWallPose previousSecond,
            out HoleInWallPose first, out HoleInWallPose second)
        {
            for (int attempt = 0; attempt < PoseAttempts; attempt++)
            {
                first = NextPose(random, poseLimit);
                second = solo ? HoleInWallPose.None : NextPose(random, poseLimit);

                if (!SamePair(first, second, previousFirst, previousSecond))
                {
                    return;
                }
            }

            // Перебор не сошёлся — берём что выпало. Такое возможно только при
            // одной доступной позе, и тогда повторять нечего.
            first = NextPose(random, poseLimit);
            second = solo ? HoleInWallPose.None : NextPose(random, poseLimit);
        }

        private static bool SamePair(HoleInWallPose first, HoleInWallPose second,
            HoleInWallPose previousFirst, HoleInWallPose previousSecond) =>
            (first == previousFirst && second == previousSecond) ||
            (first == previousSecond && second == previousFirst);

        private static HoleInWallPose NextPose(System.Random random, int poseLimit) =>
            (HoleInWallPose)(random.Next(poseLimit) + 1);

        private static HoleInWallPose DifferentPose(System.Random random, HoleInWallPose from, int poseLimit)
        {
            if (poseLimit <= 1)
            {
                return from;
            }

            // Сдвиг на 1…poseLimit−1 по кругу: попасть в исходную позу нельзя
            // по построению, и перебора с повторами не нужно.
            int shift = random.Next(poseLimit - 1) + 1;
            int index = ((int)from - 1 + shift) % poseLimit;
            return (HoleInWallPose)(index + 1);
        }

        private static float WidestWidth(CutoutShapes shapes, HoleInWallPose a, HoleInWallPose b) =>
            Mathf.Max(shapes.Size(a).x, shapes.Size(b).x);

        /// <summary>
        /// Насколько далеко от центра дорожки может стоять вырез такой ширины, м.
        ///
        /// Ограничение одно — вырез целиком внутри платформы. Раньше их было
        /// два, вторым шёл допуск попадания; теперь он по построению не шире
        /// полувыреза (<c>CutoutShapes.Tolerance</c>), и отдельным условием
        /// быть перестал.
        /// </summary>
        private static float OffsetLimit(HoleInWallConfig config, float width) =>
            Mathf.Max(0f, (config.PlatformWidth - width) * 0.5f);

        private static float NextFloat(System.Random random, float min, float max) =>
            max <= min ? min : min + (float)random.NextDouble() * (max - min);
    }
}
