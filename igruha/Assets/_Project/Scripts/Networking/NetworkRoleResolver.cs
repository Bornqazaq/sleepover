using System;
using System.Collections.Generic;
using Unity.Multiplayer.PlayMode;
using UnityEngine;

namespace Igruha.Networking
{
    /// <summary>Роль, с которой инстанс поднимает сеть при старте.</summary>
    public enum NetworkStartRole
    {
        Host,
        Client
    }

    /// <summary>
    /// Решает, поднимать инстанс хостом или клиентом.
    ///
    /// Три источника, в порядке убывания приоритета:
    /// 1. Аргумент запуска <c>--client</c> — сценарий с билдами (см.
    ///    <see cref="NetworkLaunchArguments"/>).
    /// 2. Тег Multiplayer Play Mode («Host» / «Client») — виртуальные игроки в редакторе.
    /// 3. Признак «это не главный редактор» — виртуальный игрок без тега.
    ///
    /// Аргументов командной строки у виртуального игрока нет, поэтому одного
    /// <c>--client</c> для Play Mode недостаточно (IGR-275, пункт 3).
    /// </summary>
    public static class NetworkRoleResolver
    {
        public const string HostTag = "Host";
        public const string ClientTag = "Client";

        public static NetworkStartRole Resolve(out string reason)
        {
            if (NetworkLaunchArguments.HasClientFlag())
            {
                reason = $"аргумент запуска {NetworkLaunchArguments.ClientFlag}";
                return NetworkStartRole.Client;
            }

            // Виртуальные игроки существуют только в редакторе; в билде
            // CurrentPlayer отвечает значениями главного редактора.
            if (!Application.isEditor)
            {
                reason = "билд без аргументов";
                return NetworkStartRole.Host;
            }

            if (TryResolveByPlayModeTag(out NetworkStartRole taggedRole, out string tag))
            {
                reason = $"тег Play Mode «{tag}»";
                return taggedRole;
            }

            if (!CurrentPlayer.IsMainEditor)
            {
                reason = "виртуальный игрок без тега";
                return NetworkStartRole.Client;
            }

            reason = "главный редактор";
            return NetworkStartRole.Host;
        }

        private static bool TryResolveByPlayModeTag(out NetworkStartRole role, out string matchedTag)
        {
            IReadOnlyList<string> tags = CurrentPlayer.Tags;
            if (tags != null)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    string tag = tags[i];
                    if (string.Equals(tag, ClientTag, StringComparison.OrdinalIgnoreCase))
                    {
                        role = NetworkStartRole.Client;
                        matchedTag = tag;
                        return true;
                    }

                    if (string.Equals(tag, HostTag, StringComparison.OrdinalIgnoreCase))
                    {
                        role = NetworkStartRole.Host;
                        matchedTag = tag;
                        return true;
                    }
                }
            }

            role = NetworkStartRole.Host;
            matchedTag = null;
            return false;
        }
    }
}
