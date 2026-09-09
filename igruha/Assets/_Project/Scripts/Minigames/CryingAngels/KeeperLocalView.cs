using UnityEngine;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Что в риге фонаря видно только чужим глазам, а что — только своим.
    ///
    /// Линза фонаря светится ради блума и стоит у самой вершины конуса — там же,
    /// где у Водящего камера от первого лица: для него она заслоняет весь экран
    /// белым шаром. И наоборот: тьма Водящего (пост-обработка, в которой зал
    /// гаснет и остаётся только луч) нужна одному Водящему; Бегущие должны
    /// видеть зал при луне.
    ///
    /// Ссылки — Behaviour и GameObject, а не типы Volume: мини-игры не тянут
    /// сборку RP Core ради одного выключателя.
    /// </summary>
    public sealed class KeeperLocalView : MonoBehaviour
    {
        [Tooltip("Объекты, которые прячутся, когда риг принадлежит локальному игроку")]
        [SerializeField] private GameObject[] hiddenForOwner;
        [Tooltip("Компоненты, которые включаются только у локального Водящего (пост-обработка тьмы)")]
        [SerializeField] private Behaviour[] ownerOnly;

        private void Awake()
        {
            Apply(false);
        }

        public void Apply(bool ownedLocally)
        {
            if (hiddenForOwner != null)
            {
                foreach (GameObject go in hiddenForOwner)
                {
                    if (go != null)
                    {
                        go.SetActive(!ownedLocally);
                    }
                }
            }

            if (ownerOnly != null)
            {
                foreach (Behaviour behaviour in ownerOnly)
                {
                    if (behaviour != null)
                    {
                        behaviour.enabled = ownedLocally;
                    }
                }
            }
        }
    }
}
