using System;
using Igruha.Core.Player;
using Igruha.Core.Session;
using UnityEngine;

namespace Igruha.Core.Hub
{
    /// <summary>Local fallback; the spawned selection service owns network profile requests.</summary>
    public sealed class HubPartyProfiles : MonoBehaviour, IHubPartyProfiles
    {
        public CharacterRoster roster;
        public event Action<string> ProfileResult;
        public IHubPartyProfiles Current => CharacterSelection.Current as IHubPartyProfiles ?? this;
        public int CharacterOf(int id) => SessionScoreboard.Current?.FindPlayer(id)?.CharacterIndex ?? -1;
        public void ChangeOwnName(string value)
        {
            var player = SessionScoreboard.Current?.LocalPlayer;
            if (SessionScoreboard.IsNetworked || player == null || !CanEdit) return;
            if (!PartyDisplayName.TryNormalize(value, out var valid))
            { ProfileResult?.Invoke("Имя: до 14 русских или 20 латинских букв, без специальных знаков."); return; }
            player.DisplayName = valid; LocalPartyProfile.Save(player); ProfileResult?.Invoke("Имя сохранено");
        }
        public static bool CanEdit => ConsoleMenu.Active != null && ConsoleMenu.Active.IsOpen &&
            ConsoleMenu.Active.Page == ConsolePage.Party && !Minigame.PartySeries.Active;
        public void ChangeOwnCharacter(int index)
        {
            var session = SessionScoreboard.Current; var player = session?.LocalPlayer;
            if (SessionScoreboard.IsNetworked || player?.Avatar == null || !CanEdit || roster == null ||
                index < 0 || index >= roster.Characters.Count || !roster.Characters[index].IsAvailable) return;
            if (player.CharacterIndex == index) return;
            foreach (var other in session.Players)
                if (other.Id != player.Id && other.CharacterIndex == index)
                { ProfileResult?.Invoke("Этот облик уже занят. Выбери другой."); return; }
            var old = player.Avatar;
            var instance = Instantiate(roster.Characters[index].Prefab, old.transform.position, old.transform.rotation);
            player.Avatar = instance.GetComponent<PlayerController>(); player.CharacterIndex = index;
            if (instance.TryGetComponent(out PlayerInputReader reader)) reader.enabled = false;
            Destroy(old.gameObject); LocalPartyProfile.Save(player);
            ProfileResult?.Invoke("Облик изменён");
        }
    }
}
