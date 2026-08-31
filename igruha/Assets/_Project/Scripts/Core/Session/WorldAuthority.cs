using Unity.Netcode;

namespace Igruha.Core.Session
{
    /// <summary>
    /// «Вправе ли эта машина решать исход» для объектов мира, у которых нет
    /// владельца-игрока: ловушек, снарядов, зон.
    ///
    /// У персонажа тот же вопрос решает <c>PlayerController.HasWorldAuthority</c>
    /// через свой relay, но у ловушки relay взять неоткуда — она не привязана
    /// ни к кому. Отсюда отдельная точка на весь Core.
    ///
    /// Правило: сети нет — решаем сами (одиночный тест сцены должен работать);
    /// сеть есть — решает только сервер.
    /// </summary>
    public static class WorldAuthority
    {
        /// <summary>Запущена ли сетевая сессия. NetworkManager поднимается ещё в сцене Boot.</summary>
        public static bool IsNetworkSession =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        /// <summary>Может ли эта машина менять состояние мира.</summary>
        public static bool HasAuthority => !IsNetworkSession || NetworkManager.Singleton.IsServer;

        /// <summary>
        /// Ведёт ли это тело сама эта машина. Вне сети — всегда: мотор здесь же.
        ///
        /// Вопрос отдельный от <see cref="HasAuthority"/> и задаётся о другом.
        /// Авторитет решает, <b>что произошло</b>, — и это сервер. Владелец
        /// двигает тело, и всё, что напишет в чужую копию любая другая машина,
        /// включая сервер, тут же перетрёт сетевой транспорт. Поэтому
        /// непрерывную силу и чтение ввода спрашивают здесь, а не у авторитета:
        /// иначе выбор был бы между полусотней пакетов в секунду и ничем.
        /// </summary>
        public static bool DrivenHere(NetworkObject body) =>
            body == null || !body.IsSpawned || body.IsOwner;
    }
}
