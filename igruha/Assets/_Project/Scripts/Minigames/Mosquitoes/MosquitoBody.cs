using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Igruha.Core.UI;
using Igruha.Networking;

namespace Igruha.Minigames.Mosquitoes
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class MosquitoBody : NetworkBehaviour
    {
        private const float BoundaryCorrectionRetrySeconds = .25f;
        [SerializeField] private InputActionAsset inputTemplate;
        [SerializeField] private Transform visual;
        [SerializeField] private GameObject[] characterVisuals;
        public readonly NetworkVariable<int> CharacterIndex = new NetworkVariable<int>(0);
        private int initialCharacterIndex;
        private Vector3 initialPosition;
        private readonly NetworkVariable<Vector3> spawnPosition = new NetworkVariable<Vector3>();
        private int shownCharacter = -1;
        private MosquitoFlightVisual flightVisual;
        public void ShowBiteSuccess() => flightVisual?.Bite();
        public int VisualCharacterIndex => shownCharacter;
        [SerializeField] private Transform cameraTarget;
        [SerializeField] private AudioSource buzz;
        public readonly NetworkVariable<int> PlayerId = new NetworkVariable<int>(-1);
        public readonly NetworkVariable<bool> EnteredRoom = new NetworkVariable<bool>();
        private bool offlineEntered, predictedEntered;
        public bool HasEntered => IsSpawned ? EnteredRoom.Value : offlineEntered;
        public bool InsideForMovement => HasEntered || predictedEntered;
        public void ConfirmEntry()
        {
            if (IsSpawned) { if (!IsServer) return; EnteredRoom.Value = true; }
            else offlineEntered = true;
        }
        public readonly NetworkVariable<bool> Alive = new NetworkVariable<bool>(true);
        public readonly NetworkVariable<bool> Attached = new NetworkVariable<bool>();
        public readonly NetworkVariable<float> Cooldown = new NetworkVariable<float>();
        private readonly NetworkVariable<float> biteFill = new NetworkVariable<float>();
        private readonly NetworkVariable<bool> steering = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private MosquitoesMinigame game;
        private Rigidbody body;
        private ClientNetworkTransform flightTransform;
        private Collider hitbox;
        private InputActionAsset input;
        private InputActionMap flightActions;
        private InputAction move, rise, descend, slow, bite;
        private Transform cameraTransform;
        private Vector3 direction, previousPosition;
        private float stalled;
        private float boundaryCorrectionIn;
        private bool local, bot, quiet, sentHold, lastAlive = true;
        private int offlineId = -1;
        private bool offlineAlive = true, offlineAttached;
        private float offlineCooldown;
        public float BiteProgress { get; set; }
        public float BiteFraction => IsSpawned ? biteFill.Value : game != null ? Mathf.Clamp01(BiteProgress / game.BiteHoldDuration) : 0;
        public bool Holding { get; set; }
        public bool Participated { get; set; }
        public int Id => IsSpawned ? PlayerId.Value : offlineId;
        public bool IsAlive => IsSpawned ? Alive.Value : offlineAlive;
        public bool IsAttached => IsSpawned ? Attached.Value : offlineAttached;
        public float BiteCooldown => IsSpawned && !IsServer ? Cooldown.Value : offlineCooldown;
        public bool IsLocal => IsSpawned ? IsOwner : local;
        public Vector3 Velocity { get; private set; }
        public Vector3 Position => body.position;
        // Rules consume the received owner pose, not the extra delayed rendering interpolation.
        public Vector3 RulePosition => flightTransform != null && flightTransform.HasRemoteSample ? flightTransform.ReceivedPosition : Position;
        public RigidbodyInterpolation FlightInterpolation => body.interpolation;
        public bool FlightKinematic => body.isKinematic;
        public float BuzzVolume => buzz != null ? buzz.volume : 0;
        public int VisibleRendererCount
        {
            get { return flightVisual != null ? flightVisual.VisibleCount : 0; }
        }
        public Transform CameraTarget => cameraTarget != null ? cameraTarget : transform;
        public Collider CollisionShape => hitbox;

        private void Awake()
        {
            body = GetComponent<Rigidbody>(); hitbox = GetComponent<Collider>();
            flightTransform = GetComponent<ClientNetworkTransform>();
            previousPosition = transform.position;
            if (Camera.main != null) cameraTransform = Camera.main.transform;
        }
        public override void OnNetworkSpawn()
        {
            if (IsServer) PlayerId.Value = (int)OwnerClientId;
            if (IsServer) CharacterIndex.Value = initialCharacterIndex;
            if (IsServer) spawnPosition.Value = initialPosition;
            game = FindFirstObjectByType<MosquitoesMinigame>();
            game?.RegisterBody(this);
            game?.Audio?.ConfigureBuzz(buzz);
            ConfigureInput(IsOwner, game != null && game.Automated);
            // NGO moves Transform after prefab Awake. With Rigidbody interpolation the
            // owner's physics pose may still be the prefab origin (inside the floor).
            // Seed both poses before the first simulation and owner transform publication.
            transform.position = spawnPosition.Value;
            body.position = spawnPosition.Value;
            previousPosition = spawnPosition.Value;
            if (IsOwner && flightTransform != null) flightTransform.Teleport(spawnPosition.Value, transform.rotation, Vector3.one);
            ApplyCharacter(CharacterIndex.Value);
        }
        public override void OnNetworkDespawn() { flightActions?.Disable(); game?.UnregisterBody(this); }
        public override void OnGainedOwnership() { if (game != null) ConfigureInput(true, game.Automated); }
        public override void OnLostOwnership() { if (game != null) ConfigureInput(false, game.Automated); }
        public void Initialize(MosquitoesMinigame owner, int id, bool human, bool automated, int characterIndex)
        {
            game = owner; offlineId = id; local = human; bot = automated;
            initialPosition = transform.position;
            body.position = initialPosition; previousPosition = initialPosition;
            if (IsSpawned && IsServer) PlayerId.Value = id;
            initialCharacterIndex = characterIndex;
            ApplyCharacter(characterIndex);
            game.RegisterBody(this);
            game.Audio?.ConfigureBuzz(buzz);
            ConfigureInput(human, automated);
        }
        private void ApplyCharacter(int index)
        {
            if (characterVisuals == null || characterVisuals.Length == 0) return;
            index = Mathf.Clamp(index, 0, characterVisuals.Length - 1);
            if (shownCharacter == index) return;
            if (visual != null) { visual.gameObject.SetActive(false); Destroy(visual.gameObject); }
            shownCharacter = index;
            visual = Instantiate(characterVisuals[index], transform, false).transform;
            flightVisual = new MosquitoFlightVisual(visual, Id);
        }
        private void ConfigureInput(bool human, bool automated)
        {
            local = human; bot = automated;
            bool drive = !IsSpawned || IsOwner;
            body.isKinematic = !drive;
            body.collisionDetectionMode = drive ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;
            body.interpolation = drive ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            if (!human || automated) { flightActions?.Disable(); return; }
            if (input != null) { flightActions.Enable(); return; }
            if (inputTemplate == null) { Debug.LogError("Mosquito flight has no input actions.", this); return; }
            input = Instantiate(inputTemplate);
            input.devices = null; input.bindingMask = null;
            flightActions = input.FindActionMap("Player", true);
            move = input.FindAction("Player/Move", true);
            rise = input.FindAction("Player/Jump", true);
            descend = input.FindAction("Player/Crouch", true);
            slow = input.FindAction("Player/Sprint", true);
            bite = input.FindAction("Player/Attack", true);
            flightActions.Enable();
        }
        private void Update()
        {
            if (game == null) return;
            if (IsAlive != lastAlive)
            {
                lastAlive = IsAlive;
                if (!lastAlive) game.BodyDied(this);
            }
            hitbox.enabled = IsAlive;
            if (!IsLocal && IsSpawned) return;
            bool enabledInput = game.ControlsAvailable && IsAlive &&
                !TutorialScreen.PointerInputActive && !(PauseScreen.Current != null && PauseScreen.Current.IsPaused);
            bool hold = false;
            direction = Vector3.zero; quiet = false;
            if (enabledInput && bot && !(Debug.isDebugBuild && game.LocalId == Id && Id == 2 &&
                Igruha.Core.Session.LaunchArguments.TryGetValue("--mosquito-check", out string scenario) && scenario == "entry-timeout"))
            {
                Vector3 target = InsideForMovement ? game.BiteAnchor(Id) : MosquitoWindowEntry.Approach;
                if (InsideForMovement && Vector2.Distance(new Vector2(Position.x, Position.z), new Vector2(target.x, target.z)) > .45f)
                    target.y = game.Config.FlightCeiling - .08f;
                if (InsideForMovement && (BiteCooldown > 0 || !game.Sleep.CanBite))
                    target = new Vector3(Mathf.Sin(Time.time * .43f + Id) * 2.6f, 1.6f, Mathf.Cos(Time.time * .43f + Id) * 1.6f);
                Vector3 delta = target - Position;
                direction = Vector3.ClampMagnitude(delta * 2f, 1f);
                quiet = delta.magnitude < 1f;
                hold = InsideForMovement && Vector3.Distance(Position, game.BiteAnchor(Id)) <= game.Config.BiteDistance * .9f;
            }
            else if (enabledInput && input != null)
            {
                if (!flightActions.enabled) flightActions.Enable();
                if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
                Vector2 planar = move.ReadValue<Vector2>();
                Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                forward.y = 0;
                if (forward.sqrMagnitude < .001f) forward = transform.forward;
                forward.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                direction = Vector3.ClampMagnitude(forward * planar.y + right * planar.x +
                    Vector3.up * ((rise.IsPressed() ? 1 : 0) - (descend.IsPressed() ? 1 : 0)), 1f);
                quiet = slow.IsPressed(); hold = bite.IsPressed();
            }
            if (sentHold != hold)
            {
                sentHold = hold;
                if (IsSpawned) HoldServerRpc(hold); else Holding = hold;
            }
            if (IsSpawned && IsOwner) steering.Value = direction.sqrMagnitude > .1f;
        }
        private void FixedUpdate()
        {
            if (game == null) return;
            float dt = Time.fixedDeltaTime;
            if (!predictedEntered && MosquitoWindowEntry.Crossed(previousPosition, Position)) predictedEntered = true;
            Velocity = (Position - previousPosition) / dt; previousPosition = Position;
            bool drives = !IsSpawned || IsOwner;
            if (drives)
            {
                bool attached = IsAttached && (bot ? Holding || sentHold : sentHold);
                bool moving = game.ControlsAvailable && IsAlive && !attached;
                Vector3 target = moving ? direction * (quiet ? game.Config.QuietSpeed : game.Config.MaxSpeed) : Vector3.zero;
                body.linearVelocity = moving ? Vector3.MoveTowards(body.linearVelocity, target,
                    (direction.sqrMagnitude > .001f ? game.Config.Acceleration : game.Config.Braking) * dt) : Vector3.zero;
                if (InsideForMovement)
                {
                    body.position = MosquitoWindowEntry.ClampInside(body.position);
                    float outwardLimit = Mathf.Max(0, (MosquitoWindowEntry.Plane - body.position.x) / dt);
                    body.linearVelocity = new Vector3(Mathf.Min(outwardLimit, body.linearVelocity.x), body.linearVelocity.y, body.linearVelocity.z);
                    float ceiling = game.Config.FlightCeiling;
                    if (body.position.y >= ceiling && body.linearVelocity.y > 0)
                        body.linearVelocity = new Vector3(body.linearVelocity.x, 0, body.linearVelocity.z);
                    if (body.position.y > ceiling + .01f)
                        body.position = new Vector3(body.position.x, ceiling, body.position.z);
                }
                if (attached) body.MovePosition(Vector3.MoveTowards(Position, game.BiteAnchor(Id), dt * 3));
                Vector3 planar = body.linearVelocity; planar.y = 0;
                if (planar.sqrMagnitude > .03f) body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(planar), dt * 540));
            }
            if ((!IsSpawned || IsServer) && IsAlive && HasEntered)
            {
                bool inside = game.InFlightBounds(RulePosition);
                if (!inside)
                {
                    boundaryCorrectionIn -= dt;
                    if (boundaryCorrectionIn <= 0)
                    {
                        ResetPosition(game.FlightRecovery(Id));
                        boundaryCorrectionIn = BoundaryCorrectionRetrySeconds;
                    }
                    stalled = 0;
                    return;
                }
                boundaryCorrectionIn = 0;
                stalled = !IsAttached && Velocity.sqrMagnitude < .002f &&
                    (IsSpawned ? steering.Value : direction.sqrMagnitude > .1f) ? stalled + dt : 0;
                if (stalled >= game.Config.StuckSeconds)
                {
                    ResetPosition(game.FlightRecovery(Id)); stalled = 0;
                }
            }
        }
        private void LateUpdate()
        {
            if (game == null) return;
            if (IsSpawned && shownCharacter != CharacterIndex.Value) ApplyCharacter(CharacterIndex.Value);
            bool visible = IsAlive && game.CanSeeMosquito(Position);
            flightVisual?.Tick(Velocity, game.Config.MaxSpeed, IsAttached, Velocity.magnitude <= game.Config.QuietSpeed + .05f,
                visible, game.BiteSurface(Id), Time.deltaTime);
            if (buzz != null)
            {
                float volume = IsAlive && !IsAttached && game.ControlsAvailable ?
                    Mathf.InverseLerp(game.Config.QuietSpeed, game.Config.MaxSpeed, Velocity.magnitude) : 0f;
                buzz.volume = Mathf.MoveTowards(buzz.volume, volume, Time.deltaTime * 8);
                if (buzz.clip != null && buzz.volume > 0f && !buzz.isPlaying) buzz.Play();
                if (buzz.volume == 0f && buzz.isPlaying) buzz.Stop();
            }
        }
        [ServerRpc] private void HoldServerRpc(bool hold, ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId || game == null || hold && (!game.ControlsAvailable || !IsAlive)) return;
            Holding = hold;
        }
        public void SetBiteState(bool attached, float cooldown)
        {
            if (IsSpawned && !IsServer) return;
            offlineCooldown = cooldown;
            if (IsSpawned) { if (!IsServer) return; Attached.Value = attached; Cooldown.Value = Mathf.Ceil(cooldown * 10) / 10; biteFill.Value = Mathf.Round(Mathf.Clamp01(BiteProgress / game.BiteHoldDuration) * 20) / 20; }
            else { offlineAttached = attached; offlineCooldown = cooldown; }
        }
        public void Kill()
        {
            if (!IsAlive) return;
            Holding = false; BiteProgress = 0; SetBiteState(false, BiteCooldown);
            if (IsSpawned) { if (IsServer) Alive.Value = false; }
            else offlineAlive = false;
        }
        public void ResetPosition(Vector3 position)
        {
            if (IsSpawned) { if (IsServer) ResetPositionClientRpc(position); }
            else MoveTo(position);
        }
        [ClientRpc] private void ResetPositionClientRpc(Vector3 position) { if (IsOwner) MoveTo(position); }
        private void MoveTo(Vector3 position)
        {
            body.position = position; body.linearVelocity = Vector3.zero; previousPosition = position; Velocity = Vector3.zero;
            if (IsSpawned && IsOwner) flightTransform.Teleport(position, transform.rotation, Vector3.one);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public string InputDiagnostic => $"input={input != null} enabled={input != null && input.enabled} rise={rise?.IsPressed()} move={move?.ReadValue<Vector2>()} keyboard={Keyboard.current?.spaceKey.isPressed} direction={direction:F2} pointer={TutorialScreen.PointerInputActive} paused={PauseScreen.Current != null && PauseScreen.Current.IsPaused} bot={bot} local={local} game={game?.GetInstanceID()} controls={game?.ControlsAvailable} active={enabled}";
        public void MoveForBoundaryProbe(Vector3 position)
        {
            if (!IsSpawned || !IsOwner) return;
            enabled = false;
            MoveTo(position);
        }
#endif
        public override void OnDestroy()
        {
            if (input != null) { input.Disable(); Destroy(input); }
            base.OnDestroy();
        }
    }
}
