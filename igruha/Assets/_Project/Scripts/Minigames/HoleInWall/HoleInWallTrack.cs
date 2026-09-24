using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Дорожка: платформа над бассейном, своя стена, состав (пара или одиночка),
    /// счёт и места возврата.
    ///
    /// Дорожек арена строит четыре, играют не все: сколько нужно составу,
    /// решает <c>PairAssignment</c>, а лишние контроллер выключает целиком.
    /// </summary>
    public sealed class HoleInWallTrack : MonoBehaviour
    {
        /// <summary>Участник дорожки со всем, что о нём нужно знать правилам.</summary>
        public sealed class Member
        {
            public int PlayerId { get; }
            public PlayerController Avatar { get; }
            public PlayerPoseAbility Pose { get; }
            public StuckDetector Stuck { get; }
            public PlayerRespawner Respawner { get; }

            /// <summary>Ввод участника. Им же водит болванку соло-прогона.</summary>
            public PlayerInputReader Input { get; }

            /// <summary>
            /// Формы вырезов этого участника: вырез закреплён за игроком, и
            /// контур у него собственный. Считается один раз при сборке
            /// дорожки и до конца раунда не меняется.
            /// </summary>
            public CutoutShapes Shapes { get; }

            /// <summary>Воронка его выреза: край дырки доводит его в последние полсекунды.</summary>
            public WallFunnel Funnel { get; }

            /// <summary>Вода: поднимает его на поверхность и гасит, пока он в бассейне.</summary>
            public PlayerBuoyancy Buoyancy { get; }

            /// <summary>Точка респавна, с которой участник пришёл. Возвращается в конце раунда.</summary>
            public Transform OriginalRespawnPoint { get; }

            /// <summary>Летит в воду или барахтается: возврат уже назначен на <see cref="ReturnAt"/>.</summary>
            public bool Returning { get; set; }
            public bool RecoveryStarted { get; set; }

            /// <summary>Момент возврата на платформу в общих часах.</summary>
            public double ReturnAt { get; set; }

            /// <summary>Сетевой объект аватара. По нему видно, ведёт ли это тело сама эта машина.</summary>
            public NetworkObject Body { get; }

            /// <summary>Тело ведёт эта машина: ввод, физика и столкновения у него здесь, а не в копии.</summary>
            public bool DrivenHere => WorldAuthority.DrivenHere(Body);

            /// <summary>Радиус капсулы, м. Им меряется, когда видимая грань стены доходит до тела.</summary>
            public float Radius { get; }

            /// <summary>Разбор участника на текущей стене у сервера.</summary>
            public HoleInWallCheck Check { get; } = new HoleInWallCheck();

            public Member(int playerId, PlayerController avatar, PlayerPoseAbility pose,
                StuckDetector stuck, PlayerRespawner respawner, PlayerInputReader input,
                CutoutShapes shapes, WallFunnel funnel, PlayerBuoyancy buoyancy)
            {
                PlayerId = playerId;
                Avatar = avatar;
                Body = avatar != null ? avatar.GetComponent<NetworkObject>() : null;
                Radius = avatar != null && avatar.TryGetComponent(out CapsuleCollider capsule) ? capsule.radius : 0f;
                Pose = pose;
                Stuck = stuck;
                Respawner = respawner;
                Input = input;
                Shapes = shapes;
                Funnel = funnel;
                Buoyancy = buoyancy;
                OriginalRespawnPoint = respawner != null ? respawner.RespawnPoint : null;
            }
        }

        [Tooltip("Номер дорожки, с нуля. Им же берётся её X из конфига")]
        [SerializeField] private int index;
        [Tooltip("Стена этой дорожки")]
        [SerializeField] private SweepingWall wall;
        [Tooltip("Две точки на платформе: где стоит пара. У одиночки занята только первая, и она сдвигается в центр")]
        [SerializeField] private Transform[] slots = new Transform[2];
        [Tooltip("Надпись над дорожкой одиночки. Прячется, если на дорожке пара")]
        [SerializeField] private GameObject soloBanner;

        private readonly List<Member> members = new List<Member>(2);
        private readonly List<WallPattern> patterns = new List<WallPattern>(8);

        /// <summary>Номер дорожки, с нуля.</summary>
        public int Index => index;

        public SweepingWall Wall => wall;

        /// <summary>Кто на дорожке. Один — одиночка, двое — пара.</summary>
        public IReadOnlyList<Member> Members => members;

        /// <summary>Рисунок всех стен этой дорожки. Считается сервером один раз на раунд.</summary>
        public List<WallPattern> Patterns => patterns;

        /// <summary>Сколько стен дорожка прошла. Это же очко каждому её участнику.</summary>
        public int Score { get; private set; }

        /// <summary>
        /// Трос этой пары. У одиночки его нет. Живёт здесь, а не общим списком:
        /// снимать его приходится и поштучно — когда пара теряет одного.
        /// </summary>
        public PlayerTether Tether { get; set; }

        /// <summary>На дорожке один человек: вырез один, стена быстрее.</summary>
        public bool Solo => members.Count == 1;

        /// <summary>Дорожка в игре: на ней есть хотя бы один участник.</summary>
        public bool Active => members.Count > 0;

        /// <summary>Текущая стена уже пущена. У одиночки это происходит позже, чем у пар.</summary>
        public bool WallLaunched { get; set; }

        /// <summary>Вердикт по текущей стене уже посчитан. Считается ровно один раз.</summary>
        public bool WallResolved { get; set; }

        /// <summary>
        /// Стена текущего прохода дошла до линии проверки на ЭТОЙ машине, и её
        /// участники, которых эта машина ведёт, уже сверены со своими вырезами.
        /// Отметка своя у каждой машины: вердикт при этом решает только сервер.
        /// </summary>
        public bool LocalChecked { get; set; }

        /// <summary>
        /// Клиент уже сообщил серверу об ударе стеной по своему телу на этой
        /// стене. Касание приходит каждый шаг физики, отчёт нужен один.
        /// </summary>
        public bool StrikeReported { get; set; }

        /// <summary>
        /// Сервер дошёл до линии проверки и собирает разбор участников: свой
        /// вид — сразу, отчёты владельцев — по приезде. Вердикт выносится,
        /// когда разобраны все или вышел <see cref="CheckDeadline"/>.
        /// </summary>
        public bool CheckPending { get; private set; }

        /// <summary>Момент линии проверки у сервера в общих часах.</summary>
        public double CheckOpenedAt { get; private set; }

        /// <summary>До какого момента ждать отчёты владельцев.</summary>
        public double CheckDeadline { get; private set; }

        /// <summary>Открыть сбор разбора на линии проверки.</summary>
        public void OpenCheck(double now, float timeout)
        {
            CheckPending = true;
            CheckOpenedAt = now;
            CheckDeadline = now + timeout;
        }

        /// <summary>Все участники разобраны — вердикт можно выносить.</summary>
        public bool AllChecked
        {
            get
            {
                for (int i = 0; i < members.Count; i++)
                {
                    if (!members[i].Check.Decided)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Момент последнего засчитанного прохода в общих часах. Бесконечность в прошлом — ещё не проходили.</summary>
        public double PassedAt { get; private set; } = double.NegativeInfinity;

        /// <summary>Номер последней засчитанной стены, с единицы.</summary>
        public int PassedWall { get; private set; }

        /// <summary>Новая стена: сбросить всё, что относилось к предыдущей.</summary>
        public void BeginWall()
        {
            WallLaunched = false;
            WallResolved = false;
            LocalChecked = false;
            StrikeReported = false;
            ResetChecks();
        }

        private void ResetChecks()
        {
            CheckPending = false;
            for (int i = 0; i < members.Count; i++)
            {
                members[i].Check.Reset();
                members[i].Funnel?.ResetStats();
            }
        }

        /// <summary>Освободить дорожку от прошлого раунда.</summary>
        public void ResetTrack()
        {
            ResetChecks();
            members.Clear();
            patterns.Clear();
            Score = 0;
            Tether = null;
            WallLaunched = false;
            WallResolved = false;
            LocalChecked = false;
            StrikeReported = false;
            PassedAt = double.NegativeInfinity;
            PassedWall = 0;

            if (soloBanner != null)
            {
                soloBanner.SetActive(false);
            }

            if (wall != null)
            {
                wall.Retire();
            }
        }

        public void AddMember(Member member)
        {
            if (member != null)
            {
                members.Add(member);
            }
        }

        /// <summary>
        /// Снять участника: он вышел из матча. Дорожка остаётся в раунде —
        /// её номер держит раскладку у клиента, — но играет тем составом,
        /// что остался.
        /// </summary>
        /// <returns>Истина — участник был на дорожке и снят.</returns>
        public bool RemoveMember(int playerId)
        {
            int slot = IndexOfMember(playerId);
            if (slot < 0)
            {
                return false;
            }

            members.RemoveAt(slot);

            if (soloBanner != null)
            {
                soloBanner.SetActive(Solo);
            }

            return true;
        }

        /// <summary>Стена пройдена: очко каждому участнику дорожки.</summary>
        public void AwardWall(int wallNumber, double time)
        {
            Score++;
            PassedWall = wallNumber;
            PassedAt = time;
        }

        /// <summary>Счёт, объявленный сервером. Клиент его только применяет — считает всегда сервер.</summary>
        public void ApplyScore(int value)
        {
            Score = Mathf.Max(0, value);
        }

        /// <summary>
        /// Расставить места на платформе под состав: пару разводит на
        /// <c>PairSpread</c>, одиночку ставит по центру.
        ///
        /// Эти же точки служат респавном: возврат из воды идёт ровно туда,
        /// откуда сметнуло.
        /// </summary>
        public void ArrangeSlots(HoleInWallConfig config)
        {
            if (slots == null)
            {
                return;
            }

            float half = config.PairSpread * 0.5f;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    continue;
                }

                float x = Solo ? 0f : (i == 0 ? -half : half);
                slots[i].localPosition = new Vector3(x, 0f, 0f);

                // Лицом к стене: с этой же стороны приходит стена, туда же
                // фиксируется фронт персонажа.
                slots[i].localRotation = Quaternion.LookRotation(-SweepingWall.TravelDirection, Vector3.up);
            }

            if (soloBanner != null)
            {
                soloBanner.SetActive(Solo);
            }
        }

        /// <summary>
        /// Указать каждой воронке её вырез. Зовётся, когда состав дорожки
        /// собран: номер выреза совпадает с местом на платформе, и до конца
        /// раунда не меняется — даже зеркальный переворот двигает вырез,
        /// а не его принадлежность.
        /// </summary>
        public void AimFunnels(HoleInWallConfig config)
        {
            float trackX = transform.position.x;
            for (int i = 0; i < members.Count; i++)
            {
                members[i].Funnel?.Configure(config, wall, i, trackX);
            }
        }

        /// <summary>Снять воронки: раунд кончился, доводить больше некуда.</summary>
        public void ReleaseFunnels()
        {
            for (int i = 0; i < members.Count; i++)
            {
                members[i].Funnel?.Release();
            }
        }

        /// <summary>
        /// Формы вырезов участника по номеру места. Пусто — на этом месте
        /// никого нет: у одиночки занято только нулевое.
        /// </summary>
        public CutoutShapes ShapesOf(int memberIndex) =>
            memberIndex >= 0 && memberIndex < members.Count ? members[memberIndex].Shapes : null;

        /// <summary>Точка участника на платформе. Пусто — такого места на дорожке нет.</summary>
        public Transform SlotOf(int memberIndex) =>
            slots != null && memberIndex >= 0 && memberIndex < slots.Length ? slots[memberIndex] : null;

        /// <summary>Место участника по его номеру. Минус один — участника на дорожке нет.</summary>
        public int IndexOfMember(int playerId)
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].PlayerId == playerId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
