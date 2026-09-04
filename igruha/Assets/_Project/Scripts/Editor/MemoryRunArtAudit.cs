using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Замеры арта «Рейса на память» — приёмка подфаз 4.1–4.6 числом.
    ///
    /// Отличие от аудитов остальных мини-игр — раздел «Плиты». В других играх
    /// приёмка проверяет, что арт <b>не сдвинул физику</b>; здесь к этому
    /// добавляется проверка, что арт <b>не создал различий</b>. Тридцать плит
    /// обязаны быть неразличимы, и это единственное требование фазы 4, которое
    /// ломает игру целиком, а не портит картинку.
    ///
    /// Проверяется ссылками, а не глазом: тридцать рендереров обязаны делить
    /// один меш, один материал, один поворот и один масштаб. Глаз на тридцати
    /// серых квадратах ошибается, ссылка — нет.
    /// </summary>
    internal static class MemoryRunArtAudit
    {
        /// <summary>Отметка низа зоны выбывания. Ниже неё арт класть можно, выше — нельзя.</summary>
        private const float KillZoneFloorY = -7.14f;

        [MenuItem("Igruha/Рейс на память/Замеры арта")]
        private static void Measure()
        {
            Debug.Log(Report());
        }

        /// <summary>
        /// Отчёт строкой. Вынесен отдельно от пункта меню намеренно: замеры
        /// снимаются мостом MCP, а <c>Debug.Log</c> не всегда доезжает до
        /// читателя консоли — большие записи из буфера теряются. Возвращённую
        /// строку не теряет ничто.
        /// </summary>
        internal static string Report()
        {
            MemoryRunConfig config = FindConfig();
            if (config == null)
            {
                return "Не найден MemoryRunConfig — замерять нечего";
            }

            var arena = GameObject.Find("_Arena");
            if (arena == null)
            {
                return "В сцене нет группы _Arena — сначала построй арену";
            }

            // Замеры идут по габаритам рендереров, а те отстают от только что
            // построенной иерархии. Проверено 04.09: аудит, запущенный тем же
            // вызовом, что и пересборка, насчитал 33 вылезающих объекта с
            // выносом 32 м — при том, что реально вылезал один и на 4 см.
            // Отсюда правило: замеры запускаются <b>отдельно от пересборки</b>,
            // а синхронизация — на всякий случай здесь же.
            Physics.SyncTransforms();

            var report = new StringBuilder();
            report.Append("📏 «Рейс на память» — замеры арта");

            MeasurePlates(arena, config, report);
            MeasureDress(arena, report);
            MeasureScenery(arena, report);
            MeasurePit(arena, report);
            MeasureColliders(report);
            MeasureMeshes(arena, report);

            return report.ToString();
        }

        /// <summary>
        /// 🔴 Главный раздел. Тридцать плит обязаны быть неразличимы: один меш,
        /// один материал, один поворот, один масштаб, сетка без отклонений.
        ///
        /// Любая единица сверх — это примета, по которой игрок начнёт запоминать
        /// плиту вместо маршрута, и вся мини-игра перестаёт быть игрой на память.
        /// </summary>
        private static void MeasurePlates(GameObject arena, MemoryRunConfig config, StringBuilder report)
        {
            Transform plates = arena.transform.Find("Plates");
            report.Append("\n\n— 🔴 Плиты: неразличимость —");
            if (plates == null)
            {
                report.Append("\n  группы Plates нет ✘");
                return;
            }

            var meshes = new HashSet<Mesh>();
            var materials = new HashSet<Material>();
            var rotations = new HashSet<Vector3>();
            var scales = new HashSet<Vector3>();
            int found = 0;
            int undressed = 0;
            int boxesVisible = 0;
            float worstGridError = 0f;

            for (int step = 0; step < config.Steps; step++)
            {
                for (int lane = 0; lane < MemoryRunConfig.LaneCount; lane++)
                {
                    Transform box = plates.Find($"Plate_{step:00}_{lane}");
                    if (box == null)
                    {
                        continue;
                    }

                    found++;

                    var boxRenderer = box.GetComponent<MeshRenderer>();
                    if (boxRenderer != null && boxRenderer.enabled)
                    {
                        boxesVisible++;
                    }

                    Transform dress = box.Find("Dress");
                    if (dress == null)
                    {
                        undressed++;
                        continue;
                    }

                    var filter = dress.GetComponent<MeshFilter>();
                    var renderer = dress.GetComponent<MeshRenderer>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        meshes.Add(filter.sharedMesh);
                    }

                    if (renderer != null && renderer.sharedMaterial != null)
                    {
                        materials.Add(renderer.sharedMaterial);
                    }

                    // Поворот и масштаб округляются до тысячных: сравнивать
                    // числа с плавающей точкой напрямую бессмысленно, а разница
                    // меньше тысячной невидима и на приметы не тянет.
                    rotations.Add(Round(dress.rotation.eulerAngles));
                    scales.Add(Round(dress.lossyScale));

                    // Сетка: плита обязана стоять ровно там, где её ждёт сервер.
                    // Сервер определяет, на какой плите игрок, пересчётом из
                    // координат (MemoryRunConfig.TryGetCell), а не триггером —
                    // сдвинутая артом плита развела бы вид и правила.
                    Vector3 expected = config.CellCenter(step, lane);
                    var actual = new Vector3(box.position.x, 0f, box.position.z);
                    worstGridError = Mathf.Max(worstGridError,
                        Vector3.Distance(new Vector3(expected.x, 0f, expected.z), actual));
                }
            }

            report.Append("\n  плит найдено:             ").Append(found).Append(Mark(found == 30));
            report.Append("\n  не одето:                 ").Append(undressed).Append(Mark(undressed == 0));
            report.Append("\n  коробок видно сквозь арт: ").Append(boxesVisible).Append(Mark(boxesVisible == 0));
            report.Append("\n  уникальных мешей:         ").Append(meshes.Count).Append(Mark(meshes.Count == 1));
            report.Append("\n  уникальных материалов:    ").Append(materials.Count).Append(Mark(materials.Count == 1));
            report.Append("\n  уникальных поворотов:     ").Append(rotations.Count).Append(Mark(rotations.Count == 1));
            report.Append("\n  уникальных масштабов:     ").Append(scales.Count).Append(Mark(scales.Count == 1));
            report.Append("\n  сход с сетки, макс:       ").Append(worstGridError.ToString("F4")).Append(" м")
                .Append(Mark(worstGridError < 0.001f));

            if (meshes.Count == 1 && materials.Count == 1 && rotations.Count == 1 && scales.Count == 1)
            {
                report.Append("\n  → тридцать плит различить нечем ✔");
            }
            else
            {
                report.Append("\n  → 🔴 ПЛИТЫ РАЗЛИЧИМЫ: механика памяти сломана");
            }
        }

        /// <summary>
        /// Дресс не имеет права ни принести коллайдер, ни вылезти за коробку.
        /// Первое даёт вторую поверхность другой формы поверх выверенной
        /// прыжком геометрии, второе — опору там, где физики нет.
        /// </summary>
        private static void MeasureDress(GameObject arena, StringBuilder report)
        {
            int dressed = 0;
            int colliders = 0;
            int overflowing = 0;
            float worstOverflow = 0f;

            foreach (Transform dress in arena.GetComponentsInChildren<Transform>(true))
            {
                if (dress.name != "Dress" || dress.parent == null)
                {
                    continue;
                }

                dressed++;
                colliders += dress.GetComponentsInChildren<Collider>(true).Length;

                var box = dress.parent.GetComponent<Collider>();
                if (box == null || !TryWorldBounds(dress.gameObject, out Bounds art))
                {
                    continue;
                }

                Bounds cage = box.bounds;
                float over = Mathf.Max(
                    Mathf.Max(art.max.x - cage.max.x, cage.min.x - art.min.x),
                    Mathf.Max(art.max.z - cage.max.z, cage.min.z - art.min.z));
                over = Mathf.Max(over, art.max.y - cage.max.y);

                if (over > 0.01f)
                {
                    overflowing++;
                    worstOverflow = Mathf.Max(worstOverflow, over);
                }
            }

            report.Append("\n\n— Дресс —");
            report.Append("\n  коробок одето:            ").Append(dressed);
            report.Append("\n  коллайдеров в дрессе:     ").Append(colliders).Append(Mark(colliders == 0));
            report.Append("\n  вылезает за коробку:      ").Append(overflowing).Append(Mark(overflowing == 0));
            if (overflowing > 0)
            {
                report.Append(" (макс ").Append(worstOverflow.ToString("F2")).Append(" м)");
            }

            report.Append("\n  ненайденных моделей:      ").Append(MemoryRunDress.Missing.Count)
                .Append(Mark(MemoryRunDress.Missing.Count == 0));
        }

        /// <summary>
        /// Конструкция под рядами и декор: коллайдеров ноль, слой Default.
        /// Коллайдер здесь ловил бы прыжок и делал приземление лотереей,
        /// а слой Ground — заставил бы камеру цепляться за балку.
        /// </summary>
        private static void MeasureScenery(GameObject arena, StringBuilder report)
        {
            report.Append("\n\n— Несущая конструкция и декор —");

            Transform structure = arena.transform.Find("Plates/RowStructure");
            if (structure == null)
            {
                report.Append("\n  группы RowStructure нет ✘");
                return;
            }

            int parts = 0;
            int colliders = 0;
            int offDefault = 0;
            int shadows = 0;
            var sizes = new HashSet<Vector3>();
            int beams = 0;

            foreach (Transform part in structure.GetComponentsInChildren<Transform>(true))
            {
                if (part == structure)
                {
                    continue;
                }

                parts++;
                colliders += part.GetComponents<Collider>().Length;
                if (part.gameObject.layer != LayerMask.NameToLayer("Default"))
                {
                    offDefault++;
                }

                var renderer = part.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                {
                    shadows++;
                }

                if (part.name.StartsWith("Beam_"))
                {
                    beams++;
                    sizes.Add(Round(part.lossyScale));
                }
            }

            report.Append("\n  частей:                   ").Append(parts);
            report.Append("\n  коллайдеров:              ").Append(colliders).Append(Mark(colliders == 0));
            report.Append("\n  не на Default:            ").Append(offDefault).Append(Mark(offDefault == 0));
            report.Append("\n  отбрасывают тень:         ").Append(shadows).Append(Mark(shadows == 0));

            // Балка под рядом обязана быть одна и та же на все десять рядов:
            // разная — это метка ряда, то есть та же подсказка, что царапина.
            report.Append("\n  балок под рядами:         ").Append(beams).Append(Mark(beams == 10));
            report.Append("\n  уникальных сечений балки: ").Append(sizes.Count).Append(Mark(sizes.Count == 1));
        }

        /// <summary>
        /// Ничего в пропасти выше низа зоны выбывания. Балка или труба над этой
        /// отметкой превращает падение в приземление, и игрок остаётся жив там,
        /// где правила требуют смерти.
        /// </summary>
        private static void MeasurePit(GameObject arena, StringBuilder report)
        {
            int above = 0;
            float highest = float.NegativeInfinity;
            string worst = string.Empty;

            foreach (var renderer in arena.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;

                // Смотрим только на то, что живёт ниже поверхности ходьбы:
                // всё, что выше нуля, к пропасти отношения не имеет.
                if (bounds.max.y > -0.5f)
                {
                    continue;
                }

                if (bounds.max.y > KillZoneFloorY)
                {
                    above++;
                    if (bounds.max.y > highest)
                    {
                        highest = bounds.max.y;
                        worst = renderer.name;
                    }
                }
            }

            report.Append("\n\n— Пропасть —");
            report.Append("\n  арта выше зоны выбывания: ").Append(above).Append(Mark(above == 0));
            if (above > 0)
            {
                report.Append(" (выше всех «").Append(worst).Append("» на ")
                    .Append(highest.ToString("F2")).Append(" м при пороге ")
                    .Append(KillZoneFloorY.ToString("F2")).Append(')');
            }
        }

        /// <summary>
        /// Число сплошных коллайдеров обязано совпасть с блокаутом. Считаем
        /// по всей сцене, а не по арене: барьер очереди живёт на своём слое,
        /// и его пропажа из счёта была бы поломкой, а не улучшением.
        /// </summary>
        private static void MeasureColliders(StringBuilder report)
        {
            int solid = 0;
            int triggers = 0;
            int barrier = 0;

            int ground = LayerMask.NameToLayer("Ground");
            int cover = LayerMask.NameToLayer("Cover");
            int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");

            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (collider.isTrigger)
                {
                    triggers++;
                    continue;
                }

                int layer = collider.gameObject.layer;
                if (layer == ground || layer == cover)
                {
                    solid++;
                }
                else if (layer == ignoreRaycast)
                {
                    barrier++;
                }
            }

            report.Append("\n\n— Коллайдеры —");
            report.Append("\n  сплошных на Ground/Cover: ").Append(solid).Append(" (до арта было 39)")
                .Append(Mark(solid == 39));
            report.Append("\n  барьер очереди:           ").Append(barrier).Append(Mark(barrier == 1));
            report.Append("\n  триггеров:                ").Append(triggers);
        }

        private static void MeasureMeshes(GameObject arena, StringBuilder report)
        {
            int total = 0;
            int visible = 0;
            long triangles = 0;

            foreach (var renderer in arena.GetComponentsInChildren<MeshRenderer>(true))
            {
                total++;
                if (!renderer.enabled)
                {
                    continue;
                }

                visible++;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    triangles += filter.sharedMesh.triangles.Length / 3;
                }
            }

            report.Append("\n\n— Меши —");
            report.Append("\n  рендереров всего:         ").Append(total);
            report.Append("\n  видимых:                  ").Append(visible);
            report.Append("\n  погашено под дресс:       ").Append(total - visible);
            report.Append("\n  треугольников видимых:    ").Append(triangles);
        }

        private static Vector3 Round(Vector3 value)
        {
            return new Vector3(
                Mathf.Round(value.x * 1000f) / 1000f,
                Mathf.Round(value.y * 1000f) / 1000f,
                Mathf.Round(value.z * 1000f) / 1000f);
        }

        private static bool TryWorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private static string Mark(bool ok)
        {
            return ok ? " ✔" : " ✘";
        }

        private static MemoryRunConfig FindConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:MemoryRunConfig");
            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<MemoryRunConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
