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
        /// Чья это статуя. Нужно, чтобы снять её вместе с ушедшим игроком:
        /// иначе на арене остаётся укрытие от того, кого в матче уже нет.
        /// </summary>
        public int OwnerId { get; private set; } = -1;

        /// <summary>
        /// Собрать статую по капсуле игрока. Высота берётся его собственная:
        /// статуя обязана перекрывать луч ровно настолько, насколько перекрывал
        /// бы стоящий человек, иначе укрытие врёт.
        /// </summary>
        public static PetrifiedStatue Create(CapsuleCollider source, int coverLayer, Transform parent, int ownerId)
        {
            if (source == null)
            {
                return null;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PetrifiedStatue";
            go.layer = coverLayer;
            go.transform.SetParent(parent, false);

            // Без этого статуя в СБОРКЕ фиолетовая. CreatePrimitive вешает
            // встроенный Default-Material, его шейдер не из URP: в редакторе
            // он есть всегда, в билд не попадает, и на его месте оказывается
            // заглушка «шейдер потерян». Поймано на живом прогоне 24.08
            // у «Порядка банок» — здесь ровно та же мина.
            if (go.TryGetComponent(out Renderer renderer))
            {
                renderer.sharedMaterial = StatueMaterial;
            }

            float height = source.height;
            float width = source.radius * 2f;
            go.transform.position = source.transform.position + Vector3.up * (height * 0.5f);
            go.transform.rotation = source.transform.rotation;
            go.transform.localScale = new Vector3(width, height, width);

            PetrifiedStatue statue = go.AddComponent<PetrifiedStatue>();
            statue.OwnerId = ownerId;
            return statue;
        }

        /// <summary>
        /// Материал статуи. Один общий на все статуи раунда — они одинаковые,
        /// и копии материала множили бы вызовы отрисовки.
        ///
        /// ⚠️ Такой же статический материал заведён в <c>CanShelf</c>: две
        /// мини-игры лепят примитивы в рантайме и обе спотыкались об одно.
        /// Общее место этому — утилита в <c>Core</c>, но Core закрыт пунктом
        /// DoD эпика «Порядка банок», поэтому пока по экземпляру на игру.
        /// </summary>
        private static Material statueMaterial;

        private static Material StatueMaterial
        {
            get
            {
                if (statueMaterial != null)
                {
                    return statueMaterial;
                }

                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogError("PetrifiedStatue: шейдер 'Universal Render Pipeline/Lit' не найден — " +
                                   "статуи будут нарисованы заглушкой");
                    return null;
                }

                statueMaterial = new Material(shader) { name = "PetrifiedStatue (runtime)" };
                return statueMaterial;
            }
        }

        /// <summary>Раунд кончился — арену надо вернуть в исходный вид.</summary>
        public void Remove() => Destroy(gameObject);
    }
}
