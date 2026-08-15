using UnityEngine;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Статуя на месте окаменения: перекрывает луч как обычное укрытие.
    /// Живёт по флагу конфига и по умолчанию выключена — риск понятен и
    /// проверен на бумаге: к концу раунда укрытий становится вдвое больше,
    /// и Водящий слабеет ровно тогда, когда должен додавливать. Включаем
    /// на плейтесте, если игра окажется слишком тяжёлой для Бегущих.
    ///
    /// Геометрия — серый блок по капсуле окаменевшего: настоящая модель
    /// статуи приезжает в арт-фазе (IGR-291).
    /// </summary>
    public sealed class PetrifiedStatue : MonoBehaviour
    {
        /// <summary>
        /// Собрать статую по капсуле игрока. Высота берётся его собственная:
        /// статуя обязана перекрывать луч ровно настолько, насколько перекрывал
        /// бы стоящий человек, иначе укрытие врёт.
        /// </summary>
        public static PetrifiedStatue Create(CapsuleCollider source, int coverLayer, Transform parent)
        {
            if (source == null)
            {
                return null;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PetrifiedStatue";
            go.layer = coverLayer;
            go.transform.SetParent(parent, false);

            float height = source.height;
            float width = source.radius * 2f;
            go.transform.position = source.transform.position + Vector3.up * (height * 0.5f);
            go.transform.rotation = source.transform.rotation;
            go.transform.localScale = new Vector3(width, height, width);

            return go.AddComponent<PetrifiedStatue>();
        }

        /// <summary>Раунд кончился — арену надо вернуть в исходный вид.</summary>
        public void Remove() => Destroy(gameObject);
    }
}
