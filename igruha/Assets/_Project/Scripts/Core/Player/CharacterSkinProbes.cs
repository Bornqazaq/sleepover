using System;
using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Опорные точки кожи персонажей — по нескольку сотен настоящих вершин
    /// сетки на персонажа, с весами костей. По ним <see cref="CharacterFootGrounding"/>
    /// видит, где кожа касается пола: подошва, спина, живот, ладонь.
    ///
    /// <b>Зачем кожа, а не кости.</b> У наших персонажей кость голеностопа
    /// стоит на 11–22 см выше подошвы, а спина и живот в лежачих клипах — ещё
    /// дальше от своих костей: падения записаны Mixamo на худом эталоне, а
    /// Толстый в полтора раза толще. Кость ещё над полом, а подошва и живот
    /// уже в нём — замер 21.09: до 25 см у встающего, до 29 см у лежащего.
    ///
    /// <b>Зачем отдельный ассет.</b> Сетка персонажа — около миллиона вершин,
    /// и в сборке она не читается: Read/Write выключен, а включать его ради
    /// этого — держать в памяти вторую копию каждого персонажа. Точки
    /// отбираются один раз в редакторе (пункт «Igruha/Персонажи/Собрать
    /// опорные точки кожи»), а в игре считаются так же, как их считает
    /// видеокарта: по позам костей и весам.
    ///
    /// Ключ — сама сетка. Префабы персонажей заморожены, и ссылку на данные
    /// в них не положить; сетка же у каждого своя и находится на любой копии.
    /// Персонаж без записи не ломается — подъём остаётся по костям ступней.
    /// </summary>
    [CreateAssetMenu(fileName = ResourceName, menuName = "Igruha/Character Skin Probes")]
    public sealed class CharacterSkinProbes : ScriptableObject
    {
        /// <summary>Имя ассета в Resources.</summary>
        public const string ResourceName = "CharacterSkinProbes";

        /// <summary>
        /// Частей тела, под каждой из которых пол ищется отдельно: левая нога,
        /// правая нога, левая рука, правая рука, туловище с головой. На склоне
        /// у ног разный пол, и искать его под одной точкой нельзя.
        /// </summary>
        public const int GroupCount = 5;

        /// <summary>Сколько костей держит одна вершина — столько же, сколько у видеокарты.</summary>
        public const int BonesPerProbe = 4;

        /// <summary>Бит на индекс кости в упакованном поле: 8 бит — до 256 костей.</summary>
        private const int BoneIndexBits = 8;

        private const int BoneIndexMask = (1 << BoneIndexBits) - 1;

        /// <summary>Одна вершина кожи.</summary>
        [Serializable]
        public struct Probe
        {
            [Tooltip("Вершина в позе привязки, в пространстве сетки")]
            public Vector3 Position;

            [Tooltip("Веса четырёх костей, по убыванию")]
            public Vector4 Weights;

            [Tooltip("Индексы четырёх костей в SkinnedMeshRenderer.bones, по байту на кость")]
            public int Bones;

            [Tooltip("Часть тела: 0 левая нога, 1 правая нога, 2 левая рука, 3 правая рука, 4 туловище")]
            public int Group;

            /// <summary>Индекс кости в слоте 0–3.</summary>
            public int Bone(int slot) => (Bones >> (slot * BoneIndexBits)) & BoneIndexMask;

            /// <summary>Упаковать четыре индекса костей в одно поле.</summary>
            public static int PackBones(int bone0, int bone1, int bone2, int bone3)
            {
                return (bone0 & BoneIndexMask)
                       | ((bone1 & BoneIndexMask) << BoneIndexBits)
                       | ((bone2 & BoneIndexMask) << (BoneIndexBits * 2))
                       | ((bone3 & BoneIndexMask) << (BoneIndexBits * 3));
            }

            /// <summary>Самый большой индекс кости, который помещается в упаковку.</summary>
            public const int MaxBoneIndex = BoneIndexMask;
        }

        /// <summary>Точки одного персонажа.</summary>
        [Serializable]
        public struct Entry
        {
            [Tooltip("Сетка персонажа — ключ записи")]
            public Mesh Mesh;

            [Tooltip("Позы привязки костей сетки, в порядке SkinnedMeshRenderer.bones")]
            public Matrix4x4[] BindPoses;

            [Tooltip("Отобранные вершины кожи")]
            public Probe[] Probes;
        }

        [Tooltip("По записи на персонажа. Собирается пунктом меню Igruha/Персонажи/Собрать опорные точки кожи")]
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private static CharacterSkinProbes shared;

        /// <summary>Записи как есть — для редакторного сборщика и проверок.</summary>
        public Entry[] Entries => entries;

        /// <summary>
        /// Общий ассет из Resources. Пока его нет, каждый вызов пробует снова:
        /// зовётся на появлении персонажа, а не каждый кадр.
        /// </summary>
        public static CharacterSkinProbes Shared
        {
            get
            {
                if (shared == null)
                {
                    shared = Resources.Load<CharacterSkinProbes>(ResourceName);
                }

                return shared;
            }
        }

        /// <summary>Заменить все записи. Только для редакторного сборщика.</summary>
        public void Replace(Entry[] value)
        {
            entries = value ?? Array.Empty<Entry>();
        }

        /// <summary>Точки персонажа с этой сеткой. Ложь — записи нет.</summary>
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
