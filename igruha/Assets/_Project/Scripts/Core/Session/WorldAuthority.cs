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
    }
}
