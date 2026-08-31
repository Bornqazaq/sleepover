using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Погонщик болванок для одиночного прогона: ведёт всех, кем эта машина
    /// не управляет, к их вырезам и ставит им нужную позу.
    ///
    /// Без него парную механику в одиночку не проверить вовсе. Провал здесь
    /// парный: неподвижный манекен-напарник проваливал бы каждую стену,
    /// и живой игрок не увидел бы ни одного успешного прохода — а значит,
    /// ни очков, ни мест, ни каскада провалов.
    /// </summary>
    /// <remarks>
    /// <b>🔴 В сетевой катке погонщик выключен и пишет об этом в лог.</b>
    /// Признак болванки здесь — «этой машиной не управляется», и у СЕРВЕРА так
    /// выглядит любой клиент: без проверки погонщик вставал бы в позу за живого
    /// человека, то есть делал бы за него единственное действие, которое
    /// в игре вообще есть. На этих граблях стояли трижды — в «Экзамене»,
    /// в «Верю / не верю» и в «Рейсе на память».
    ///
    /// Единственное исключение — процесс, запущенный с <c>--bot</c>:
    /// за такой клавиатурой человека нет вовсе, и там болванка ведёт
    /// <b>только персонажа своей машины</b>.
    ///
    /// Путь только человеческий: <c>DriveMove</c> и <c>DrivePose</c> — те же
    /// вызовы, которыми играет живой игрок. Короткого пути в обход правил нет,
    /// иначе проверка перестанет проверять то, что делает человек.
    /// </remarks>
    public sealed class HoleInWallDebugBot : MonoBehaviour
    {
        /// <summary>Ближе этого расстояния до своей точки болванка стоит, м.</summary>
        private const float ArriveRadius = 0.12f;

        [SerializeField] private HoleInWallMinigame game;
        [Tooltip("С какой вероятностью болванка ошибается на стене: 0 — проходит всё, 1 — не проходит ничего. Нужна, чтобы в соло-прогоне были и проходы, и провалы, и каскады")]
        [Range(0f, 1f)]
        [SerializeField] private float mistakeChance = 0.25f;
        [Tooltip("Отдать болванке и персонажа этой машины — тогда соло-прогон идёт целиком сам, без рук. Только для одиночного прогона")]
        [SerializeField] private bool autopilotLocalPlayer;

        /// <summary>Решение на текущую стену: как именно этот участник её провалит.</summary>
        private struct Mistake
        {
            public bool WrongPose;
            public bool WrongPlace;
        }

        private readonly Dictionary<int, Mistake> mistakes = new Dictionary<int, Mistake>(8);
        private int decidedWall = -1;
        private bool networkNoticed;
        private bool autopilotNoticed;

        private void Update()
        {
            if (game == null)
            {
                enabled = false;
                return;
            }

            if (WorldAuthority.IsNetworkSession && !HandleNetworkSession())
            {
                return;
            }

            if (!WorldAuthority.IsNetworkSession && autopilotLocalPlayer)
            {
                AnnounceAutopilot();
                EngageLocalAutopilot();
            }
        }

        /// <summary>Ложь — погонщик в этой сессии не работает и себя выключил.</summary>
        private bool HandleNetworkSession()
        {
            if (!LaunchArguments.BotEnabled)
            {
                if (!networkNoticed)
                {
                    networkNoticed = true;
                    Debug.Log($"{name}: погонщик болванок выключен — идёт сетевая катка, " +
                              "здесь за каждого играет живой человек", this);
                }

                enabled = false;
                return false;
            }

            if (!networkNoticed)
            {
                networkNoticed = true;
                Debug.Log($"{name}: 🤖 сетевая катка с --bot: погонщик ведёт ТОЛЬКО персонажа " +
                          "этой машины, чужими не рулит", this);
            }

            EngageLocalAutopilot();
            return true;
        }

        private void AnnounceAutopilot()
        {
            if (autopilotNoticed)
            {
                return;
            }

            // 🔴 Галка говорит вслух. Оставленная включённой после автопрогона,
            // она отбирает персонажа у человека за клавиатурой: тот жмёт WASD,
            // ничего не происходит, а персонаж уходит сам. Ровно это приняли
            // за сломанную игру на прогоне «Рейса на память» 31.08.
            autopilotNoticed = true;
            Debug.LogWarning($"{name}: 🤖 автопилот ведёт ТВОЕГО персонажа — снять галку " +
                             "autopilotLocalPlayer на HoleInWallDebugBot, иначе руками не поиграть", this);
        }

        private void EngageLocalAutopilot()
        {
            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            if (local?.Avatar == null)
            {
                return;
            }

            if (local.Avatar.TryGetComponent(out PlayerInputReader reader))
            {
                reader.EngageAutopilot();
            }
        }

        private void FixedUpdate()
        {
            if (game == null || game.CurrentWallNumber <= 0)
            {
                return;
            }

            if (decidedWall != game.CurrentWallNumber)
            {
                decidedWall = game.CurrentWallNumber;
                DecideMistakes();
            }

            IReadOnlyList<HoleInWallTrack> tracks = game.PlayingTracks;
            for (int t = 0; t < tracks.Count; t++)
            {
                DriveTrack(tracks[t]);
            }
        }

        /// <summary>
        /// Бросок на стену: кто и как её провалит. Решение принимается один раз
        /// на стену — иначе болванка металась бы между правильной и неправильной
        /// точкой каждый такт.
        /// </summary>
        private void DecideMistakes()
        {
            IReadOnlyList<HoleInWallTrack> tracks = game.PlayingTracks;
            for (int t = 0; t < tracks.Count; t++)
            {
                IReadOnlyList<HoleInWallTrack.Member> members = tracks[t].Members;
                for (int m = 0; m < members.Count; m++)
                {
                    // Один бросок на «провалит ли», второй — на «чем именно».
                    // Двумя независимыми бросками болванка иногда ошибалась
                    // и позой, и местом сразу, и по логу нельзя было понять,
                    // какая из двух проверок сработала.
                    bool failing = Random.value < mistakeChance;
                    bool byPose = Random.value < 0.5f;
                    mistakes[members[m].PlayerId] = new Mistake
                    {
                        WrongPose = failing && byPose,
                        WrongPlace = failing && !byPose
                    };
                }
            }
        }

        private void DriveTrack(HoleInWallTrack track)
        {
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            for (int i = 0; i < members.Count; i++)
            {
                HoleInWallTrack.Member member = members[i];
                if (member.Input == null || member.Avatar == null || !IsDriveable(member.Input))
                {
                    continue;
                }

                DriveMember(track, member, i);
            }
        }

        private void DriveMember(HoleInWallTrack track, HoleInWallTrack.Member member, int cutoutIndex)
        {
            if (!track.Wall.Running ||
                !track.Wall.TryGetCutout(cutoutIndex, out HoleInWallPose pose, out float offset))
            {
                member.Input.DriveMove(Vector2.zero);
                return;
            }

            mistakes.TryGetValue(member.PlayerId, out Mistake mistake);

            member.Input.DrivePose((int)(mistake.WrongPose ? OtherPose(pose) : pose));

            // Мимо на два допуска: провал уверенный, но болванка всё ещё стоит
            // на платформе, а не уходит с неё сама.
            float error = mistake.WrongPlace ? MistakeOffset(member) * game.Config.HitTolerance * 2f : 0f;
            float targetX = track.transform.position.x + offset + error;

            Vector3 position = member.Avatar.transform.position;
            var toTarget = new Vector3(targetX - position.x, 0f, track.transform.position.z - position.z);

            if (toTarget.sqrMagnitude <= ArriveRadius * ArriveRadius)
            {
                member.Input.DriveMove(Vector2.zero);
                return;
            }

            member.Input.DriveMove(member.Avatar.WorldToMoveInput(toTarget));
        }

        /// <summary>В какую сторону промахнуться. Зависит от участника, чтобы двое не сошлись в одной точке.</summary>
        private static float MistakeOffset(HoleInWallTrack.Member member) =>
            member.PlayerId % 2 == 0 ? 1f : -1f;

        private static HoleInWallPose OtherPose(HoleInWallPose pose)
        {
            int next = (int)pose % HoleInWallConfig.PoseCount + 1;
            return (HoleInWallPose)next;
        }

        /// <summary>
        /// Кем болванке позволено рулить.
        ///
        /// <b>Не по флагу</b> <c>enabled</c> у ридера: в автопрогоне на восьми
        /// процессах персонаж у каждой машины свой, то есть локально
        /// управляемый и с живым ридером, — там болванку взводит <c>--bot</c>.
        /// </summary>
        private static bool IsDriveable(PlayerInputReader reader) =>
            reader.Autopilot || !reader.LocallyControlled;
    }
}
