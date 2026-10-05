using System;
using Igruha.Core.Minigame;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Igruha.Minigames.SumoRing
{
    public struct SumoCombatHit : INetworkSerializable
    {
        public int Attacker, Target;
        public SumoAttack Attack;
        public SumoContact Contact;
        public Vector3 Direction, Point;
        public float Speed, Slide;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Attacker); s.SerializeValue(ref Target); s.SerializeValue(ref Attack); s.SerializeValue(ref Contact);
            s.SerializeValue(ref Direction); s.SerializeValue(ref Point); s.SerializeValue(ref Speed); s.SerializeValue(ref Slide);
        }
    }

    /// <summary>One authority for Sumo contacts. Input is queued; all decisions occur in FixedUpdate.</summary>
    public sealed class SumoCombat : MonoBehaviour
    {
        private const int CommandCapacity = 64;
        private const float GroundProbeHeight = .18f, GroundProbeLength = .38f;
        private const float AimInterval = .08f, AimThreshold = 3f;
        private struct Intent { public int Id, Sequence; public SumoCommand Command; public float Yaw; }
        private readonly Intent[] queue = new Intent[CommandCapacity];
        private int queued, nextInputSequence;
        private SumoMinigame game;
        private SumoNetwork network;
        private Camera gameCamera;
        private SumoConfig config;
        private SumoFighter[] fighters = Array.Empty<SumoFighter>();
        private SumoCombatState[] states = Array.Empty<SumoCombatState>();
        private SumoCombatHit[] contacts = Array.Empty<SumoCombatHit>();
        private bool[] usedParry = Array.Empty<bool>();
        private SumoFighter local;
        private GameObject hud;
        private InputAction attackInput, guardInput;
        private bool attackHeld, guardHeld, inputBlocked;
        private float nextAim, nextGuardRetry, lastYaw, lastAimYaw;
        public event Action StateChanged;
        public event Action<SumoCombatHit> Contact;
        public int Count => states.Length;
        public bool Active => game != null && game.Running && game.Elapsed >= 0 && !game.Round.Finished;
        public double Elapsed => game.Elapsed;
        public SumoCombatState StateAt(int index) => states[index];
        public SumoFighter FighterAt(int index) => fighters[index];
        public SumoFighter Local => local;

        public void Bind(SumoMinigame owner, SumoConfig settings, Camera view)
        {
            Release(); game = owner; config = settings; gameCamera = view; network = GetComponent<SumoNetwork>();
            int n = game.Participants.Count;
            fighters = new SumoFighter[n]; states = new SumoCombatState[n]; contacts = new SumoCombatHit[n]; usedParry = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var p = game.Participants[i]; states[i] = SumoCombatState.Create(p.Player.Id);
                fighters[i] = p.Motor.gameObject.AddComponent<SumoFighter>();
                fighters[i].Bind(this, p, config, i);
                if (p.Input != null && p.Input.LocallyControlled) local = fighters[i];
            }
            attackInput = new InputAction("SumoPush", InputActionType.Button, "<Mouse>/leftButton");
            attackInput.AddBinding("<Gamepad>/rightTrigger");
            guardInput = new InputAction("SumoGuard", InputActionType.Button, "<Mouse>/rightButton");
            guardInput.AddBinding("<Gamepad>/leftTrigger");
            if (local != null) { attackInput.Enable(); guardInput.Enable(); }
        }
        public void BindHud(TMPro.TMP_FontAsset font)
        {
            hud = new GameObject("SumoCombatHUD", typeof(RectTransform)); hud.transform.SetParent(transform, false);
            hud.AddComponent<SumoCombatHud>().Bind(game, this, font);
        }
        public void ResetCombat()
        {
            queued = nextInputSequence = 0; attackHeld = guardHeld = false;
            for (int i = 0; i < states.Length; i++)
            {
                states[i] = SumoCombatState.Create(states[i].Id);
                if (fighters[i] != null) { fighters[i].ResetCombat(); fighters[i].ResetVisual(); }
            }
            StateChanged?.Invoke();
        }
        public void Release()
        {
            attackInput?.Dispose(); guardInput?.Dispose(); attackInput = guardInput = null;
            if (hud != null) { hud.SetActive(false); Destroy(hud); hud = null; }
            foreach (var f in fighters) if (f != null) { f.Release(); Destroy(f); }
            fighters = Array.Empty<SumoFighter>(); states = Array.Empty<SumoCombatState>(); local = null; queued = 0;
        }
        private void OnDestroy() => Release();
        private void OnApplicationFocus(bool focus) { if (!focus && local != null && !local.Input.Autopilot) CancelLocal(); }
        private void CancelLocal()
        {
            if (local != null) Submit(local.Id, SumoCommand.Cancel, lastYaw);
            attackHeld = guardHeld = false;
        }
        private void Update()
        {
            if (local == null || local.Input.Autopilot || attackInput == null) return;
            if (!Active || local.Participant.Dead || local.Input.Suspended || local.Motor.IsKnockedDown)
            { if (!inputBlocked) CancelLocal(); inputBlocked = true; attackHeld = attackInput.IsPressed(); guardHeld = guardInput.IsPressed(); return; }
            inputBlocked = false;
            Vector3 forward = gameCamera != null ? gameCamera.transform.forward : local.Motor.Facing;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            bool attack = attackInput.IsPressed(), guard = guardInput.IsPressed();
            // Press and release in one rendered frame is still one quick shove.
            if (attackInput.WasPressedThisFrame() && !attackHeld) Submit(local.Id, SumoCommand.AttackDown, yaw);
            if (attackInput.WasReleasedThisFrame() || (!attack && attackHeld)) Submit(local.Id, SumoCommand.AttackUp, yaw);
            if (guard && (!guardHeld || (local.State.Phase == SumoCombatPhase.Idle && Time.unscaledTime >= nextGuardRetry)))
            { Submit(local.Id, SumoCommand.GuardDown, yaw); nextGuardRetry = Time.unscaledTime + AimInterval; }
            if (!guard && guardHeld) Submit(local.Id, SumoCommand.GuardUp, yaw);
            attackHeld = attack; guardHeld = guard;
            if ((attack || guard) && Time.unscaledTime >= nextAim && Mathf.Abs(Mathf.DeltaAngle(lastAimYaw, yaw)) >= AimThreshold)
            { Submit(local.Id, SumoCommand.Aim, yaw); nextAim = Time.unscaledTime + AimInterval; lastAimYaw = yaw; }
            lastYaw = yaw;
        }
        /// <summary>Normal input and the development probe share this exact transport path.</summary>
        public void Submit(int id, SumoCommand command, float yaw)
        {
            if (network != null && network.IsSpawned)
            {
                int sequence = local != null && id == local.Id ? ++nextInputSequence : 0;
                if (!network.IsServer && sequence > 0)
                {
                    bool releasing = command == SumoCommand.Cancel || command == SumoCommand.GuardUp;
                    if (Active && Available(IndexOf(id)) && (releasing || Grounded(IndexOf(id))))
                        local.Predict(sequence, command, yaw);
                }
                network.SubmitCombat(id, command, yaw, sequence);
            }
            else Enqueue(id, command, yaw);
        }
        public void Enqueue(int id, SumoCommand command, float yaw, int sequence = 0)
        {
            if (queued >= queue.Length || (byte)command > (byte)SumoCommand.Cancel || float.IsNaN(yaw) || float.IsInfinity(yaw)) return;
            queue[queued++] = new Intent { Id = id, Command = command, Yaw = yaw, Sequence = sequence };
        }
        public int IndexOf(int id)
        { for (int i = 0; i < states.Length; i++) if (states[i].Id == id) return i; return -1; }
        public void ApplySnapshot(SumoCombatState state)
        {
            int i = IndexOf(state.Id);
            if (i < 0) return;
            states[i] = state;
            fighters[i].Observe(state);
        }
        public bool Owns(int id, ulong sender)
        {
            int i = IndexOf(id);
            return i >= 0 && fighters[i] != null && fighters[i].Identity != null && fighters[i].Identity.OwnerClientId == sender;
        }
        public bool Grounded(int index)
        {
            var f = fighters[index];
            return f != null && Physics.Raycast(f.Motor.Position + Vector3.up * GroundProbeHeight, Vector3.down, GroundProbeLength, f.Motor.GroundLayers, QueryTriggerInteraction.Ignore);
        }
        private bool Available(int i) => fighters[i] != null && !fighters[i].Participant.Dead && !fighters[i].Motor.IsKnockedDown && !fighters[i].Motor.MovementLocked && fighters[i].Motor.Position.y >= config.Height - config.FallTolerance;
        public void TickAuthority(double now)
        {
            if (!Active) { queued = 0; return; }
            bool changed = false;
            for (int i = 0; i < states.Length; i++)
            {
                changed |= SumoCombatRules.Advance(ref states[i], now, config);
                if (!Available(i) || ((states[i].Phase == SumoCombatPhase.Guard || states[i].Phase == SumoCombatPhase.Charge) && !Grounded(i)))
                    changed |= SumoCombatRules.Command(ref states[i], SumoCommand.Cancel, states[i].Yaw, now, config);
                usedParry[i] = false;
            }
            for (int q = 0; q < queued; q++)
            {
                var intent = queue[q]; int i = IndexOf(intent.Id);
                if (i < 0) continue;
                // Acknowledge even a rejected action: the owner must undo its visual
                // prediction when airborne, stunned or otherwise unavailable.
                if (intent.Sequence > 0)
                {
                    if (intent.Sequence <= states[i].ProcessedInput) continue;
                    states[i].ProcessedInput = intent.Sequence;
                    states[i].Revision++;
                    changed = true;
                }
                if (!Available(i)) continue;
                bool releasing = intent.Command == SumoCommand.Cancel || intent.Command == SumoCommand.GuardUp;
                if (!releasing && !Grounded(i)) continue;
                changed |= SumoCombatRules.Command(ref states[i], intent.Command, intent.Yaw, now, config);
            }
            queued = 0;
            int hits = 0;
            // Plan all contacts before mutating either fighter: simultaneous attacks trade fairly.
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                if (s.Phase != SumoCombatPhase.Windup || now < s.Until) continue;
                int target = Available(i) && Grounded(i) ? FindTarget(i) : -1;
                var hit = new SumoCombatHit { Attacker = s.Id, Target = target >= 0 ? states[target].Id : -1, Attack = s.Attack, Contact = SumoContact.Miss };
                if (target >= 0)
                {
                    var from = fighters[i].Motor.Position; var to = fighters[target].Motor.Position;
                    hit.Direction = to - from; hit.Direction.y = 0; hit.Direction.Normalize();
                    hit.Point = (from + to) * .5f + Vector3.up * fighters[target].BodyHeight * .65f;
                    var defending = states[target]; if (usedParry[target]) defending.ParryUntil = 0;
                    hit.Contact = SumoCombatRules.Contact(s, defending, from - to, Grounded(target), now, config);
                    if (hit.Contact == SumoContact.Parry) usedParry[target] = true;
                    hit.Speed = s.Attack == SumoAttack.Quick ? config.QuickPushSpeed : s.Attack == SumoAttack.Heavy ? config.HeavyPushSpeed : config.CounterPushSpeed;
                    hit.Slide = s.Attack == SumoAttack.Quick ? config.QuickSlideSeconds : config.HeavySlideSeconds;
                    if (hit.Contact == SumoContact.Block) hit.Speed *= config.GuardPushMultiplier;
                }
                contacts[hits++] = hit;
            }
            for (int n = 0; n < hits; n++)
            { int i = IndexOf(contacts[n].Attacker); SumoCombatRules.Recover(ref states[i], now, config); changed = true; }
            for (int n = 0; n < hits; n++)
            {
                var hit = contacts[n]; int a = IndexOf(hit.Attacker), t = IndexOf(hit.Target);
                if (t >= 0)
                {
                    if (hit.Contact == SumoContact.Parry)
                    {
                        SumoCombatRules.Stagger(ref states[a], now, config.ParriedStaggerSeconds);
                        SumoCombatRules.AwardCounter(ref states[t], hit.Attacker, now, config);
                    }
                    else if (hit.Contact != SumoContact.Block)
                        SumoCombatRules.Stagger(ref states[t], now, hit.Contact == SumoContact.GuardBreak ? config.GuardBreakSeconds : config.StaggerSeconds);
                }
                if (network != null && network.IsSpawned) network.BroadcastContact(hit);
                else PresentContact(hit);
            }
            if (changed)
            {
                for (int i = 0; i < fighters.Length; i++) if (fighters[i] != null) fighters[i].Observe(states[i]);
                StateChanged?.Invoke();
            }
        }
        private int FindTarget(int attacker)
        {
            int chosen = -1; float nearest = float.MaxValue;
            var source = fighters[attacker]; var s = states[attacker];
            for (int i = 0; i < fighters.Length; i++)
            {
                if (i == attacker || !Available(i) || (s.Attack == SumoAttack.Counter && states[i].Id != s.CounterTarget)) continue;
                Vector3 delta = fighters[i].Motor.Position - source.Motor.Position;
                if (!SumoCombatRules.IsFrontal(s.Forward, delta, config.AttackArc)) continue;
                if (BodyGap(source.Capsule, fighters[i].Capsule) > config.CombatReach) continue;
                if (delta.sqrMagnitude < nearest) { nearest = delta.sqrMagnitude; chosen = i; }
            }
            return chosen;
        }
        public static float BodyGap(CapsuleCollider a, CapsuleCollider b)
        {
            if (a == null || b == null) return float.MaxValue;
            Bounds x = a.bounds, y = b.bounds;
            float horizontal = new Vector2(x.center.x - y.center.x, x.center.z - y.center.z).magnitude;
            float vertical = Mathf.Max(0, Mathf.Abs(x.center.y - y.center.y) - Mathf.Max(0, x.extents.y - x.extents.x) - Mathf.Max(0, y.extents.y - y.extents.x));
            return Mathf.Sqrt(horizontal * horizontal + vertical * vertical) - x.extents.x - y.extents.x;
        }
        public void PresentContact(SumoCombatHit hit)
        {
            if (!Active) return;
            int a = IndexOf(hit.Attacker), t = IndexOf(hit.Target);
            if (a >= 0 && fighters[a] != null) fighters[a].Feedback(hit, false);
            if (t >= 0 && fighters[t] != null) fighters[t].Feedback(hit, true);
            Contact?.Invoke(hit);
        }
    }
}
