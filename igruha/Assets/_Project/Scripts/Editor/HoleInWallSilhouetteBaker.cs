using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.HoleInWall;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Пекарь контуров вырезов: снимает силуэт позы с кожи персонажа
    /// и превращает его в ломаную, по которой стена режет полотно.
    ///
    /// Меню: <c>Igruha/Дырка в стене/Испечь силуэты вырезов</c>. Тем же
    /// проходом это делает и билдер клипов поз — контур обязан совпадать
    /// с позой, которую персонаж принимает.
    /// </summary>
    /// <remarks>
    /// <b>Почему через битмап, а не по вершинам.</b> Силуэт — это тень модели,
    /// а не выпуклая оболочка: у него есть просвет между ног и между рукой
    /// и телом. Обвести его по вершинам нельзя — порядка у них нет. Битмап
    /// отвечает на всё сразу: залить просветы, раздуть на запас, обвести
    /// внешний край.
    ///
    /// <b>Запас — дилатация битмапа, а не масштаб контура.</b> Масштабом
    /// у Шланги ростом 1.96 м запас вышел бы на четверть больше, чем у Карлана
    /// ростом 1.61 м, хотя протискиваются они одинаково. Дилатация даёт
    /// <see cref="Margin"/> метров всем.
    ///
    /// <b>Внутренние дырки не хранятся.</b> Просвет между ног дал бы в стене
    /// столбик, между которым надо протискиваться; заливка убирает его ещё
    /// здесь. Низ силуэта заодно замыкается по нижней строке: пол платформы
    /// сплошной, и вырез обязан доходить до него целиком.
    ///
    /// <b>Пары здесь нет и быть не должно.</b> Короткое время вырез пары
    /// пёкся объединением двух силуэтов — и это оказалось нерабочим на вид:
    /// у высокого и низкого руки оказываются на разной высоте, объединение
    /// даёт дырку с четырьмя руками, и в стене это читается как клякса, а не
    /// как поза. Вырез закреплён за игроком, и контур ему нужен ровно один —
    /// собственный. Восемь персонажей плюс ростерный запасной, по четыре
    /// позы каждый.
    /// </remarks>
    internal static class HoleInWallSilhouetteBaker
    {
        private const string SilhouettesPath =
            "Assets/_Project/Settings/Gameplay/Minigames/HoleInWallSilhouettes.asset";

        private const string ConfigPath =
            "Assets/_Project/Settings/Gameplay/Minigames/HoleInWallConfig.asset";

        /// <summary>
        /// Запас по контуру, м: на столько вырез шире силуэта в каждую сторону.
        ///
        /// 0.11 м — это чуть больше ладони. Меньше — и дрожь позы (амплитуда
        /// около двух сантиметров на кисти) начинает выпирать за край; больше —
        /// и вырез перестаёт читаться силуэтом, ради чего всё и делалось.
        /// </summary>
        private const float Margin = 0.11f;

        /// <summary>
        /// Допуск упрощения ломаной Дугласом–Пойкером, м.
        ///
        /// Сантиметр снимает пиксельную лесенку и оставляет силуэт: контур
        /// выходит в 40–80 точек вместо полутысячи. На столько же упрощение
        /// имеет право срезать угол внутрь, поэтому раздуваем силуэт
        /// на <see cref="Margin"/> + этот допуск — иначе обещанный запас
        /// в узких местах съело бы упрощение.
        /// </summary>
        private const float SimplifyTolerance = 0.01f;

        /// <summary>Сторона пикселя битмапа, м. 5 мм — вчетверо мельче допуска упрощения.</summary>
        private const float PixelSize = 0.005f;

        /// <summary>Полуширина кадра битмапа, м. Самая широкая поза достаёт на 1.11 м.</summary>
        private const float FrameHalfWidth = 2f;

        /// <summary>Высота кадра битмапа, м. Самая высокая поза — 2.38 м.</summary>
        private const float FrameHeight = 3f;

        [MenuItem("Igruha/Дырка в стене/Испечь силуэты вырезов")]
        internal static void BakeAll()
        {
            string[] prefabs = HoleInWallPoseClipBuilder.PrefabNames;
            string[] names = HoleInWallPoseClipBuilder.CharacterNames;
            int poses = HoleInWallPoseClipBuilder.PoseCount;

            var pipeline = new SilhouettePipeline(FrameHalfWidth, FrameHeight, PixelSize, Margin, SimplifyTolerance);
            var report = new System.Text.StringBuilder();
            report.AppendLine($"HoleInWallSilhouetteBaker: запас {Margin:F2} м, допуск {SimplifyTolerance:F3} м, " +
                              $"пиксель {PixelSize * 1000f:F0} мм, кадр {pipeline.Width}×{pipeline.Height}");

            // Сырые силуэты: персонаж × поза. Держим их все, потому что
            // из них собираются составы — 28 пар и ростер.
            var raw = new bool[names.Length][][];
            var keys = new string[names.Length];

            for (int c = 0; c < names.Length; c++)
            {
                raw[c] = CaptureCharacter(prefabs[c], names[c], pipeline, poses, out keys[c], report);
            }

            var shapes = new List<CutoutSilhouette>(64);
            var union = new bool[pipeline.Length];

            for (int c = 0; c < names.Length; c++)
            {
                if (raw[c] != null)
                {
                    Emit(shapes, pipeline, keys[c], raw[c], poses, report);
                }
            }

            EmitRoster(shapes, pipeline, raw, union, poses, report);
            Save(shapes, report);

            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Снять четыре силуэта одного персонажа. Персонаж поднимается
        /// в превью-сцену: он нужен живым, чтобы поставить позу мышцами
        /// и продавить кожу костями, а открытую сцену геймдизайнера при этом
        /// трогать нельзя.
        /// </summary>
        private static bool[][] CaptureCharacter(string prefabName, string characterName, SilhouettePipeline pipeline,
            int poses, out string key, System.Text.StringBuilder report)
        {
            key = null;
            string path = HoleInWallPoseClipBuilder.PlayerPrefabFolder + prefabName + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"HoleInWallSilhouetteBaker: нет префаба {path}.");
                return null;
            }

            UnityEngine.SceneManagement.Scene preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                var animator = instance.GetComponentInChildren<Animator>(true);
                var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);

                if (animator == null || animator.avatar == null || !animator.isHuman || skin == null ||
                    skin.sharedMesh == null)
                {
                    Debug.LogError($"HoleInWallSilhouetteBaker ({characterName}): нет Humanoid-аватара или скиннед-меша.");
                    return null;
                }

                key = CutoutShapes.KeyOf(instance);
                if (string.IsNullOrEmpty(key))
                {
                    Debug.LogError($"HoleInWallSilhouetteBaker ({characterName}): у аватара нет контроллера аниматора — " +
                                   "состав дорожки будет нечем опознать.");
                    return null;
                }

                var measurer = new HoleInWallPoseClipBuilder.PoseMeasurer(animator, skin);
                var grids = new bool[poses][];
                int outside = 0;

                for (int p = 0; p < poses; p++)
                {
                    measurer.PlaceOnGround(HoleInWallPoseClipBuilder.MusclesOf(p));
                    measurer.RefreshBones();

                    var grid = new bool[pipeline.Length];
                    for (int v = 0; v < measurer.VertexCount; v++)
                    {
                        Vector3 world = measurer.WorldVertex(v);

                        // Поза посажена на пол обмером по каждой одиннадцатой
                        // вершине, поэтому пропущенные могут уйти на миллиметры
                        // ниже нуля. Для выреза это ничего не значит — низ
                        // выреза и есть пол, — поэтому такую вершину прижимаем
                        // к полу, а не теряем.
                        if (pipeline.TryPixel(world.x, Mathf.Max(0f, world.y), out int index))
                        {
                            grid[index] = true;
                        }
                        else
                        {
                            outside++;
                        }
                    }

                    grids[p] = grid;
                }

                report.AppendLine($"  снят {characterName,-8} ключ {key,-16} вершин {measurer.VertexCount}" +
                                  (outside > 0 ? $" ⚠️ вне кадра {outside}" : string.Empty));

                if (outside > 0)
                {
                    Debug.LogError($"HoleInWallSilhouetteBaker ({characterName}): {outside} вершин вышли за кадр битмапа — " +
                                   "силуэт обрезан, расширить FrameHalfWidth/FrameHeight.");
                }

                return grids;
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        /// <summary>Испечь четыре позы одного персонажа.</summary>
        private static void Emit(List<CutoutSilhouette> shapes, SilhouettePipeline pipeline, string character,
            bool[][] raw, int poses, System.Text.StringBuilder report)
        {
            for (int p = 0; p < poses; p++)
            {
                pipeline.LoadRaw(raw[p]);
                Append(shapes, pipeline, character, p, report);
            }
        }

        /// <summary>
        /// Испечь ростерный вырез: объединение всех восьмерых. Это запасной
        /// вырез — тот самый «на самого большого», который до 04.09 был
        /// единственным. Достаётся игроку, чей персонаж не опознан. Кляксой
        /// он и выглядит, поэтому в игре его быть не должно: увидели —
        /// значит ключ персонажа не нашёлся, и это ошибка, а не оформление.
        /// </summary>
        private static void EmitRoster(List<CutoutSilhouette> shapes, SilhouettePipeline pipeline, bool[][][] raw,
            bool[] union, int poses, System.Text.StringBuilder report)
        {
            for (int p = 0; p < poses; p++)
            {
                System.Array.Clear(union, 0, union.Length);
                bool any = false;

                for (int c = 0; c < raw.Length; c++)
                {
                    if (raw[c] == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < union.Length; i++)
                    {
                        union[i] |= raw[c][p][i];
                    }

                    any = true;
                }

                if (any)
                {
                    pipeline.LoadRaw(union);
                    Append(shapes, pipeline, HoleInWallSilhouettes.RosterCharacter, p, report);
                }
            }
        }

        private static void Append(List<CutoutSilhouette> shapes, SilhouettePipeline pipeline, string character,
            int poseIndex, System.Text.StringBuilder report)
        {
            Vector2[] chain = pipeline.Extract(out float clearance);
            if (chain == null)
            {
                Debug.LogError($"HoleInWallSilhouetteBaker: контур персонажа «{character}», " +
                               $"поза {HoleInWallPoseClipBuilder.PoseTitles[poseIndex]} не построен.");
                return;
            }

            float left = 0f;
            float right = 0f;
            float height = 0f;
            for (int i = 0; i < chain.Length; i++)
            {
                left = Mathf.Min(left, chain[i].x);
                right = Mathf.Max(right, chain[i].x);
                height = Mathf.Max(height, chain[i].y);
            }

            var shape = new CutoutSilhouette();
            shape.Bake(character, poseIndex + 1, chain, height, left, right);
            shapes.Add(shape);

            // Ростерный состав печатаем всегда, остальные — только если запас
            // не выдержан: 148 строк в консоли не читает никто.
            bool suspicious = clearance < Margin - SimplifyTolerance;
            if (suspicious || character == HoleInWallSilhouettes.RosterCharacter)
            {
                report.AppendLine(
                    $"  {character,-18} {HoleInWallPoseClipBuilder.PoseTitles[poseIndex],-9} " +
                    $"точек {chain.Length,3} H={height:F3} x[{left:F2}…{right:F2}] запас {clearance:F3}" +
                    (suspicious ? " ⚠️" : string.Empty));
            }

            if (suspicious)
            {
                Debug.LogWarning($"HoleInWallSilhouetteBaker: у персонажа «{character}» " +
                                 $"поза {HoleInWallPoseClipBuilder.PoseTitles[poseIndex]} прошла с запасом " +
                                 $"{clearance:F3} м вместо {Margin:F2} — контур где-то поджат.");
            }
        }

        private static void Save(List<CutoutSilhouette> shapes, System.Text.StringBuilder report)
        {
            var asset = AssetDatabase.LoadAssetAtPath<HoleInWallSilhouettes>(SilhouettesPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<HoleInWallSilhouettes>();
                AssetDatabase.CreateAsset(asset, SilhouettesPath);
            }

            asset.Bake(shapes.ToArray(), Margin);
            EditorUtility.SetDirty(asset);

            var config = AssetDatabase.LoadAssetAtPath<HoleInWallConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogWarning($"HoleInWallSilhouetteBaker: конфига нет по пути {ConfigPath} — контуры подключить некуда.");
            }
            else
            {
                // Ссылку проставляем сами: генерируемый ассет — не то, что
                // человек должен подключать мышью.
                var serialized = new SerializedObject(config);
                SerializedProperty property = serialized.FindProperty("silhouettes");
                if (property != null)
                {
                    property.objectReferenceValue = asset;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(config);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            report.AppendLine($"  испечено контуров: {shapes.Count}");
        }
    }
}
