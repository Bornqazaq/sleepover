using Unity.Netcode.Components;
using UnityEngine;

namespace Igruha.Networking
{
    /// <summary>
    /// NetworkAnimator с авторитетом владельца.
    ///
    /// Анимации гонит CharacterAnimatorDriver на машине владельца, поэтому
    /// реплицировать нужно от него. Сервер-авторитетный вариант затирал бы
    /// параметры Animator у владельца состоянием со сервера.
    /// </summary>
    [DisallowMultipleComponent]
    public class OwnerNetworkAnimator : NetworkAnimator
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
