using System.Collections.Generic;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.UI;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Igruha.Minigames.Mosquitoes
{
    public sealed class MosquitoesMinigame : MinigameControllerBase, IPushButtonOverride
    {
        [SerializeField] private MosquitoesConfig config;
        [SerializeField] private MosquitoBody mosquitoPrefab;
        [SerializeField] private GameObject dragonflyVisual;
        private readonly Dictionary<int, Vector3> entryPrevious = new Dictionary<int, Vector3>();
        public float EntryRemaining { get; private set; }
        public int DragonflyKills { get; private set; }
        private float dragonflyCameraUntil, dragonflyFinishAt;
        private bool eatenLocally;
        [SerializeField] private Transform bed, bedExit, lampCenter;
        [SerializeField] private MinigameCameraController cameraController;
        [SerializeField] private CinemachineOrbitalFollow orbit;
        [SerializeField] private MosquitoesPresentation presentation;
        [SerializeField] private MosquitoesAudio audioPlayer;
        [SerializeField] private InputActionAsset inputTemplate;
        [SerializeField] private MinigameDefinition giantTutorial, mosquitoTutorial;
        [SerializeField] private int debugGiantId = -1;
        [SerializeField] private bool debugDisableBots;
        private readonly PlayerAvatarPark parked = new PlayerAvatarPark();
        private readonly List<SessionPlayer> roster = new List<SessionPlayer>(8);
        private readonly List<MosquitoBody> bodies = new List<MosquitoBody>(7);
        private readonly HashSet<int> participated = new HashSet<int>();
        private readonly HashSet<int> departed = new HashSet<int>();
        private readonly SpecialRoleHistory roles = new SpecialRoleHistory();
        private MosquitoesNetwork network;
        private PlayerController giant;
        private PlayerPushAbility punch;
        private IPushButtonOverride previousPunchOverride;
        private Animator giantAnimator;
        private MosquitoGiantRig giantRig;
        public MosquitoGiantRig GiantRig => giantRig;
        public int ConfirmedBiteEffects { get; private set; }
        private bool previousLock, previousRootMotion, cleaned = true, winnerGiant, spectating;
        private float previousAnimatorSpeed, radius, swatCooldown, swatImpact = -1, hudIn, botIn;
        private GiantPhase shownPhase = (GiantPhase)255;
        private InputAction sharedLook;
        private bool lookWasEnabled;
        private Transform cameraRestore;
        private CameraMode cameraModeRestore;
        private int localId = -1, giantId = -1;
        private string hint;
        public MosquitoesConfig Config => config;
        public MosquitoesAudio Audio => audioPlayer;
        public GiantSleepState Sleep { get; private set; }
        public int GiantId => giantId;
        public int LocalId => localId;
        public bool RosterReady => roster.Count > 0;
        public bool ControlsAvailable => !cleaned && RoundActive && Countdown <= 0 && giant != null;
        public bool Automated => LaunchArguments.BotEnabled;
        public float Countdown { get; private set; }
        public IReadOnlyList<MosquitoBody> Bodies => bodies;
        public Vector3 BiteTarget => giantRig != null ? giantRig.BitePoint : giant != null ? giant.Position + Vector3.up : bed.position;
        public int AliveCount { get { int n = 0; foreach (var b in bodies) if (b != null && b.IsAlive && !departed.Contains(b.Id)) n++; return n; } }

        protected override void Awake()
        {
            base.Awake(); network = GetComponent<MosquitoesNetwork>();
            if (inputTemplate != null) sharedLook = inputTemplate.FindAction("Player/Look", true);
        }
        protected override void OnPlayersReady()
        {
            roster.Clear(); roster.AddRange(Players);
            localId = SessionScoreboard.Current?.LocalPlayer?.Id ?? (roster.Count > 0 ? roster[0].Id : -1);
        }
        protected override void OnRoundStarted()
        {
            if (config == null || mosquitoPrefab == null || bed == null) { Debug.LogError("Mosquitoes: incomplete scene configuration.", this); return; }
            Sleep = new GiantSleepState(config); Countdown = config.CountdownSeconds;
            EntryRemaining = MosquitoWindowEntry.Deadline; DragonflyKills = 0; entryPrevious.Clear();
            dragonflyCameraUntil = dragonflyFinishAt = 0; eatenLocally = false;
            cleaned = false; winnerGiant = false; spectating = false; departed.Clear(); participated.Clear();
            presentation?.Begin();
            if (cameraController != null) { cameraRestore = cameraController.CurrentTarget; cameraModeRestore = cameraController.CurrentMode; }
            if (orbit != null) radius = orbit.Radius;
            if (!HasAuthority) return;
            Timer?.StopTimer();
            giantId = SessionScoreboard.Current != null ? SessionScoreboard.Current.PickSpecialRole("Giant") : roles.Pick("Giant", roster);
            if (Debug.isDebugBuild && LaunchArguments.TryGetValue("--mosquito-giant", out string role) && int.TryParse(role, out int forcedRole)) debugGiantId = forcedRole;
            if (Debug.isDebugBuild && LaunchArguments.TryGetValue("--mosquito-check", out string probe) && probe == "round-timeout") debugDisableBots = true;
            if (Debug.isDebugBuild && debugGiantId >= 0 && FindPlayer(debugGiantId) != null) giantId = debugGiantId;
#if UNITY_EDITOR
            if (network == null || !network.IsSpawned)
            {
                int startRole = UnityEditor.SessionState.GetInt("Mosquitoes.StartRole", -1);
                UnityEditor.SessionState.EraseInt("Mosquitoes.StartRole");
                if (startRole == 0) giantId = localId;
                else if (startRole == 1)
                    foreach (SessionPlayer player in roster) if (player.Id != localId) { giantId = player.Id; break; }
            }
#endif
            if (FindPlayer(giantId) == null) giantId = roster[0].Id;
            BindRole();
            foreach (SessionPlayer player in roster)
            {
                if (player.Id == giantId) continue;
                MosquitoBody body = Instantiate(mosquitoPrefab, FlightSpawn(player.Id), Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                entryPrevious[player.Id] = body.Position;
                bool net = network != null && network.IsSpawned;
                body.Initialize(this, player.Id, player.Id == localId, !net && player.Id != localId || Automated, CharacterSelection.Current is Igruha.Networking.CharacterSelectionManager selection ? selection.CharacterOf(player.Id) : player.CharacterIndex);
                if (net) body.NetworkObject.SpawnWithOwnership((ulong)player.Id, true);
            }
            network?.Publish();
        }
        private SessionPlayer FindPlayer(int id)
        {
            for (int i = 0; i < roster.Count; i++) if (roster[i].Id == id) return roster[i];
            return null;
        }
        private void BindRole()
        {
            SessionPlayer player = FindPlayer(giantId);
            if (player?.Avatar == null) return;
            giant = player.Avatar; previousLock = giant.MovementLocked;
            punch = giant.GetComponent<PlayerPushAbility>();
            if (punch != null) { previousPunchOverride = punch.ButtonOverride; punch.ButtonOverride = this; punch.PunchStarted += PunchStarted; }
            giantAnimator = giant.GetComponentInChildren<Animator>();
            if (giantAnimator != null) { previousAnimatorSpeed = giantAnimator.speed; previousRootMotion = giantAnimator.applyRootMotion; }
            if (sharedLook != null) lookWasEnabled = sharedLook.enabled;
            foreach (SessionPlayer p in roster) if (p.Id != giantId) parked.Park(p.Avatar);
            sharedLook?.Enable();
            giantRig = giant.gameObject.AddComponent<MosquitoGiantRig>(); giantRig.Initialize(giantAnimator);
            shownPhase = (GiantPhase)255;
            hint = localId == giantId ? "ВЕЛИКАН • E у кровати — встать / уснуть • ЛКМ — шлепок\nНакопи 60 секунд сна или поймай всех комаров" :
                $"КОМАР • WASD — полёт • Space / Ctrl — высота • Shift — тихий полёт\nЗа {MosquitoWindowEntry.Deadline:0} секунд влети в окно! Обратно нельзя. Затем держи ЛКМ у груди 0,5 с";
            presentation?.SetRole(localId == giantId, hint);
            ApplyPose();
            StartCoroutine(ApplyRoleTutorial());
        }
        private System.Collections.IEnumerator ApplyRoleTutorial()
        {
            yield return null;
            if (!IsPractice || cleaned) yield break;
            var view = FindFirstObjectByType<TutorialView>();
            var definition = localId == giantId ? giantTutorial : mosquitoTutorial;
            if (view != null && definition != null) view.Show(definition);
        }
        public void RegisterBody(MosquitoBody body) { if (!bodies.Contains(body)) bodies.Add(body); }
        public void UnregisterBody(MosquitoBody body) { if (!cleaned) bodies.Remove(body); }
        public void Receive(MosquitoesSnapshot snapshot)
        {
            if (HasAuthority || Sleep == null) return;
            giantId = snapshot.GiantId;
            Sleep.ApplySnapshot((GiantPhase)snapshot.Phase, snapshot.Sleep, snapshot.Transition, snapshot.Immunity, snapshot.Lamp);
            Countdown = snapshot.Countdown; EntryRemaining = snapshot.EntryRemaining;
            if (!cleaned && giant == null) BindRole();
        }
        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!cleaned && network != null && !network.IsSpawned && Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame)
            {
                int next = 0; for (int i = 0; i < roster.Count; i++) if (roster[i].Id == giantId) next = (i + 1) % roster.Count;
                debugGiantId = roster[next].Id; Cleanup(); OnRoundStarted();
            }
#endif
            if (cleaned || !RoundActive || Sleep == null) return;
            if (HasAuthority)
            {
                if (Countdown > 0)
                {
                    Countdown = Mathf.Max(0, Countdown - Time.deltaTime);
                    if (Countdown == 0) Timer?.StartTimer(RoundDuration);
                }
                else TickRules(Time.deltaTime);
            }
            if (giant == null) return;
            ApplyPose();
            if (!debugDisableBots && HasAuthority && ControlsAvailable && (giantId != localId && (network == null || !network.IsSpawned) || Automated)) TickGiantBot(Time.deltaTime);
            BindCamera();
            presentation?.Paint(Sleep, Countdown, AliveCount, IsPractice);
            hudIn -= Time.unscaledDeltaTime;
            if (hudIn <= 0)
            {
                hudIn = .1f;
                if (Countdown > 0) Hud?.ShowCountdown(Countdown); else Hud?.HideCountdown();
                MosquitoBody own = FindBody(localId);
                string status = localId == giantId ? (Sleep.LampOn ? "ЛАМПА ВКЛ" : "ЛАМПА ВЫКЛ") :
                    own != null && !own.IsAlive ? (eatenLocally ? $"СТРЕКОЗА СЪЕЛА ТЕБЯ • не успел в окно за {MosquitoWindowEntry.Deadline:0} секунд" : "НАБЛЮДЕНИЕ • вы выбыли до конца раунда") :
                    own != null && !own.HasEntered ? $"В ОКНО! {EntryRemaining:0.0} с • снаружи охотится стрекоза" :
                    own != null && own.BiteCooldown > 0 ? $"УКУС ЧЕРЕЗ {own.BiteCooldown:0.0} с" : "УКУС ГОТОВ";
                presentation?.SetStatus($"СОН {Sleep.Sleep:0.0} / {config.SleepTarget:0} с     КОМАРЫ {AliveCount}\n{status}");
            }
        }
        private void TickRules(float dt)
        {
            EntryRemaining = Mathf.Max(0, EntryRemaining - dt);
            Sleep.Tick(dt); swatCooldown = Mathf.Max(0, swatCooldown - dt);
            if (swatImpact >= 0) { swatImpact -= dt; if (swatImpact <= 0) { swatImpact = -1; ResolveSwat(); } }
            foreach (MosquitoBody body in bodies)
            {
                if (body == null || !body.IsAlive || departed.Contains(body.Id)) continue;
                if (!body.HasEntered)
                {
                    Vector3 previous = entryPrevious.TryGetValue(body.Id, out var last) ? last : FlightSpawn(body.Id);
                    if (EntryRemaining > 0 && MosquitoWindowEntry.Crossed(previous, body.Position))
                    {
                        body.ConfirmEntry();
                        if (Automated) Debug.Log($"[MosquitoEntry] id={body.Id} remaining={EntryRemaining:F2}");
                    }
                    else if (EntryRemaining <= 0)
                    {
                        Vector3 caught = body.Position; body.Kill(); DragonflyKills++; dragonflyFinishAt = Time.unscaledTime + 1.1f;
                        network?.Effect(3, caught, body.Id);
                        if (Automated) Debug.Log($"[MosquitoDragonfly] id={body.Id} deadline={MosquitoWindowEntry.Deadline:0} alive={body.IsAlive}");
                        continue;
                    }
                    entryPrevious[body.Id] = body.Position;
                }
                else if (body.Position.x > MosquitoWindowEntry.Plane + .015f)
                    body.ResetPosition(MosquitoWindowEntry.ClampInside(body.Position));
                float cooldown = Mathf.Max(0, body.BiteCooldown - dt);
                if (Sleep.LampOn && InsideLight(body.Position)) body.Participated = true;
                bool valid = body.HasEntered && body.Holding && cooldown <= 0 && Sleep.CanBite &&
                    Vector3.Distance(body.Position, BiteTarget) <= config.BiteDistance && ClearPath(body.Position, BiteTarget);
                if (valid)
                {
                    body.BiteProgress += dt;
                    if (body.BiteProgress >= config.BiteHoldSeconds && Sleep.RegisterBite())
                    {
                        cooldown = config.BiteCooldown; body.BiteProgress = 0; valid = false; body.Participated = true;
                        network?.Effect(0, giantRig != null ? giantRig.BiteSurfacePoint : BiteTarget, body.Id);
                    }
                }
                else body.BiteProgress = 0;
                body.SetBiteState(valid, cooldown);
                if (body.Participated) participated.Add(body.Id);
            }
            if (Sleep.ReachedTarget || AliveCount == 0 && Time.unscaledTime >= dragonflyFinishAt) { winnerGiant = true; EndMinigame(); }
        }
        public bool TryBed(int sender)
        {
            if (!HasAuthority || !ControlsAvailable || sender != giantId || giant == null) return false;
            if (Sleep.Phase == GiantPhase.Awake && Vector3.Distance(giant.Position, bedExit.position) > 1.35f) return false;
            bool changed = Sleep.ToggleBed();
            if (changed) { network?.Effect(1, lampCenter.position); network?.Publish(); }
            return changed;
        }
        public bool IsGiant(PlayerController player) => player != null && player == giant;
        public bool TryLamp(int sender)
        {
            if (!HasAuthority || !ControlsAvailable || sender != giantId || giant == null ||
                Vector3.Distance(giant.Position, lampCenter.position) > 1.8f) return false;
            bool changed = Sleep.ToggleLamp();
            if (changed) { network?.Effect(1, lampCenter.position); network?.Publish(); }
            return changed;
        }
        public bool HandlePushButton(PlayerController player) => !ControlsAvailable || Sleep == null || !Sleep.CanSwat;
        private void PunchStarted() { if (localId == giantId) network?.RequestSwat(); }
        public bool TrySwat(int sender)
        {
            if (!HasAuthority || !ControlsAvailable || sender != giantId || !Sleep.CanSwat || swatCooldown > 0) return false;
            swatCooldown = giant.Config.PushCooldown; swatImpact = giant.Config.PunchImpactDelay;
            return true;
        }
        private void ResolveSwat()
        {
            if (giant == null || !Sleep.CanSwat) return;
            Vector3 center = giant.Position + Vector3.up * .9f;
            float angleCos = Mathf.Cos(config.SwatArc * .5f * Mathf.Deg2Rad);
            foreach (MosquitoBody body in bodies)
            {
                if (body == null || !body.IsAlive) continue;
                Vector3 delta = body.Position - center;
                if (delta.magnitude > config.SwatRadius + config.BodyRadius) continue;
                Vector3 horizontal = delta; horizontal.y = 0;
                if (horizontal.sqrMagnitude > .0001f && Vector3.Dot(giant.Facing, horizontal.normalized) < angleCos) continue;
                if (!ClearPath(center, body.Position)) continue;
                body.Kill(); network?.Effect(2, body.Position);
            }
        }
        private static bool ClearPath(Vector3 start, Vector3 end) => !Physics.Linecast(start, end, LayerMask.GetMask("Cover", "Ground"), QueryTriggerInteraction.Ignore);
        private void ApplyPose()
        {
            if (shownPhase == Sleep.Phase) return;
            shownPhase = Sleep.Phase;
            bool sleeping = shownPhase != GiantPhase.Awake;
            giant.MovementLocked = sleeping || Countdown > 0;
            Transform target = sleeping ? bed : bedExit;
            if (HasAuthority || localId == giantId) giant.TeleportTo(target.position, target.rotation);
            giantRig?.SetSleeping(sleeping);
        }

        private void BindCamera()
        {
            if (cameraController == null) return;
            if (localId == giantId) return;
            MosquitoBody own = FindBody(localId);
            if (own == null) return;
            bool showCapture = !own.IsAlive && Time.unscaledTime < dragonflyCameraUntil;
            Transform target = own.IsAlive || showCapture ? own.CameraTarget : giant.CameraTarget;
            if (cameraController.CurrentTarget != target) cameraController.Apply(CameraMode.ThirdPerson, target);
            if (orbit != null) orbit.Radius = own.IsAlive || showCapture ? config.CameraRadius : radius;
            if (!own.IsAlive && !spectating) { spectating = true; Hud?.ShowSpectatorTarget(FindPlayer(giantId)?.DisplayName ?? "Великан"); }
        }
        public bool InsideLight(Vector3 position)
        { Vector3 delta = position - lampCenter.position; delta.y = 0; return delta.sqrMagnitude <= config.LightRadius * config.LightRadius; }
        public bool CanSeeMosquito(Vector3 position) => localId != giantId || Sleep != null && Sleep.LampOn && InsideLight(position);
        public Vector3 FlightSpawn(int id)
        {
            return RoomSpawn(FlightSlot(id));
        }
        public Vector3 FlightRecovery(int id) => MosquitoWindowEntry.Recovery(FlightSlot(id));
        private int FlightSlot(int id)
        {
            int slot = 0;
            foreach (SessionPlayer player in roster) { if (player.Id == giantId) continue; if (player.Id == id) break; slot++; }
            return slot;
        }
        public static Vector3 RoomSpawn(int slot) => MosquitoWindowEntry.Spawn(slot);
        public bool InFlightBounds(Vector3 p) => Mathf.Abs(p.x) < 3.48f && Mathf.Abs(p.z) < 3.08f &&
            p.y >= config.BodyRadius * .5f && p.y <= config.FlightCeiling + .01f;
        public MosquitoBody FindBody(int id) { foreach (var body in bodies) if (body != null && body.Id == id) return body; return null; }
        public bool IsLiving(int id) => !departed.Contains(id) && FindBody(id) is MosquitoBody body && body.IsAlive;
        public void BodyDied(MosquitoBody body) { }
        public void PlayEffect(byte kind, Vector3 position, int attacker = -1)
        {
            if (kind == 3)
            {
                if (attacker == localId) { eatenLocally = true; dragonflyCameraUntil = Time.unscaledTime + .85f; }
                MosquitoDragonflyAttack.Show(dragonflyVisual, position, FindBody(attacker));
                if (Automated) Debug.Log($"[MosquitoDragonflyEffect] local={localId} victim={attacker}");
                return;
            }
            presentation?.Effect(kind, position, kind != 2 || CanSeeMosquito(position));
            if (kind != 0) return;
            ConfirmedBiteEffects++;
            FindBody(attacker)?.ShowBiteSuccess();
            giantRig?.React();
            // A hit on the victim is visible to the victim; it never reveals a hidden attacker.
            MosquitoBiteFeedback.Show(position);
            if (Automated) Debug.Log($"[MosquitoBite] local={localId} attacker={attacker} confirmed={ConfirmedBiteEffects}");
        }
        private void TickGiantBot(float dt)
        {
            botIn -= dt; if (botIn > 0) return; botIn = .7f;
            if (Sleep.Phase != GiantPhase.Awake) return;
            MosquitoBody nearest = null; float distance = float.MaxValue;
            foreach (var b in bodies) if (b != null && b.IsAlive && (b.Position - giant.Position).sqrMagnitude < distance) { nearest = b; distance = (b.Position - giant.Position).sqrMagnitude; }
            if (nearest != null && distance < 4f)
            {
                Vector3 toward = nearest.Position - giant.Position; giant.SetFacing(Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg); TrySwat(giantId);
            }
            else { giant.TeleportTo(bedExit.position, bedExit.rotation); TryBed(giantId); }
        }
        protected override void OnPlayerLeftRound(int playerId) => PlayerGone(playerId);
        public void PlayerGone(int id)
        {
            if (!HasAuthority || cleaned || !departed.Add(id)) return;
            if (id == giantId) { winnerGiant = false; EndMinigame(); return; }
            entryPrevious.Remove(id);
            MosquitoBody body = FindBody(id);
            if (body != null) { body.Kill(); bodies.Remove(body); if (body.IsSpawned) body.NetworkObject.Despawn(); else Destroy(body.gameObject); }
            if (AliveCount == 0) { winnerGiant = true; EndMinigame(); }
        }
        protected override void CollectResults(MinigameResults results)
        {
            foreach (SessionPlayer player in roster)
                results.Add(player.Id, PlaceFor(player.Id, giantId, winnerGiant, roster.Count,
                    departed.Contains(player.Id), config.IdleMosquitoPenalty, participated.Contains(player.Id)));
        }
        public static int PlaceFor(int id, int giantId, bool giantWon, int count, bool left, bool penalizeIdle, bool active) =>
            (id == giantId ? giantWon && !left : !giantWon && (left || !penalizeIdle || active)) ? 1 : count;
        protected override void OnRoundEnded()
        {
            Cleanup();
            network?.Publish();
        }
        protected override void OnDisable() { Cleanup(); base.OnDisable(); }
        private void Cleanup()
        {
            if (cleaned) return; cleaned = true;
            if (punch != null) { punch.PunchStarted -= PunchStarted; punch.ButtonOverride = previousPunchOverride; }
            if (giant != null) giant.MovementLocked = previousLock;
            if (giantRig != null) { giantRig.Restore(); Destroy(giantRig); giantRig = null; }
            if (giantAnimator != null) { giantAnimator.speed = previousAnimatorSpeed; giantAnimator.applyRootMotion = previousRootMotion; }
            parked.ReleaseAll();
            if (sharedLook != null && !lookWasEnabled) sharedLook.Disable();
            if (!RoundActive) foreach (SessionPlayer player in roster)
                if (player.Avatar != null && player.Avatar.TryGetComponent(out PlayerInputReader reader)) reader.enabled = false;
            if (cameraController != null && cameraRestore != null) cameraController.Apply(cameraModeRestore, cameraRestore);
            if (orbit != null) orbit.Radius = radius;
            presentation?.End(); Hud?.HideCountdown(); Hud?.HideStatus(); Hud?.HideSpectatorTarget();
            foreach (MosquitoBody body in bodies)
            {
                if (body == null) continue;
                if (body.Participated) participated.Add(body.Id);
                if (body.IsSpawned) { if (body.IsServer) body.NetworkObject.Despawn(); }
                else Destroy(body.gameObject);
            }
            bodies.Clear(); giant = null;
        }
        private void OnDestroy() => Cleanup();
    }
}
