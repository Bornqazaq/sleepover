using System;
using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Объём торса персонажа — стопка горизонтальных срезов от таза до шеи,
    /// у каждого среза свой радиус в каждую сторону. По нему
    /// <see cref="CharacterArmClearance"/> видит, что рука ушла внутрь тела.
    ///
    /// <b>Зачем.</b> Все восемь персонажей танцуют одними и теми же клипами —
    /// <c>Shlanga@dance1..8</c>. Замер 22.09 на живом аниматоре: у Shlanga
    /// кости рук не заходят в торс ни на одном танце ни на миллиметр, у
    /// остальных заходят до 22 см (Толстый, <c>dance3</c>), потому что тело
    /// у них шире эталонного. Это не ошибка клипа и не коллайдер — это
    /// пересадка движения худого тела на толстое.
    ///
    /// <b>Почему срезами, а не капсулой.</b> Торс не круглый: у Толстого
    /// спина на 42 см от оси, а грудь на 33. Одна капсула либо пропускает
    /// руку в спину, либо выталкивает её из груди туда, где тела нет.
    /// Срез хранит радиус по восьми секторам, между ними значение берётся
    /// линейно.
    ///
    /// <b>Радиус — вписанный, а не описанный.</b> В каждой ячейке берётся
    /// нижний квинтиль расстояний до вершин, то есть поверхность в самом
    /// узком месте сектора. Так объём заведомо внутри тела: рука, стоящая на
    /// его границе, снаружи тела не висит. Цена — часть провала остаётся, но
    /// «рука пропала внутри живота» превращается в «рука прижата к животу».
    ///
    /// <b>Здесь же толщина руки.</b> Кость руки идёт по её середине, и рука,
    /// поставленная костью ровно на поверхность живота, всё равно наполовину
    /// в нём. У Толстого предплечье толщиной 9.6 см, плечо 12.7, кисть 7.7 —
    /// у Шланги те же 3.9, 6.2 и 5.8. Без этой поправки доворот считает
    /// работу сделанной там, где половина руки ещё внутри тела.
    ///
    /// <b>Зачем отдельный ассет.</b> Как и у <see cref="CharacterSkinProbes"/>:
    /// сетка персонажа в сборке не читается, а в редакторе читается. Срезы
    /// считаются один раз пунктом меню «Igruha/Персонажи/Собрать объём торса»,
    /// ключ записи — сама сетка, потому что префабы персонажей заморожены и
    /// ссылку в них не положить.
    ///
    /// Пересобирать при замене модели персонажа. Тест
    /// <c>DanceArmClearanceTests.EveryCharacterHasTorsoShape</c> напомнит.
    /// </summary>
    [CreateAssetMenu(fileName = ResourceName, menuName = "Igruha/Character Torso Shape")]
    public sealed class CharacterTorsoShape : ScriptableObject
    {
        /// <summary>Имя ассета в Resources.</summary>
        public const string ResourceName = "CharacterTorsoShape";

        /// <summary>Секторов в срезе: радиус задаётся через каждые 45°.</summary>
        public const int SectorCount = 8;

        /// <summary>Градусов на сектор.</summary>
        private const float SectorAngle = 360f / SectorCount;

        /// <summary>
        /// Один горизонтальный срез торса. Живёт в локальном пространстве
        /// кости позвоночника, к которой привязан: гнётся спина — едет и срез.
        /// </summary>
        [Serializable]
        public struct Slice
        {
            [Tooltip("Индекс кости в SkinnedMeshRenderer.bones, к которой привязан срез")]
            public int Bone;

            [Tooltip("Середина среза в локальном пространстве кости")]
            public Vector3 Center;

            [Tooltip("Направление вверх по позвоночнику, в локальном пространстве кости")]
            public Vector3 Axis;

            [Tooltip("Направление влево, в локальном пространстве кости — от него считаются секторы")]
            public Vector3 Side;

            [Tooltip("Направление вперёд, в локальном пространстве кости")]
            public Vector3 Front;

            [Tooltip("Половина высоты среза, м")]
            public float HalfHeight;

            [Tooltip("Радиусы по секторам против часовой стрелки от Side, м")]
            public float[] Radii;

            /// <summary>
            /// Радиус тела в направлении, заданном проекциями на
            /// <see cref="Side"/> и <see cref="Front"/>, м. Между секторами —
            /// линейно, иначе граница тела ступенчатая и рука дёргается на
            /// переходе.
            /// </summary>
            public float Radius(float side, float front)
            {
                if (Radii == null || Radii.Length < SectorCount)
                {
                    return 0f;
                }

                float angle = Mathf.Atan2(front, side) * Mathf.Rad2Deg;
                if (angle < 0f)
                {
                    angle += 360f;
                }

                float position = angle / SectorAngle;
                int first = (int)position % SectorCount;
                int second = (first + 1) % SectorCount;
                return Mathf.Lerp(Radii[first], Radii[second], position - (int)position);
            }
        }

        /// <summary>Торс одного персонажа.</summary>
        [Serializable]
        public struct Entry
        {
            [Tooltip("Сетка персонажа — ключ записи")]
            public Mesh Mesh;

            [Tooltip("Сколько костей было у сетки при сборке — сверка скелета")]
            public int BoneCount;

            [Tooltip("Срезы снизу вверх")]
            public Slice[] Slices;

            [Tooltip("Кончик левой ладони в локальном пространстве кости кисти")]
            public Vector3 LeftHandTip;

            [Tooltip("Кончик правой ладони в локальном пространстве кости кисти")]
            public Vector3 RightHandTip;

            [Tooltip("Полутолщина плеча по коже, м")]
            public float UpperArmRadius;

            [Tooltip("Полутолщина предплечья по коже, м")]
            public float LowerArmRadius;

            [Tooltip("Полутолщина кисти по коже, м")]
            public float HandRadius;
        }

        [Tooltip("По записи на персонажа. Собирается пунктом меню Igruha/Персонажи/Собрать объём торса")]
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private static CharacterTorsoShape shared;

        /// <summary>Записи как есть — для редакторного сборщика и проверок.</summary>
        public Entry[] Entries => entries;

        /// <summary>
        /// Общий ассет из Resources. Пока его нет, каждый вызов пробует снова:
        /// зовётся на появлении персонажа, а не каждый кадр.
        /// </summary>
        public static CharacterTorsoShape Shared
        {
            get
            {
                if (shared == null)
                {
                    shared = Resources.Load<CharacterTorsoShape>(ResourceName);
                }

                return shared;
            }
        }

        /// <summary>Заменить все записи. Только для редакторного сборщика.</summary>
        public void Replace(Entry[] value)
        {
            entries = value ?? Array.Empty<Entry>();
        }

        /// <summary>Торс персонажа с этой сеткой. Ложь — записи нет.</summary>
        public bool TryGet(Mesh mesh, out Entry entry)
        {
            if (mesh != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].Mesh == mesh)
                    {
                        entry = entries[i];
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }
    }
}
