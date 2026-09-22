using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Закрашивает чёрные подошвы Толстого: расползает кожу с краёв стопы
    /// внутрь пятна, которого генератор не нарисовал.
    ///
    /// <b>Что было.</b> Модель Толстого пришла из Tripo, и низа стопы камеры
    /// не видели ни разу — в атласе на месте подошв осталось чёрное пятно
    /// (яркость 0.16 при 0.69 у ладони, 22% текселей ступни). Геометрия
    /// целая, нормали целые, дело только в цвете: подошва, повёрнутая к
    /// камере, выглядит дырой, будто ногу отрезали. Толстый единственный
    /// босой, поэтому видно это только на нём — и видно постоянно, потому
    /// что на восьмом танце он катается по полу ступнями к зрителю.
    ///
    /// <b>Почему только он.</b> У остальных семи на ногах кроссовки, и
    /// тёмная подошва там — рисунок, а не пропуск. Общий проход по ростеру
    /// закрасил бы её кожей.
    ///
    /// <b>Как чинится.</b> Тексели, которые держат вершины стопы и пальцев,
    /// собираются в маску; тёмные из них гасятся и заполняются размыванием
    /// от светлого края внутрь — так пятно затягивается тем же цветом, что
    /// у подъёма стопы, без видимой границы. Правится файл-источник, сетка
    /// и материал не трогаются вовсе.
    ///
    /// Прогонять заново после замены модели или текстуры Толстого. Повторный
    /// запуск по уже починенной текстуре ничего не делает: тёмных текселей
    /// в маске не остаётся.
    /// </summary>
    internal static class CharacterSoleTextureRepair
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Player/Fat.prefab";

        /// <summary>
        /// Ниже этой яркости тексель считается незакрашенным — и как дыра,
        /// и как негодный образец. Кожа Толстого держится на 0.55–0.65, дно
        /// пятна на 0.15, а между ними лежит тёмная кайма: с порогом по
        /// самому дну заливка затягивала пятно этой каймой и оставалась
        /// почти чёрной.
        /// </summary>
        private const float DarkLuminance = 0.45f;

        /// <summary>
        /// Радиус пятна вокруг вершины, которым набирается маска, текселей.
        /// Берётся с запасом за край острова: на мелких мип-уровнях в кожу
        /// подмешивается соседний фон, и если его не закрасить, подошва
        /// издали снова темнеет.
        /// </summary>
        private const int MaskRadius = 6;

        /// <summary>Больше этого числа проходов размывание не делает — пятно к тому моменту затянуто.</summary>
        private const int MaxFillPasses = 600;

        /// <summary>
        /// Сколько раз закрашенное потом сглаживается. Заливка идёт кольцами
        /// от края внутрь и тянет за собой лучи — каждая неровность края
        /// расчёсывается в полосу до середины подошвы. Сглаживание их
        /// убирает, а края пятна не трогает: там лежит настоящая кожа.
        /// </summary>
        private const int SmoothPasses = 24;

        /// <summary>
        /// Качество записи JPEG. В игру текстура всё равно приходит сжатой до
        /// 2048 и DXT1, так что качество отвечает только за вес файла в
        /// репозитории: 98 даёт 1.6 МБ, 92 — вдвое меньше при той же
        /// картинке. Файлы этой папки идут через LFS, и лишний мегабайт
        /// остаётся в истории навсегда.
        /// </summary>
        private const int JpegQuality = 92;

        /// <summary>Кости, вершины под которыми считаются стопой.</summary>
        private static readonly HumanBodyBones[] FootBones =
        {
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftToes, HumanBodyBones.RightToes
        };

        [MenuItem("Igruha/Персонажи/Закрасить чёрные подошвы Толстого")]
        public static void Repair()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"Подошвы: нет префаба {PrefabPath}");
                return;
            }

            // Кости человека знает только живой аниматор: на ассете префаба
            // GetBoneTransform ничего не находит.
            var instance = (GameObject)Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                RepairInstance(instance);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void RepairInstance(GameObject instance)
        {
            var animator = instance.GetComponentInChildren<Animator>(true);
            var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (animator == null || !animator.isHuman || skin == null || skin.sharedMesh == null)
            {
                Debug.LogError("Подошвы: у Толстого нет человеческого аниматора или сетки");
                return;
            }

            Material material = skin.sharedMaterials.Length > 0 ? skin.sharedMaterials[0] : null;
            Texture main = material != null ? material.mainTexture : null;
            string path = main != null ? AssetDatabase.GetAssetPath(main) : null;
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("Подошвы: у материала Толстого нет основной текстуры");
                return;
            }

            // Импортированная копия сжата и не читается, а файл-источник
            // читается всегда — правим именно его.
            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Debug.LogError($"Подошвы: не читается {path}");
                Object.DestroyImmediate(texture);
                return;
            }

            try
            {
                int filled = Fill(texture, Mask(skin, animator, texture.width, texture.height));
                if (filled == 0)
                {
                    Debug.Log("Подошвы: закрашивать нечего — тёмных текселей на ступнях нет");
                    return;
                }

                File.WriteAllBytes(path, texture.EncodeToJPG(JpegQuality));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Debug.Log($"Подошвы: закрашено {filled} текселей в {Path.GetFileName(path)}");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// Тексели, на которых лежит кожа стопы. Вершин у модели под миллион,
        /// остров они закрывают плотно, поэтому маска набирается пятнами
        /// вокруг их UV, а не растеризацией треугольников.
        /// </summary>
        private static bool[] Mask(SkinnedMeshRenderer skin, Animator animator, int width, int height)
        {
            Mesh mesh = skin.sharedMesh;
            Transform[] bones = skin.bones;
            var foot = new HashSet<int>();
            foreach (HumanBodyBones bone in FootBones)
            {
                Transform transform = animator.GetBoneTransform(bone);
                int index = transform == null ? -1 : System.Array.IndexOf(bones, transform);
                if (index >= 0)
                {
                    foot.Add(index);
                }
            }

            var mask = new bool[width * height];
            Vector2[] uv = mesh.uv;
            BoneWeight[] weights = mesh.boneWeights;
            for (int v = 0; v < uv.Length; v++)
            {
                if (!foot.Contains(DominantBone(weights[v])))
                {
                    continue;
                }

                int x = Mathf.Clamp(Mathf.RoundToInt(uv[v].x * width), 0, width - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(uv[v].y * height), 0, height - 1);
                for (int dy = -MaskRadius; dy <= MaskRadius; dy++)
                {
                    int py = y + dy;
                    if (py < 0 || py >= height)
                    {
                        continue;
                    }

                    for (int dx = -MaskRadius; dx <= MaskRadius; dx++)
                    {
                        int px = x + dx;
                        if (px >= 0 && px < width)
                        {
                            mask[py * width + px] = true;
                        }
                    }
                }
            }

            return mask;
        }

        /// <summary>
        /// Затянуть тёмные тексели маски цветом со светлого края. Каждый
        /// проход берёт те, у кого уже есть светлый сосед, и усредняет его:
        /// пятно зарастает кольцами снаружи внутрь, границы не видно.
        /// Возвращает, сколько текселей закрашено.
        ///
        /// Образцом идёт только светлое, где бы оно ни лежало. Тёмное вокруг
        /// острова — фон атласа, и если брать цвет у него, пятно «зарастает»
        /// той же чернотой: ровно на этом первый заход и закрасил 325 тысяч
        /// текселей, не изменив картинку.
        /// </summary>
        private static int Fill(Texture2D texture, bool[] mask)
        {
            int width = texture.width;
            int height = texture.height;
            Color32[] pixels = texture.GetPixels32();
            var dark = new bool[pixels.Length];
            var queue = new List<int>();
            for (int i = 0; i < pixels.Length; i++)
            {
                if (Luminance(pixels[i]) >= DarkLuminance)
                {
                    continue;
                }

                dark[i] = true;
                if (mask[i])
                {
                    queue.Add(i);
                }
            }

            if (queue.Count == 0)
            {
                return 0;
            }

            var painted = new List<int>();
            var ready = new List<int>();
            var colors = new List<Color32>();
            for (int pass = 0; pass < MaxFillPasses && queue.Count > 0; pass++)
            {
                ready.Clear();
                colors.Clear();
                foreach (int index in queue)
                {
                    int x = index % width;
                    int y = index / width;
                    int r = 0, g = 0, b = 0, count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int py = y + dy;
                        if (py < 0 || py >= height)
                        {
                            continue;
                        }

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int px = x + dx;
                            int neighbour = py * width + px;
                            if (px < 0 || px >= width || dark[neighbour])
                            {
                                continue;
                            }

                            Color32 color = pixels[neighbour];
                            r += color.r;
                            g += color.g;
                            b += color.b;
                            count++;
                        }
                    }

                    if (count == 0)
                    {
                        continue;
                    }

                    ready.Add(index);
                    colors.Add(new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255));
                }

                if (ready.Count == 0)
                {
                    break;
                }

                // Цвет пишется общим заходом: иначе тексель, закрашенный в
                // этом же проходе, стал бы образцом для соседа, и пятно
                // затягивало бы полосами по направлению обхода.
                for (int i = 0; i < ready.Count; i++)
                {
                    pixels[ready[i]] = colors[i];
                    dark[ready[i]] = false;
                }

                painted.AddRange(ready);
                queue.RemoveAll(index => !dark[index]);
            }

            Smooth(pixels, painted, width, height);
            texture.SetPixels32(pixels);
            texture.Apply();
            return painted.Count;
        }

        /// <summary>Расчесать лучи заливки: усреднение по соседям, только по закрашенному.</summary>
        private static void Smooth(Color32[] pixels, List<int> painted, int width, int height)
        {
            var colors = new Color32[painted.Count];
            for (int pass = 0; pass < SmoothPasses; pass++)
            {
                for (int i = 0; i < painted.Count; i++)
                {
                    int x = painted[i] % width;
                    int y = painted[i] / width;
                    int r = 0, g = 0, b = 0, count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int py = y + dy;
                        if (py < 0 || py >= height)
                        {
                            continue;
                        }

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int px = x + dx;
                            if (px < 0 || px >= width)
                            {
                                continue;
                            }

                            Color32 color = pixels[py * width + px];
                            r += color.r;
                            g += color.g;
                            b += color.b;
                            count++;
                        }
                    }

                    colors[i] = new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255);
                }

                for (int i = 0; i < painted.Count; i++)
                {
                    pixels[painted[i]] = colors[i];
                }
            }
        }

        private static float Luminance(Color32 color)
        {
            return (color.r * 0.3f + color.g * 0.6f + color.b * 0.1f) / 255f;
        }

        private static int DominantBone(BoneWeight w)
        {
            int bone = w.boneIndex0;
            float weight = w.weight0;

            if (w.weight1 > weight)
            {
                bone = w.boneIndex1;
                weight = w.weight1;
            }

            if (w.weight2 > weight)
            {
                bone = w.boneIndex2;
                weight = w.weight2;
            }

            if (w.weight3 > weight)
            {
                bone = w.boneIndex3;
            }

            return bone;
        }
    }
}
