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
    public enum MosquitoBiteAccess { Unavailable, TooFar, Blocked, Ready }
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
        private float previousPunchCooldown;
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
        private CinemachineCamera flightCamera;
        private Transform lookAtRestore;
        private bool customLookAtRestore;
        private int localId = -1, giantId = -1;
        private string hint;
        private MosquitoesSnapshot receivedPose;
        private bool hasBitePose;
        private int furnitureMask, bitePathMask;
        private readonly RaycastHit[] bitePathHits = new RaycastHit[32];
        private readonly Collider[] biteOverlaps = new Collider[32];
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
        public Vector3 BiteTarget => !HasAuthority && hasBitePose ? receivedPose.BiteTarget : giantRig != null ? giantRig.BitePoint : giant != null ? giant.Position + Vector3.up : bed.position;
        public Vector3 BiteSurfaceOrigin => !HasAuthority && hasBitePose ? receivedPose.BiteSurface : giantRig != null ? giantRig.BiteSurfacePoint : BiteTarget;
        public float BiteHoldDuration => AliveCount == 1 ? config.SoloBiteHoldSeconds : config.BiteHoldSeconds;
        public Vector3 BiteAnchor(int id) => BiteAnchorForSlot(FlightSlot(id));
        public Vector3 BiteAnchorForSlot(int slot) => !HasAuthority && hasBitePose ? receivedPose.Anchor(slot) :
            giantRig != null ? giantRig.LandingPoint(slot) : BiteTarget;
        public Vector3 BiteSurface(int id) => BiteAnchor(id) - Vector3.up * MosquitoGiantRig.SurfaceOffset;
        public const float BiteContactDistance = .10f;
        public MosquitoBiteAccess BiteAccess(MosquitoBody body, Vector3 position)
        {
            if (!ControlsAvailable || body == null || !body.IsAlive || !body.HasEntered ||
                body.BiteCooldown > 0 || !Sleep.CanBite) return MosquitoBiteAccess.Unavailable;
            Vector3 delta = BiteAnchor(body.Id) - position;
            if (delta.sqrMagnitude > config.BiteDistance * config.BiteDistance) return MosquitoBiteAccess.TooFar;
            int overlaps = Physics.OverlapSphereNonAlloc(position, config.BodyRadius, biteOverlaps, bitePathMask, QueryTriggerInteraction.Ignore);
            if (overlaps == biteOverlaps.Length) return MosquitoBiteAccess.Blocked;
            for (int i = 0; i < overlaps; i++) if (BlocksBite(biteOverlaps[i])) return MosquitoBiteAccess.Blocked;
            if (delta.sqrMagnitude > .0001f)
            {
                int hits = Physics.SphereCastNonAlloc(position, config.BodyRadius, delta.normalized,
                    bitePathHits, delta.magnitude, bitePathMask, QueryTriggerInteraction.Ignore);
                if (hits == bitePathHits.Length) return MosquitoBiteAccess.Blocked;
                for (int i = 0; i < hits; i++) if (BlocksBite(bitePathHits[i].collider)) return MosquitoBiteAccess.Blocked;
            }
            return MosquitoBiteAccess.Ready;
        }
        private bool BlocksBite(Collider collider) => ((1 << collider.gameObject.layer) & furnitureMask) != 0 ||
            giantRig != null && giantRig.OwnsContact(collider);
        public int AliveCount { get { int n = 0; foreach (var b in bodies) if (b != null && b.IsAlive && !departed.Contains(b.Id)) n++; return n; } }

        protected override void Awake()
        {
            base.Awake(); network = GetComponent<MosquitoesNetwork>();
            furnitureMask = LayerMask.GetMask("Cover", "Ground"); bitePathMask = furnitureMask | (1 << 12);
            if (orbit != null) flightCamera = orbit.GetComponent<CinemachineCamera>();
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
            hasBitePose = false;
            swatCooldown = 0; swatImpact = -1; hudIn = 0; botIn = 0; ConfirmedBiteEffects = 0;
            EntryRemaining = MosquitoWindowEntry.Deadline; DragonflyKills = 0; entryPrevious.Clear();
            dragonflyCameraUntil = dragonflyFinishAt = 0; eatenLocally = false;
            cleaned = false; winnerGiant = false; spectating = false; departed.Clear(); participated.Clear();
            presentation?.Begin();
            if (cameraController != null) { cameraRestore = cameraController.CurrentTarget; cameraModeRestore = cameraController.CurrentMode; }
            if (orbit != null) radius = orbit.Radius;
            if (flightCamera != null) { lookAtRestore = flightCamera.Target.LookAtTarget; customLookAtRestore = flightCamera.Target.CustomLookAtTarget; }
            if (!HasAuthority) return;
            Timer?.StopTimer();
            giantId = SessionScoreboard.Current != null ? SessionScoreboard.Current.PickSpecialRole("Giant") : roles.Pick("Giant", roster);
            if (Debug.isDebugBuild && LaunchArguments.TryGetValue("--mosquito-giant", out string role) && int.TryParse(role, out int forcedRole)) debugGiantId = forcedRole;
            if (Debug.isDebugBuild && LaunchArguments.TryGetValue("--mosquito-check", out string probe) &&
                (probe == "round-timeout" || probe == "mosquitoes-leave")) debugDisableBots = true;
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
                Vector3 spawn = FlightSpawn(player.Id);
                Vector3 towardWindow = Vector3.ProjectOnPlane(MosquitoWindowEntry.Approach - spawn, Vector3.up);
                MosquitoBody body = Instantiate(mosquitoPrefab, spawn, Quaternion.LookRotation(towardWindow));
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
            if (punch != null)
            {
                previousPunchOverride = punch.ButtonOverride; previousPunchCooldown = punch.CooldownOverride;
                punch.ButtonOverride = this; punch.CooldownOverride = config.SwatCooldown; punch.PunchStarted += PunchStarted;
            }
            giantAnimator = giant.GetComponentInChildren<Animator>();
            if (giantAnimator != null) { previousAnimatorSpeed = giantAnimator.speed; previousRootMotion = giantAnimator.applyRootMotion; }
            if (sharedLook != null) lookWasEnabled = sharedLook.enabled;
            foreach (SessionPlayer p in roster) if (p.Id != giantId) parked.Park(p.Avatar);
            sharedLook?.Enable();
            giantRig = giant.gameObject.AddComponent<MosquitoGiantRig>(); giantRig.Initialize(giantAnimator);
            shownPhase = (GiantPhase)255;
            hint = localId == giantId ? "ВЕЛИКАН • E у кровати — встать / уснуть • ЛКМ — шлепок\nНакопи 60 секунд сна или поймай всех комаров" :
                $"КОМАР • WASD — полёт • Space / Ctrl — высота • Shift — тихий полёт\nЗа {MosquitoWindowEntry.Deadline:0} секунд влети в окно! Обратно нельзя. Затем держи ЛКМ у метки укуса 0,5 с";
            presentation?.SetRole(localId == giantId, hint);
            ApplyPose();
            RefreshTutorialInstructions();
        }
        protected override void ConfigureTutorial(TutorialScreen screen)
        {
            if (giantId < 0) return;
            bool human = localId == giantId;
            screen.SetRoleInstructions(human ? "ЧЕЛОВЕК" : "КОМАР",
                human ? "Накопи 60 секунд сна или прихлопни всех комаров" : "Не дай человеку выспаться за 120 секунд",
                human ? new[] { "E у кровати — уснуть / проснуться", "Два укуса будят и отнимают 1 секунду сна", "Проснулся — свет включён. ЛКМ — прихлопнуть комара" } :
                    new[] { "Влети в отмеченное окно за 10 секунд", "Два укуса за 4 секунды будят человека: атакуйте вместе", "После укуса отлетай. Последнему комару нужен один укус длиной 1 с" },
                human ? new[] { "E — кровать / лампа", "ЛКМ — шлепок", "Space — прыжок" } : new[] { "Space / Ctrl — высота", "Shift — тихо", "ЛКМ — держать укус" },
                human ? new[] { "WASD — ходить", "E — взаимодействовать", "ЛКМ — шлепок" } : new[] { "WASD — полёт", "Space / Ctrl — вверх / вниз", "Shift — тихий полёт", "ЛКМ — укус" });
        }
        public void RegisterBody(MosquitoBody body)
        {
            if (bodies.Contains(body)) return;
            // Remote kinematic flyers must not shove the owner's predicted flight.
            // Only mosquito pairs are ignored; walls and the giant remain solid.
            foreach (var other in bodies)
                if (other != null && other.CollisionShape != null && body.CollisionShape != null)
                    Physics.IgnoreCollision(body.CollisionShape, other.CollisionShape);
            bodies.Add(body);
        }
        public void UnregisterBody(MosquitoBody body) { if (!cleaned) bodies.Remove(body); }
        public void Receive(MosquitoesSnapshot snapshot)
        {
            if (HasAuthority || Sleep == null) return;
            giantId = snapshot.GiantId;
            receivedPose = snapshot; hasBitePose = true;
            Sleep.ApplySnapshot((GiantPhase)snapshot.Phase, snapshot.Sleep, snapshot.Transition, snapshot.Immunity, snapshot.Lamp, snapshot.RecentBites, snapshot.BiteWindow);
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
                presentation?.PaintHud(this, own, eatenLocally);
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
                Vector3 position = body.RulePosition;
                if (!body.HasEntered)
                {
                    Vector3 previous = entryPrevious.TryGetValue(body.Id, out var last) ? last : FlightSpawn(body.Id);
                    if (EntryRemaining > 0 && MosquitoWindowEntry.Crossed(previous, position))
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
                    entryPrevious[body.Id] = position;
                }
                else if (position.x > MosquitoWindowEntry.Plane + config.BodyRadius)
                    body.ResetPosition(MosquitoWindowEntry.ClampInside(position));
                float cooldown = Mathf.Max(0, body.BiteCooldown - dt);
                if (Sleep.LampOn && InsideLight(position)) body.Participated = true;
                Vector3 anchor = BiteAnchor(body.Id);
                bool valid = body.Holding && BiteAccess(body, position) == MosquitoBiteAccess.Ready;
                if (valid)
                {
                    // Acquisition steers to the skin; only actual contact earns a bite.
                    if (Vector3.Distance(position, anchor) <= BiteContactDistance) body.BiteProgress += dt;
                    else body.BiteProgress = 0;
                    if (body.BiteProgress >= BiteHoldDuration && Sleep.RegisterBite(AliveCount))
                    {
                        cooldown = config.BiteCooldown; body.BiteProgress = 0; valid = false; body.Participated = true;
                        network?.Effect(0, BiteSurface(body.Id), body.Id);
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
        public bool HandlePushButton(PlayerController player) => !ControlsAvailable || Sleep == null || !Sleep.CanSwat || swatCooldown > 0;
        private void PunchStarted() { if (localId == giantId) network?.RequestSwat(); }
        public bool TrySwat(int sender)
        {
            if (!HasAuthority || !ControlsAvailable || sender != giantId || !Sleep.CanSwat || swatCooldown > 0) return false;
            swatCooldown = config.SwatCooldown; swatImpact = giant.Config.PunchImpactDelay;
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
                Vector3 position = body.RulePosition;
                Vector3 delta = position - center;
                if (delta.magnitude > config.SwatRadius + config.BodyRadius) continue;
                Vector3 horizontal = delta; horizontal.y = 0;
                if (horizontal.sqrMagnitude > .0001f && Vector3.Dot(giant.Facing, horizontal.normalized) < angleCos) continue;
                if (!ClearPath(center, position)) continue;
                body.Kill(); network?.Effect(2, position);
            }
        }
        private bool ClearPath(Vector3 start, Vector3 end) => !Physics.Linecast(start, end, furnitureMask, QueryTriggerInteraction.Ignore);
        private void ApplyPose()
        {
            giant.MovementLocked = Sleep.Phase != GiantPhase.Awake || Countdown > 0;
            if (shownPhase == Sleep.Phase) return;
            shownPhase = Sleep.Phase;
            bool sleeping = shownPhase == GiantPhase.Sleeping || shownPhase == GiantPhase.LyingDown;
            if (sleeping) { punch?.CancelPendingPush(); swatImpact = -1; }
            Transform target = sleeping ? bed : bedExit;
            giantRig?.CaptureTransitionStart();
            if (HasAuthority || localId == giantId) giant.TeleportTo(target.position, target.rotation);
            giantRig?.SetSleeping(sleeping, Sleep.TransitionRemaining, bedExit.position, bedExit.rotation,
                Sleep.ImmunityRemaining > config.WakeSeconds);
        }

        private void BindCamera()
        {
            if (cameraController == null) return;
            if (localId == giantId)
            {
                Transform humanTarget = Sleep.Phase != GiantPhase.Awake && giantRig != null
                    ? giantRig.CameraTarget : giant.CameraTarget;
                if (humanTarget != null)
                {
                    if (cameraController.CurrentTarget != humanTarget) cameraController.Apply(CameraMode.ThirdPerson, humanTarget);
                    if (flightCamera != null) flightCamera.LookAt = humanTarget;
                }
                return;
            }
            MosquitoBody own = FindBody(localId);
            if (own == null) return;
            bool showCapture = !own.IsAlive && Time.unscaledTime < dragonflyCameraUntil;
            Transform target = own.IsAlive || showCapture ? own.CameraTarget : giant.CameraTarget;
            if (cameraController.CurrentTarget != target || flightCamera != null && flightCamera.Follow != target)
                cameraController.Apply(CameraMode.ThirdPerson, target);
            // The network player binder sets an explicit LookAt on the parked avatar.
            // Switching only Follow leaves the mosquito out of frame on every network peer.
            if (flightCamera != null) flightCamera.LookAt = target;
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
        public bool InFlightBounds(Vector3 p) => Mathf.Abs(p.x) < 3.48f + config.BodyRadius && Mathf.Abs(p.z) < 3.08f + config.BodyRadius &&
            p.y >= -config.BodyRadius && p.y <= config.FlightCeiling + config.BodyRadius;
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
            giantRig?.React(position);
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
        public override bool ResultsAreTeams => true;
        public override string ResultMetricTitle => "КОМАНДА";
        public override RoundResultDetail GetResultDetail(int playerId) => new RoundResultDetail(
            playerId == giantId ? "ЧЕЛОВЕК" : "КОМАРЫ",
            playerId == giantId ? $"Сон: {Sleep?.Sleep:0.0} / {config.SleepTarget:0} с" : "Не дать человеку выспаться");
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
            if (punch != null) { punch.CancelPendingPush(); punch.PunchStarted -= PunchStarted; punch.ButtonOverride = previousPunchOverride; punch.CooldownOverride = previousPunchCooldown; }
            if (giant != null) giant.MovementLocked = previousLock;
            if (giantRig != null) { giantRig.Restore(); Destroy(giantRig); giantRig = null; }
            if (giantAnimator != null) { giantAnimator.speed = previousAnimatorSpeed; giantAnimator.applyRootMotion = previousRootMotion; }
            parked.ReleaseAll();
            if (sharedLook != null && !lookWasEnabled) sharedLook.Disable();
            if (!RoundActive) foreach (SessionPlayer player in roster)
                if (player.Avatar != null && player.Avatar.TryGetComponent(out PlayerInputReader reader)) reader.enabled = false;
            if (cameraController != null && cameraRestore != null) cameraController.Apply(cameraModeRestore, cameraRestore);
            if (flightCamera != null)
            {
                flightCamera.Target.LookAtTarget = lookAtRestore;
                flightCamera.Target.CustomLookAtTarget = customLookAtRestore;
            }
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
