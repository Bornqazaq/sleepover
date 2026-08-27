using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Spawning;
using Igruha.Core.UI;

namespace Igruha.Core.Hub
{
    /// <summary>
    /// Запуск хаба: экран выбора персонажа, затем ожидание состава и камера на
    /// своего игрока.
    ///
    /// Выбор идёт в обоих режимах. В сети он раньше пропускался вовсе —
    /// персонажа выдавал сервер прямо в одобрении подключения, потому что NGO
    /// берёт префаб именно там. Теперь одобрение тела не создаёт, и порядок
    /// такой: показали экран → отправили намерение → сервер закрепил персонажа
    /// и создал тело → ждём, пока тела появятся у всех.
    ///
    /// В одиночку — тот же экран, дальше спавн 2–8 персонажей через
    /// PlayerSpawner (см. его network-guard: если сеть уже поднята, локальный
    /// спавн сам себя пропускает).
    /// </summary>
    public sealed class HubBootstrap : MonoBehaviour
    {
        [SerializeField] private PlayerSpawner playerSpawner;
        [SerializeField] private HubController hubController;
        [SerializeField] private MinigameCameraController cameraController;
        [SerializeField] private CharacterSelectScreen characterSelect;
        [SerializeField] private EmoteWheel emoteWheel;
        [SerializeField] private CharacterRoster roster;

        [Tooltip("Сколько секунд ждать, пока сервер соберёт состав и у всех появятся аватары. " +
                 "Считаем сроком человеческого сбора, а не техническим: в билде люди запускают " +
                 "игру вразнобой, и 15 с хватало только на редакторский стенд")]
        [SerializeField] private float networkRosterTimeout = 180f;

        private void Start()
        {
            StartCoroutine(Boot());
        }

        private IEnumerator Boot()
        {
            bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            IReadOnlyList<SessionPlayer> players;

            if (characterSelect == null || roster == null)
            {
                Debug.LogError($"{name}: HubBootstrap не настроен (characterSelect/roster)", this);
                yield break;
            }

            if (networked)
            {
                yield return ChooseCharacterNetworked();

                yield return WaitForNetworkRoster();
                ISessionScoreboard scoreboard = SessionScoreboard.Current;
                if (scoreboard == null || scoreboard.Players.Count == 0)
                {
                    Debug.LogError($"{name}: сетевая сессия не отдала табло — хаб не стартует", this);
                    yield break;
                }

                players = scoreboard.Players;
            }
            else
            {
                if (playerSpawner == null)
                {
                    Debug.LogError($"{name}: HubBootstrap не настроен (playerSpawner)", this);
                    yield break;
                }

                int chosenIndex = -1;
                bool choiceMade = false;
                characterSelect.Show(roster, null, picked =>
                {
                    chosenIndex = picked;
                    choiceMade = true;
                });
                yield return new WaitUntil(() => choiceMade);

                if (chosenIndex < 0 || chosenIndex >= roster.Characters.Count)
                {
                    Debug.LogError($"{name}: персонаж не выбран — спавн отменён", this);
                    yield break;
                }

                players = playerSpawner.SpawnPlayers(roster.Characters[chosenIndex]);
            }

            if (players.Count == 0)
            {
                yield break;
            }

            yield return BindLocalPlayer(players, networked);
        }

        /// <summary>
        /// Показать экран выбора и дождаться, пока сервер закрепит персонажа.
        ///
        /// Ждём сначала сам сервис выбора: он приезжает вместе с табло катки,
        /// а сцена хаба может успеть загрузиться раньше. Без ожидания экран
        /// открылся бы без связи с сервером — занятые не гасились бы, а клик
        /// уходил в пустоту.
        ///
        /// Закрытие экрана решает сервер, а не клик: подтверждением служит
        /// <c>HasChosen</c>. Так закрывается и случай, когда персонажа выдали
        /// по истечении срока, — экран гаснет сам.
        /// </summary>
        private IEnumerator ChooseCharacterNetworked()
        {
            float deadline = Time.realtimeSinceStartup + networkRosterTimeout;

            while (CharacterSelection.Current == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            ICharacterSelection selection = CharacterSelection.Current;
            if (selection == null)
            {
                Debug.LogError($"{name}: сетевая сессия не отдала выбор персонажа — играем тем, что даст сервер", this);
                yield break;
            }

            if (selection.HasChosen)
            {
                yield break;
            }

            characterSelect.Show(roster, selection, _ => { });

            while (characterSelect.IsOpen && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private IEnumerator WaitForNetworkRoster()
        {
            float deadline = Time.realtimeSinceStartup + networkRosterTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                ISessionScoreboard scoreboard = SessionScoreboard.Current;
                if (scoreboard != null && scoreboard.Players.Count > 0 && AllHaveAvatars(scoreboard.Players))
                {
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning($"{name}: ростер не собрался за {networkRosterTimeout:F0} с — стартуем с тем, что есть", this);
        }

        private static bool AllHaveAvatars(IReadOnlyList<SessionPlayer> players)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Avatar == null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Навести камеру, колесо эмоций и подсказку у якоря на персонажа этой машины.
        ///
        /// В сети берём **только своего** аватара и ждём его столько же, сколько ждали
        /// состав. Раньше на месте ожидания стоял откат к <c>players[0]</c> — и когда
        /// свой персонаж не успевал приехать, клиент молча получал камеру, эмоции и
        /// взаимодействие, привязанные к аватару хоста. Со стороны это выглядит как
        /// «камера смотрит куда-то в комнату, Tab и E не работают», и найти причину
        /// по такому симптому почти невозможно. Лучше громко не привязаться вовсе.
        /// </summary>
        private IEnumerator BindLocalPlayer(IReadOnlyList<SessionPlayer> players, bool networked)
        {
            PlayerController avatar;

            if (networked)
            {
                float deadline = Time.realtimeSinceStartup + networkRosterTimeout;
                while ((avatar = SessionScoreboard.Current?.LocalPlayer?.Avatar) == null
                       && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                if (avatar == null)
                {
                    Debug.LogError($"{name}: за {networkRosterTimeout:F0} с не приехал СВОЙ персонаж — " +
                                   "камера, колесо эмоций и взаимодействие остались непривязанными. " +
                                   "Чужого аватара не подставляем: это выглядело бы как сломанная игра", this);
                    yield break;
                }
            }
            else
            {
                // Без сети «свой» — первый заспавненный, остальные болванки (см. SessionManager).
                avatar = SessionScoreboard.Current?.LocalPlayer?.Avatar ?? players[0].Avatar;
                if (avatar == null)
                {
                    Debug.LogError($"{name}: у аватара нет PlayerController — камере не за кого цепляться", this);
                    yield break;
                }
            }

            RestoreLocalControl(avatar);

            if (cameraController != null)
            {
                // CameraTarget — точка на уровне груди, а не корень капсулы: персонажи
                // разного роста иначе кадрируются по-разному (см. PlayerController.CameraTarget).
                cameraController.Apply(CameraMode.ThirdPerson, avatar.CameraTarget);
            }
            else
            {
                Debug.LogError($"{name}: не назначен cameraController — камера останется на месте вместо выбранного персонажа", this);
            }

            if (hubController != null && avatar.TryGetComponent(out PlayerInteractor interactor))
            {
                hubController.BindLocalPlayer(interactor);
            }

            // Колесо берём из сцены, если ссылка пустая: Tab обязан работать
            // везде, а не только там, где поле заполнили руками.
            EmoteWheel wheel = emoteWheel != null ? emoteWheel : EmoteWheel.Current;

            if (wheel == null)
            {
                Debug.LogWarning(
                    $"{name}: в сцене нет колеса эмоций — Tab ничего не откроет. " +
                    "Собери его пунктом меню Igruha/UI/Build Emote Wheel", this);
            }
            else if (avatar.TryGetComponent(out PlayerEmoteAbility emotes))
            {
                wheel.BindLocalPlayer(emotes);
            }
        }

        /// <summary>
        /// Вернуть игроку управление. Персонаж переезжает между сценами вместе со
        /// своим состоянием, а экран результатов мини-игры ввод глушит
        /// (<c>MinigameControllerBase.EnterResults</c>) — без этого из мини-игры
        /// возвращаешься в хаб обездвиженным (IGR-324).
        ///
        /// Хаб — единственное место, где управление обязано быть включено всегда,
        /// откуда бы в него ни пришли, поэтому чиним здесь, а не в мини-игре.
        /// </summary>
        private static void RestoreLocalControl(PlayerController avatar)
        {
            if (!avatar.TryGetComponent(out PlayerInputReader reader))
            {
                return;
            }

            // Болванок и чужие копии будить нельзя — им управление снято навсегда.
            if (!reader.LocallyControlled)
            {
                return;
            }

            // Страховка от любой роли, забытой мини-игрой: блокировку ставят
            // и заморозка, и постамент Водящего, а снимает её тот, кто ставил.
            // Стоит одному такому снятию не сработать — человек приезжает в хаб
            // обездвиженным навсегда. В хабе блокировке взяться неоткуда.
            bool wasLocked = avatar.MovementLocked;
            avatar.MovementLocked = false;

            if (!reader.enabled)
            {
                reader.enabled = true;
            }
            else if (!wasLocked)
            {
                return;
            }

            Debug.Log($"🎮 [{avatar.name}] управление возвращено после мини-игры" +
                      (wasLocked ? " (снята забытая блокировка)" : string.Empty));
        }
    }
}
