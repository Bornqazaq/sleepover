using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Igruha.Core.UI;

namespace Igruha.Minigames.Mosquitoes
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class MosquitoBody : NetworkBehaviour
    {
        [SerializeField] private InputActionAsset inputTemplate;
        [SerializeField] private Transform visual;
        [SerializeField] private Transform cameraTarget;
        [SerializeField] private AudioSource buzz;
        public readonly NetworkVariable<int> PlayerId = new NetworkVariable<int>(-1);
        public readonly NetworkVariable<bool> Alive = new NetworkVariable<bool>(true);
        public readonly NetworkVariable<bool> Attached = new NetworkVariable<bool>();
        public readonly NetworkVariable<float> Cooldown = new NetworkVariable<float>();
        private readonly NetworkVariable<bool> steering = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private MosquitoesMinigame game;
        private Rigidbody body;
        private Collider hitbox;
        private Renderer[] renderers;
        private InputActionAsset input;
        private InputAction move, rise, descend, slow, bite;
        private Transform cameraTransform;
        private readonly System.Collections.Generic.List<Transform> wings = new System.Collections.Generic.List<Transform>(4);
        private readonly System.Collections.Generic.List<Quaternion> wingRest = new System.Collections.Generic.List<Quaternion>(4);
        private Vector3 direction, previousPosition, safePosition;
        private float stalled, boundaryTime;
        private bool local, bot, quiet, sentHold, lastAlive = true;
        private int offlineId = -1;
        private bool offlineAlive = true, offlineAttached;
        private float offlineCooldown;
        public float BiteProgress { get; set; }
        public bool Holding { get; set; }
        public bool Participated { get; set; }
        public int Id => IsSpawned ? PlayerId.Value : offlineId;
        public bool IsAlive => IsSpawned ? Alive.Value : offlineAlive;
        public bool IsAttached => IsSpawned ? Attached.Value : offlineAttached;
        public float BiteCooldown => IsSpawned ? Cooldown.Value : offlineCooldown;
        public bool IsLocal => IsSpawned ? IsOwner : local;
        public Vector3 Velocity { get; private set; }
        public Vector3 Position => body.position;
        public Transform CameraTarget => cameraTarget != null ? cameraTarget : transform;

        private void Awake()
        {
            body = GetComponent<Rigidbody>(); hitbox = GetComponent<Collider>();
            renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Transform child in GetComponentsInChildren<Transform>()) if (child.name.StartsWith("Wing"))
            { wings.Add(child); wingRest.Add(child.localRotation); }
            previousPosition = safePosition = transform.position;
            if (Camera.main != null) cameraTransform = Camera.main.transform;
        }
        public override void OnNetworkSpawn()
        {
            if (IsServer) PlayerId.Value = (int)OwnerClientId;
            game = FindFirstObjectByType<MosquitoesMinigame>();
            game?.RegisterBody(this);
            game?.Audio?.ConfigureBuzz(buzz);
            ConfigureInput(IsOwner, game != null && game.Automated);
        }
        public void Initialize(MosquitoesMinigame owner, int id, bool human, bool automated)
        {
            game = owner; offlineId = id; local = human; bot = automated;
            if (IsSpawned && IsServer) PlayerId.Value = id;
            game.RegisterBody(this);
            game.Audio?.ConfigureBuzz(buzz);
            ConfigureInput(human, automated);
        }
        private void ConfigureInput(bool human, bool automated)
        {
            local = human; bot = automated;
            bool drive = !IsSpawned || IsOwner;
            body.isKinematic = !drive;
            body.interpolation = drive ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            if (!human || automated || input != null || inputTemplate == null) return;
            input = Instantiate(inputTemplate);
            move = input.FindAction("Player/Move", true);
            rise = input.FindAction("Player/Jump", true);
            descend = input.FindAction("Player/Crouch", true);
            slow = input.FindAction("Player/Sprint", true);
            bite = input.FindAction("Player/Attack", true);
            input.Enable();
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
            if (enabledInput && bot)
            {
                Vector3 target = game.BiteTarget;
                if (BiteCooldown > 0 || !game.Sleep.CanBite)
                    target = new Vector3(Mathf.Sin(Time.time * .43f + Id) * 4f, 1.4f, Mathf.Cos(Time.time * .43f + Id) * 3f);
                Vector3 delta = target - Position;
                direction = Vector3.ClampMagnitude(delta * 2f, 1f);
                quiet = delta.magnitude < 1f;
                hold = delta.magnitude <= game.Config.BiteDistance * .9f;
            }
            else if (enabledInput && input != null)
            {
                Vector2 planar = move.ReadValue<Vector2>();
                Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                forward.y = 0; forward.Normalize();
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
            Velocity = (Position - previousPosition) / dt; previousPosition = Position;
            bool drives = !IsSpawned || IsOwner;
            if (drives)
            {
                bool moving = game.ControlsAvailable && IsAlive && !IsAttached;
                Vector3 target = moving ? direction * (quiet ? game.Config.QuietSpeed : game.Config.MaxSpeed) : Vector3.zero;
                body.linearVelocity = moving ? Vector3.MoveTowards(body.linearVelocity, target,
                    (direction.sqrMagnitude > .001f ? game.Config.Acceleration : game.Config.Braking) * dt) : Vector3.zero;
                if (body.position.y >= game.Config.FlightCeiling && body.linearVelocity.y > 0)
                    body.linearVelocity = new Vector3(body.linearVelocity.x, 0, body.linearVelocity.z);
                if (body.position.y > game.Config.FlightCeiling + .01f)
                    body.position = new Vector3(body.position.x, game.Config.FlightCeiling, body.position.z);
                Vector3 planar = body.linearVelocity; planar.y = 0;
                if (planar.sqrMagnitude > .03f) body.MoveRotation(Quaternion.LookRotation(planar));
            }
            if ((!IsSpawned || IsServer) && IsAlive)
            {
                bool outside = !game.InFlightBounds(Position);
                boundaryTime = outside ? boundaryTime + dt : 0;
                stalled = !IsAttached && Holding == false && Velocity.sqrMagnitude < .002f &&
                    ((IsSpawned ? steering.Value : direction.sqrMagnitude > .1f) || outside) ? stalled + dt : 0;
                if (!outside && Velocity.sqrMagnitude > .02f) safePosition = Position;
                if (boundaryTime >= game.Config.StuckSeconds || stalled >= game.Config.StuckSeconds)
                {
                    Vector3 reset = stalled >= game.Config.StuckSeconds || !game.InFlightBounds(safePosition) ? game.FlightSpawn(Id) : safePosition;
                    ResetPosition(reset); boundaryTime = stalled = 0;
                }
            }
        }
        private void LateUpdate()
        {
            if (game == null) return;
            bool visible = IsAlive && game.CanSeeMosquito(Position);
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = visible;
            if (visual != null) visual.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 55f) * 3f);
            for (int i = 0; i < wings.Count; i++) wings[i].localRotation = wingRest[i] * Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 95f) * 18f);
            if (buzz != null)
            {
                buzz.volume = IsAlive && !IsAttached && game.ControlsAvailable ?
                    Mathf.InverseLerp(game.Config.QuietThreshold, game.Config.LoudThreshold, Velocity.magnitude) : 0f;
                if (buzz.clip != null && buzz.volume > 0f && !buzz.isPlaying) buzz.Play();
                if (buzz.volume == 0f && buzz.isPlaying) buzz.Stop();
            }
        }
        [ServerRpc] private void HoldServerRpc(bool hold, ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId || game == null || !game.ControlsAvailable || !IsAlive) return;
            Holding = hold;
        }
        public void SetBiteState(bool attached, float cooldown)
        {
            if (IsSpawned) { if (!IsServer) return; Attached.Value = attached; Cooldown.Value = cooldown; }
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
            if (IsSpawned) ResetPositionClientRpc(position); else MoveTo(position);
        }
        [ClientRpc] private void ResetPositionClientRpc(Vector3 position) { if (IsOwner) MoveTo(position); }
        private void MoveTo(Vector3 position)
        {
            body.position = position; body.linearVelocity = Vector3.zero; previousPosition = position;
            if (IsSpawned && IsOwner) GetComponent<Igruha.Networking.ClientNetworkTransform>().Teleport(position, transform.rotation, Vector3.one);
        }
        public override void OnDestroy()
        {
            if (input != null) { input.Disable(); Destroy(input); }
            base.OnDestroy();
        }
    }
}
