using Unity.Netcode.Components;
using UnityEngine;

namespace Igruha.Networking
{
    /// <summary>
    /// NetworkTransform с авторитетом владельца (CLAUDE.md 3.3).
    ///
    /// Обычный NetworkTransform сервер-авторитетный: сервер перезаписывает позицию,
    /// и персонаж клиента откатывается назад при попытке двигаться. Владелец должен
    /// двигать себя локально для мгновенного отклика, поэтому авторитет отдаётся ему,
    /// а сервер остаётся источником истины для игровых исходов (счёт, попадания).
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
