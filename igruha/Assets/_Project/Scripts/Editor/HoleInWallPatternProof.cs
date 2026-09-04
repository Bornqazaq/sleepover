using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Minigames.HoleInWall;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Стенд генератора стен: гоняет <see cref="WallPatternGenerator"/> на сотне
    /// сидов по всем парам персонажей и меряет то, что глазами в плей-моде
    /// не поймать — сторону выреза, пробег до него и запас времени.
    ///
    /// Меню: <c>Igruha/Дырка в стене/Стенд: прогнать генератор</c>. На выходе
    /// таблица в консоли, картинки нет.
    /// </summary>
    /// <remarks>
    /// <b>Зачем.</b> До 04.09 генератор выбирал центр пары по всей ширине
    /// платформы, и оба выреза регулярно вставали над одной половиной пола —
    /// то есть розовый контур над голубым полом. Глазами это ловится раз в
    /// несколько прогонов, счётом — за секунду: на 51 200 стенах нарушений
    /// было 37 046, стало ноль.
    ///
    /// <b>Считает без сцены и без Unity-случайности.</b> Генератор — чистая
    /// функция от сида и от форм двоих, поэтому стенду не нужны ни арена,
    /// ни персонажи: формы берутся прямо из ассета силуэтов по ключу
    /// аниматора, как их берёт игра.
    /// </remarks>
    internal static class HoleInWallPatternProof
    {
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/HoleInWallConfig.asset";
        private const string CharacterConfigPath = "Assets/_Project/Settings/Gameplay/CharacterConfig.asset";

        /// <summary>Сколько сидов гонять. Сид раунда сервер берёт произвольный, так что важен не он, а разброс.</summary>
        private const int SeedCount = 100;

        /// <summary>С какого сида начинать. Ноль пропускаем: он же используется как «сид не задан».</summary>
        private const int FirstSeed = 1;

        /// <summary>
        /// Допуск на сравнение с нулём, м. Смещение считается по float, и вырез,
        /// прижатый ровно к границе половин, не должен попадать в нарушители
        /// из-за последнего бита мантиссы.
        /// </summary>
        private const float Epsilon = 0.001f;

        /// <summary>
        /// Ключи персонажей в ассете силуэтов — имена их контроллеров аниматора,
        /// ровно то, что отдаёт <c>CutoutShapes.KeyOf</c>. Собираются из имён
        /// префабов: у всех восьмерых контроллер называется «<i>префаб</i>Animator».
        /// </summary>
        private static string[] CharacterKeys()
        {
            string[] prefabs = HoleInWallPoseClipBuilder.PrefabNames;
            var keys = new string[prefabs.Length];
            for (int i = 0; i < prefabs.Length; i++)
            {
                keys[i] = prefabs[i] + "Animator";
            }

            return keys;
        }

        [MenuItem("Igruha/Дырка в стене/Стенд: прогнать генератор")]
        internal static void Run()
        {
            var config = AssetDatabase.LoadAssetAtPath<HoleInWallConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError($"HoleInWallPatternProof: нет конфига по пути {ConfigPath}.");
                return;
            }

            var character = AssetDatabase.LoadAssetAtPath<CharacterConfig>(CharacterConfigPath);
            if (character == null)
            {
                Debug.LogError($"HoleInWallPatternProof: нет конфига персонажа по пути {CharacterConfigPath}.");
                return;
            }

            string[] keys = CharacterKeys();
            string[] names = HoleInWallPoseClipBuilder.CharacterNames;
            int walls = config.WallCount;

            var shapes = new CutoutShapes[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                shapes[i] = new CutoutShapes(config, keys[i]);
            }

            var stats = new Stats(walls);
            var patterns = new System.Collections.Generic.List<WallPattern>(walls);
            var generator = new WallPatternGenerator();

            for (int a = 0; a < keys.Length; a++)
            {
                for (int b = 0; b < keys.Length; b++)
                {
                    for (int seed = FirstSeed; seed < FirstSeed + SeedCount; seed++)
                    {
                        generator.Generate(config, shapes[a], shapes[b], seed, false, patterns);
                        Measure(config, shapes[a], shapes[b], names[a], names[b], seed, patterns, stats);
                    }
                }
            }

            var soloStats = new Stats(walls);
            for (int a = 0; a < keys.Length; a++)
            {
                for (int seed = FirstSeed; seed < FirstSeed + SeedCount; seed++)
                {
                    generator.Generate(config, shapes[a], null, seed, true, patterns);
                    MeasureSolo(config, shapes[a], names[a], seed, patterns, soloStats);
                }
            }

            Report(config, character, stats, soloStats, keys.Length);
        }

        // ========== ЗАМЕР ==========

        private static void Measure(HoleInWallConfig config, CutoutShapes firstShapes, CutoutShapes secondShapes,
            string firstName, string secondName, int seed,
            System.Collections.Generic.List<WallPattern> patterns, Stats stats)
        {
            float half = config.PairSpread * 0.5f;
            float platformHalf = config.PlatformWidth * 0.5f;

            for (int wall = 0; wall < patterns.Count; wall++)
            {
                WallPattern pattern = patterns[wall];
                if (!pattern.HasSecond)
                {
                    continue;
                }

                stats.Walls++;

                float firstOffset = pattern.First.Offset;
                float secondOffset = pattern.Second.Offset;

                // Ширина берётся по самому широкому состоянию выреза: смена
                // формы двигает позу, но не место, и новый силуэт обязан
                // помещаться там же.
                float firstWidth = WidestWidth(firstShapes, pattern.First.Pose,
                    pattern.Trick == WallTrick.Morph ? pattern.MorphFirst : pattern.First.Pose);
                float secondWidth = WidestWidth(secondShapes, pattern.Second.Pose,
                    pattern.Trick == WallTrick.Morph ? pattern.MorphSecond : pattern.Second.Pose);

                // 1. Каждый вырез целиком над половиной своего хозяина.
                bool firstOwnHalf = firstOffset + firstWidth * 0.5f <= Epsilon;
                bool secondOwnHalf = secondOffset - secondWidth * 0.5f >= -Epsilon;
                if (!firstOwnHalf || !secondOwnHalf)
                {
                    stats.SideBreaks++;
                    stats.NoteSideBreak(wall, seed, firstName, secondName, firstOffset, secondOffset);
                }

                // 2. Центры по одну сторону от нуля — грубая форма той же
                // поломки, ради которой стенд и заведён.
                if (firstOffset * secondOffset > 0f)
                {
                    stats.SameSide++;
                }

                // 3. Вырез целиком внутри платформы.
                if (firstOffset - firstWidth * 0.5f < -platformHalf - Epsilon ||
                    secondOffset + secondWidth * 0.5f > platformHalf + Epsilon)
                {
                    stats.Overhang++;
                }

                // 4. Перемычка между вырезами.
                float bridge = secondOffset - firstOffset - (firstWidth + secondWidth) * 0.5f;
                stats.MinBridge = Mathf.Min(stats.MinBridge, bridge);

                float spread = secondOffset - firstOffset;
                stats.MinSpread = Mathf.Min(stats.MinSpread, spread);
                stats.MaxSpread = Mathf.Max(stats.MaxSpread, spread);

                // 5. Пробег каждого от своего места до своего выреза.
                stats.NoteRun(wall, Mathf.Abs(firstOffset + half), seed, firstName, secondName);
                stats.NoteRun(wall, Mathf.Abs(secondOffset - half), seed, firstName, secondName);

                // 6. Переворот: вырезы меняются местами уже в полёте, и пробег
                // считается до зеркального места, а не до объявленного.
                if (pattern.Trick == WallTrick.Mirror)
                {
                    stats.MirrorWalls++;
                    stats.NoteMirrorRun(Mathf.Abs(-firstOffset + half), seed, firstName, secondName);
                    stats.NoteMirrorRun(Mathf.Abs(-secondOffset - half), seed, firstName, secondName);
                }
            }
        }

        private static void MeasureSolo(HoleInWallConfig config, CutoutShapes shapes, string name, int seed,
            System.Collections.Generic.List<WallPattern> patterns, Stats stats)
        {
            float platformHalf = config.PlatformWidth * 0.5f;

            for (int wall = 0; wall < patterns.Count; wall++)
            {
                WallPattern pattern = patterns[wall];
                stats.Walls++;

                float offset = pattern.First.Offset;
                float width = WidestWidth(shapes, pattern.First.Pose,
                    pattern.Trick == WallTrick.Morph ? pattern.MorphFirst : pattern.First.Pose);

                if (Mathf.Abs(offset) + width * 0.5f > platformHalf + Epsilon)
                {
                    stats.Overhang++;
                }

                // Одиночка стоит по центру платформы: пробег и есть смещение.
                stats.NoteRun(wall, Mathf.Abs(offset), seed, name, "—");
            }
        }

        private static float WidestWidth(CutoutShapes shapes, HoleInWallPose a, HoleInWallPose b) =>
            Mathf.Max(shapes.Size(a).x, shapes.Size(b).x);

        // ========== ОТЧЁТ ==========

        private static void Report(HoleInWallConfig config, CharacterConfig character,
            Stats pair, Stats solo, int characterCount)
        {
            var report = new StringBuilder();
            report.AppendLine($"HoleInWallPatternProof: {SeedCount} сидов × {characterCount * characterCount} " +
                              $"упорядоченных пар персонажей = {pair.Walls} стен пары и {solo.Walls} стен одиночки");
            report.AppendLine();

            report.AppendLine("ПРАВИЛО ПОЛОВИН");
            report.AppendLine($"  вырез залез на чужую половину:      {pair.SideBreaks}");
            report.AppendLine($"  центры по одну сторону от нуля:     {pair.SameSide}");
            report.AppendLine($"  вырез вылез за платформу (пара):    {pair.Overhang}");
            report.AppendLine($"  вырез вылез за платформу (одиночка):{solo.Overhang}");
            if (pair.SideBreaks > 0)
            {
                report.AppendLine($"  первый нарушитель: {pair.SideBreakNote}");
            }

            report.AppendLine();
            report.AppendLine("ГЕОМЕТРИЯ");
            report.AppendLine($"  минимальная перемычка: {pair.MinBridge:F3} м (порог {config.CutoutBridge:F3})");
            report.AppendLine($"  разнос: {pair.MinSpread:F3} … {pair.MaxSpread:F3} м " +
                              $"(полоса конфига {config.SpreadMin:F2} … {config.SpreadMax:F2}, трос {config.TetherLength:F2})");

            report.AppendLine();
            report.AppendLine($"ПРОБЕГ ДО СВОЕГО ВЫРЕЗА — максимум по стенам " +
                              $"(разгон {character.Acceleration:F0} м/с², потолок {character.MaxSpeed:F1} м/с)");
            report.AppendLine("  стена  подъезд, с   пара: пробег, м → бег, с   запас, с | одиночка: пробег, м → бег, с   запас, с");

            for (int wall = 0; wall < pair.MaxRun.Length; wall++)
            {
                float pairApproach = config.ApproachSeconds(wall, false);
                float soloApproach = config.ApproachSeconds(wall, true);
                float pairRun = RunSeconds(character, pair.MaxRun[wall]);
                float soloRun = RunSeconds(character, solo.MaxRun[wall]);

                report.AppendLine(
                    $"  {wall + 1,5}  {pairApproach,9:F2}   {pair.MaxRun[wall],14:F2} {pairRun,10:F2} " +
                    $"{pairApproach - pairRun,10:F2} | {solo.MaxRun[wall],17:F2} {soloRun,10:F2} " +
                    $"{soloApproach - soloRun,10:F2}");
            }

            report.AppendLine($"  худший случай пары: {pair.RunNote}");

            report.AppendLine();
            report.AppendLine("ЗЕРКАЛЬНЫЙ ПЕРЕВОРОТ — единственное исключение из правила половин");
            if (pair.MirrorWalls == 0)
            {
                report.AppendLine("  стен с переворотом нет: mirrorWall в конфиге выключен");
            }
            else
            {
                float lead = config.MirrorLead;
                float cross = RunSeconds(character, pair.MaxMirrorRun);
                report.AppendLine($"  стена {config.MirrorWallIndex + 1}, объявляется за {lead:F2} с до удара");
                report.AppendLine($"  перебежка крест-накрест: до {pair.MaxMirrorRun:F2} м → {cross:F2} с, " +
                                  $"запас {lead - cross:F2} с");
                report.AppendLine($"  расстояние между напарниками при перебежке не меняется: " +
                                  $"оно равно разносу, до {pair.MaxSpread:F2} м при тросе {config.TetherLength:F2} м");
                report.AppendLine($"  худший случай: {pair.MirrorNote}");
            }

            bool broken = pair.SideBreaks > 0 || pair.Overhang > 0 || solo.Overhang > 0 ||
                          pair.MinBridge < config.CutoutBridge - Epsilon;

            // Кусками, а не одной простынёй: длинную запись консоль редактора
            // обрезает молча, и хвост с пробегами до неё просто не доезжает.
            foreach (string block in report.ToString().Split(Separator, System.StringSplitOptions.RemoveEmptyEntries))
            {
                Debug.Log(block.TrimEnd());
            }

            if (broken)
            {
                Debug.LogError("HoleInWallPatternProof: рисунок стен нарушает правило половин или геометрию — " +
                               "разбор в записях выше.");
            }
        }

        /// <summary>Пустая строка делит отчёт на записи консоли.</summary>
        private static readonly string[] Separator = { "\n\n" };

        /// <summary>
        /// За сколько секунд персонаж пробегает столько метров с места.
        ///
        /// Разгон равноускоренный до потолка скорости — ровно то, что делает
        /// <c>PlayerController</c>. Трос и воронка здесь не учитываются: они
        /// и помогают, и мешают в зависимости от того, куда бежит напарник,
        /// а нужна оценка сверху по чистому ходу.
        /// </summary>
        private static float RunSeconds(CharacterConfig character, float distance)
        {
            if (distance <= 0f)
            {
                return 0f;
            }

            float acceleration = Mathf.Max(0.01f, character.Acceleration);
            float maxSpeed = Mathf.Max(0.01f, character.MaxSpeed);
            float rampDistance = maxSpeed * maxSpeed / (2f * acceleration);

            return distance <= rampDistance
                ? Mathf.Sqrt(2f * distance / acceleration)
                : maxSpeed / acceleration + (distance - rampDistance) / maxSpeed;
        }

        /// <summary>Счётчики одного прогона. Простой мешок значений — стенд живёт один вызов меню.</summary>
        private sealed class Stats
        {
            internal Stats(int walls)
            {
                MaxRun = new float[Mathf.Max(1, walls)];
            }

            internal int Walls;
            internal int SideBreaks;
            internal int SameSide;
            internal int Overhang;
            internal int MirrorWalls;

            internal float MinBridge = float.MaxValue;
            internal float MinSpread = float.MaxValue;
            internal float MaxSpread = float.MinValue;
            internal float MaxMirrorRun;

            internal readonly float[] MaxRun;

            internal string SideBreakNote = string.Empty;
            internal string RunNote = string.Empty;
            internal string MirrorNote = string.Empty;

            private float worstRun;

            internal void NoteSideBreak(int wall, int seed, string first, string second,
                float firstOffset, float secondOffset)
            {
                if (SideBreakNote.Length > 0)
                {
                    return;
                }

                SideBreakNote = $"стена {wall + 1}, сид {seed}, {first} × {second}, " +
                                $"смещения {firstOffset:F2} и {secondOffset:F2} м";
            }

            internal void NoteRun(int wall, float distance, int seed, string first, string second)
            {
                if (wall >= 0 && wall < MaxRun.Length)
                {
                    MaxRun[wall] = Mathf.Max(MaxRun[wall], distance);
                }

                if (distance <= worstRun)
                {
                    return;
                }

                worstRun = distance;
                RunNote = $"стена {wall + 1}, сид {seed}, {first} × {second}, пробег {distance:F2} м";
            }

            internal void NoteMirrorRun(float distance, int seed, string first, string second)
            {
                if (distance <= MaxMirrorRun)
                {
                    return;
                }

                MaxMirrorRun = distance;
                MirrorNote = $"сид {seed}, {first} × {second}, перебежка {distance:F2} м";
            }
        }
    }
}
