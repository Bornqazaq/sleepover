using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Igruha.Core.Audio;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Размечает поверхности сцен — фаза 5, общий слой.
    ///
    /// Без метки пол считается бетоном, и это честное умолчание, но пока меток
    /// нет ни в одной сцене, шесть из семи приехавших наборов шагов не звучат
    /// нигде: и заводской цех, и школьный зал, и песок двора одинаково бетонные.
    ///
    /// <b>Что чем звучит — не выдумка этого прохода.</b> Раскладка записана в
    /// паспорте поставки и продублирована в <see cref="SurfaceKind"/> и
    /// <see cref="CoreSfx"/>: ковёр — хаб, дерево — лестница хаба, клетки цирка
    /// и школа, металл — плиты «Рейса на память», гравий — стройка и песок.
    /// Здесь она только доведена до конкретных объектов сцен.
    ///
    /// <b>Метка ставится на то, у чего есть коллайдер.</b> Шаг узнаёт поверхность
    /// по коллайдеру под ногами и ищет метку вверх по иерархии; на декоративной
    /// сетке без коллайдера метка не прозвучит ни разу.
    ///
    /// Спорная метка правится одним полем в инспекторе и стоит одного неверно
    /// звучащего шага — поэтому раскладка ниже принята, а не отложена до
    /// плейтеста.
    ///
    /// Проход повторим: второй запуск переписывает метки, а не плодит их.
    /// </summary>
    internal static class SurfaceMarkPass
    {
        /// <summary>Объект сцены и то, чем он звучит. Имя — точное либо с хвостом <c>*</c>.</summary>
        private readonly struct Mark
        {
            public readonly string Name;
            public readonly SurfaceKind Kind;

            public Mark(string name, SurfaceKind kind)
            {
                Name = name;
                Kind = kind;
            }

            public bool Matches(string candidate)
                => Name.EndsWith("*")
                    ? candidate.StartsWith(Name[..^1], System.StringComparison.Ordinal)
                    : candidate == Name;
        }

        private static readonly Dictionary<string, Mark[]> ByScene = new()
        {
            // Подвальная game room: пол и лестница — орех (материал HO_Walnut).
            // Ковры в зоне дивана — декоративные сетки без коллайдеров, ходят
            // не по ним, и метка на них не прозвучала бы ни разу.
            ["Hub"] = new[]
            {
                new Mark("Floor_*", SurfaceKind.Wood),
                new Mark("PitFloor", SurfaceKind.Wood),
                new Mark("EntryStairs", SurfaceKind.Wood),
            },

            // Заводской цех: плиты, настил и транспортёры — железо, и это вся
            // арена целиком. Одна метка на корень, а не триста на плиты: шаг
            // ищет метку вверх по иерархии, и корня для этого достаточно.
            ["MemoryRun"] = new[]
            {
                new Mark("_Arena", SurfaceKind.Metal),
            },

            // Школьный зал: паркет.
            ["Exam"] = new[]
            {
                new Mark("Floor_Near", SurfaceKind.Wood),
                new Mark("Floor_Far", SurfaceKind.Wood),
                new Mark("Floor_Left", SurfaceKind.Wood),
                new Mark("Floor_Right", SurfaceKind.Wood),
            },

            // Апокалиптический двор: песок площадки. Паспорт зовёт двор травой,
            // но арт-проход сделал его песчаным, а песок в поставке идёт гравием.
            ["Infection"] = new[]
            {
                new Mark("Ground", SurfaceKind.Gravel),
                new Mark("Floor", SurfaceKind.Gravel),
                new Mark("Sand", SurfaceKind.Gravel),
                new Mark("SandZone", SurfaceKind.Gravel),
                new Mark("Sandbox", SurfaceKind.Gravel),
            },

            // Ночной цирк: опилки шатра и деревянные клетки. Метка на «Cages»
            // накрывает полы всех восьми клеток разом — и обязана стоять именно
            // там, а не на арене: внутри клетки свой объект «Floor», и общая
            // метка опилок перебила бы дерево.
            ["Stopwatch"] = new[]
            {
                new Mark("TentFloor", SurfaceKind.Gravel),
                new Mark("Cages", SurfaceKind.Wood),
            },

            ["CansOrder"] = new[]
            {
                new Mark("TentFloor", SurfaceKind.Gravel),
                new Mark("Cages", SurfaceKind.Wood),
            },

            // Зал с коробками: дощатый помост.
            ["BelieveOrNot"] = new[]
            {
                new Mark("Floor", SurfaceKind.Wood),
            },
        };

        [MenuItem("Igruha/Звук/Разметить поверхности сцены")]
        internal static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!ByScene.TryGetValue(scene.name, out Mark[] marks))
            {
                Debug.Log($"[Звук] Для сцены «{scene.name}» раскладки поверхностей нет — пол останется бетонным.");
                return;
            }

            var report = new StringBuilder();
            int marked = 0;
            int skipped = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform node in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    foreach (Mark mark in marks)
                    {
                        if (!mark.Matches(node.name)) continue;

                        // Ходят по коллайдерам. Метка на сетке без коллайдера — ни
                        // на что не влияющая запись в сцене, и ставить её незачем.
                        if (node.GetComponentInChildren<Collider>(includeInactive: true) == null)
                        {
                            skipped++;
                            break;
                        }

                        SurfaceAudio surface = node.GetComponent<SurfaceAudio>()
                                               ?? Undo.AddComponent<SurfaceAudio>(node.gameObject);

                        var fields = new SerializedObject(surface);
                        fields.FindProperty("kind").enumValueIndex = (int)mark.Kind;
                        fields.ApplyModifiedPropertiesWithoutUndo();

                        report.Append(marked == 0 ? "" : ", ").Append(node.name).Append('→').Append(mark.Kind);
                        marked++;
                        break;
                    }
                }
            }

            if (marked == 0)
            {
                Debug.LogWarning($"[Звук] В сцене «{scene.name}» ни один объект раскладки не найден с коллайдером "
                                 + $"(пропущено без коллайдера: {skipped}). Пол останется бетонным.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Звук] Поверхности сцены «{scene.name}» размечены: {marked} "
                      + $"(без коллайдера пропущено {skipped}).\n{report}");
        }
    }
}
