using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>Server-driven pursuit; clients reproduce the same telegraph before the impact.</summary>
    public sealed class PitBear : MonoBehaviour
    {
        // Existing values are kept because the state is replicated as a byte.
        public enum BearState { Patrol, WindUp, Chase, Taunt, Attack, Recovery, Watching }

        [Header("Original bear")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;
        [SerializeField] private string speedParameter = "Speed";
        [SerializeField] private string strikeParameter = "Strike";
        [SerializeField] private string roarParameter = "Roar";
        [SerializeField] private string alertParameter = "Alert";
        [Header("Weight and steering")]
        [SerializeField] private float turnSpeed = 150f;
        [SerializeField] private float wallMargin = .8f;
        [SerializeField] private float acceleration = 6f;
        [Header("Attack — matches Bruno_Strike, 1.5 seconds")]
        [SerializeField] private float attackContactTime = .55f;
        [SerializeField] private float attackDuration = 1.5f;
        [SerializeField] private float attackRecovery = .45f;

        public event Action<PlayerController, Vector3> Caught;
        public BearState State => state;
        public PlayerController Target { get; private set; }
        public Transform VisualRoot => visualRoot;
        public float AnimatorSpeed => currentSpeed;

        private sealed class Runner
        {
            internal PlayerController Player;
            internal Animator Animator;
            internal float StandingTime;
            internal bool Ready;
        }
        private readonly List<Runner> runners = new List<Runner>(8);
        private static readonly int FlyBack = Animator.StringToHash("FlyBack");
        private static readonly int FallForward = Animator.StringToHash("FallForward");
        private static readonly int StandUpBack = Animator.StringToHash("StandUpFromBack");
        private static readonly int StandUpForward = Animator.StringToHash("StandUpFromForward");
        private float chaseSpeed = 5.5f, patrolSpeed = 2.1f, strikeRadius = 2.35f;
        private float windUpDuration = 3f, knockbackSpeed = 8f, pitRadius = 8.64f;
        private float windUpLeft, patrolAngle, currentSpeed, attackElapsed, recoveryLeft;
        private float patrolPauseIn = 9f, patrolPauseLeft;
        private bool hitEvaluated;
        private PlayerController attackVictim;
        private Vector3 attackDirection, previousPosition;
        private bool visualPositionKnown;
        private BearState state = BearState.Patrol;

        public void Configure(float chase, float patrol, float strike, float windUp, float knockback, float pit)
        {
            chaseSpeed=chase; patrolSpeed=patrol; strikeRadius=strike; windUpDuration=windUp;
            knockbackSpeed=knockback; pitRadius=pit; runners.Clear(); Target=null;
            currentSpeed=0; windUpLeft=0; attackVictim=null; state=BearState.Patrol;
            patrolPauseIn=9; patrolPauseLeft=0; visualPositionKnown=false;
        }

        /// <summary>Called when the hatch opens, while the player is still above the pit.</summary>
        public void RegisterFallen(PlayerController player)
        {
            if(player==null)return;
            ForgetRunner(player);
            runners.Add(new Runner { Player=player, Animator=player.GetComponentInChildren<Animator>() });
        }

        public void ForgetRunner(PlayerController player)
        {
            for(int i=runners.Count-1;i>=0;i--)
                if(runners[i].Player==null || runners[i].Player==player)runners.RemoveAt(i);
        }

        /// <summary>
        /// The head start begins only after landing AND completing the get-up.
        /// Remote motors are disabled: use their replicated animation and observed floor position.
        /// Readiness latches, so jumping later cannot renew protection.
        /// </summary>
        public bool CanChase(PlayerController player,float deltaTime)
        {
            for(int i=0;i<runners.Count;i++)
            {
                Runner runner=runners[i];if(runner.Player!=player)continue;
                if(runner.Ready)return true;
                bool onFloor=player.transform.position.y<=transform.position.y+.65f;
                bool recovering=RecoveryAnimation(runner.Animator);
                if(player.enabled)
                {
                    onFloor &= player.IsGrounded;
                    recovering |= player.IsKnockedDown || player.MovementLocked;
                }
                else
                {
                    RaycastHit hit;
                    onFloor &= Physics.Raycast(player.transform.position+Vector3.up*.25f,Vector3.down,out hit,.6f,
                        Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) && hit.point.y<=transform.position.y+.3f;
                }
                runner.StandingTime=onFloor && !recovering ? runner.StandingTime+deltaTime : 0;
                runner.Ready=runner.StandingTime>=.3f;
                if(runner.Ready)Trace("Ready "+player.name+"; full head start begins after recovery");
                return runner.Ready;
            }
            return false;
        }

        private static bool RecoveryAnimation(Animator a)
        {
            if(a==null || !a.isInitialized)return false;
            return IsRecovery(a.GetCurrentAnimatorStateInfo(0).shortNameHash) ||
                (a.IsInTransition(0) && IsRecovery(a.GetNextAnimatorStateInfo(0).shortNameHash));
        }
        private static bool IsRecovery(int hash)=>hash==FlyBack || hash==FallForward || hash==StandUpBack || hash==StandUpForward;

        /// <summary>Only the authoritative minigame calls Tick; impacts are decided here.</summary>
        public void Tick(float deltaTime,PlayerController nearest,bool someoneOnLowestCage)
        {
            if(deltaTime<=0)return;
            if(state==BearState.Attack){TickAttack(deltaTime);return;}
            if(state==BearState.Recovery)
            {
                currentSpeed=0;recoveryLeft-=deltaTime;
                if(recoveryLeft>0)return;
                SetState(BearState.Chase);
            }
            if(nearest==null)
            {
                Target=null;
                for(int i=0;i<runners.Count;i++)
                    if(runners[i].Player!=null && !runners[i].Ready)
                    {
                        SetState(BearState.Watching);currentSpeed=0;
                        FaceTowards(runners[i].Player.transform.position-transform.position,deltaTime);
                        return;
                    }
                SetState(someoneOnLowestCage?BearState.Taunt:BearState.Patrol);
                if(someoneOnLowestCage)currentSpeed=0;else Patrol(deltaTime);
                return;
            }
            if(Target!=nearest)
            {
                Target=nearest;windUpLeft=windUpDuration;currentSpeed=0;
                SetState(BearState.WindUp);
                // This frame's delta belongs to the preceding state. Starting a
                // target on a long frame must still grant the full head start.
                if(windUpDuration>0)
                {
                    FaceTowards(nearest.transform.position-transform.position,deltaTime);
                    return;
                }
            }
            Vector3 delta=nearest.transform.position-transform.position;delta.y=0;
            FaceTowards(delta,deltaTime);
            if(windUpLeft>0)
            {
                windUpLeft-=deltaTime;currentSpeed=0;
                if(windUpLeft<=0)SetState(BearState.Chase);
                return;
            }
            SetState(BearState.Chase);
            if(delta.magnitude<=strikeRadius && Vector3.Dot(transform.forward,delta.normalized)>.75f)
            {
                attackVictim=nearest;attackDirection=transform.forward;attackElapsed=0;hitEvaluated=false;
                currentSpeed=0;SetState(BearState.Attack);return;
            }
            Steer(delta,chaseSpeed,deltaTime);
        }

        private void TickAttack(float dt)
        {
            float previous=attackElapsed;attackElapsed+=dt;
            // Short committed lunge; no homing or turning during the swipe.
            float lungeTime=Mathf.Max(0,Mathf.Min(attackElapsed,attackContactTime)-Mathf.Max(previous,.28f));
            MoveBy(attackDirection*(lungeTime*1.65f));currentSpeed=0;
            if(!hitEvaluated && attackElapsed>=attackContactTime)
            {
                hitEvaluated=true;
                if(attackVictim!=null)
                {
                    Vector3 delta=attackVictim.transform.position-transform.position;
                    float vertical=Mathf.Abs(delta.y);delta.y=0;
                    if(vertical<1.3f && delta.magnitude<=strikeRadius+.1f && Vector3.Dot(attackDirection,delta.normalized)>.35f)
                    {
                        Vector3 impulse=(delta.sqrMagnitude>.001f?delta.normalized:attackDirection)+Vector3.up*.35f;
                        var victim=attackVictim;ForgetRunner(victim);Target=null;
                        Trace("Contact "+victim.name+" at "+attackElapsed.ToString("F2")+" s");
                        Caught?.Invoke(victim,impulse.normalized*knockbackSpeed);
                    }
                }
            }
            if(attackElapsed>=attackDuration)
            {
                attackVictim=null;recoveryLeft=attackRecovery;
                SetState(BearState.Recovery);
            }
        }

        public void ApplyNetworkState(BearState next,float animatorSpeed)
        {SetState(next);currentSpeed=animatorSpeed;}

        public void PlayStrike()
        {
            if(animator!=null){animator.ResetTrigger(roarParameter);animator.ResetTrigger(alertParameter);}
            Trigger(strikeParameter);
        }

        private void Patrol(float dt)
        {
            if(patrolPauseLeft>0){patrolPauseLeft-=dt;currentSpeed=0;return;}
            patrolPauseIn-=dt;
            if(patrolPauseIn<=0){patrolPauseLeft=1.2f;patrolPauseIn=9f;currentSpeed=0;return;}
            float radius=Mathf.Max(1,pitRadius-wallMargin*2);
            patrolAngle+=patrolSpeed/radius*dt*Mathf.Rad2Deg;
            float variedRadius=radius-.3f+.3f*Mathf.Sin(patrolAngle*Mathf.Deg2Rad*1.7f);
            Vector3 target=Quaternion.Euler(0,patrolAngle+18,0)*Vector3.forward*variedRadius;
            Vector3 delta=target-transform.position;delta.y=0;FaceTowards(delta,dt);Steer(delta,patrolSpeed,dt);
        }
        private void Steer(Vector3 direction,float speed,float dt)
        {
            float alignment=direction.sqrMagnitude>.001f?Vector3.Dot(transform.forward,direction.normalized):0;
            float wanted=speed*Mathf.InverseLerp(.15f,.9f,alignment);
            currentSpeed=Mathf.MoveTowards(currentSpeed,wanted,acceleration*dt);
            MoveBy(transform.forward*(currentSpeed*dt));
        }
        private void MoveBy(Vector3 delta)
        {
            Vector3 next=transform.position+delta;Vector2 flat=new Vector2(next.x,next.z);
            flat=Vector2.ClampMagnitude(flat,Mathf.Max(.5f,pitRadius-wallMargin));next.x=flat.x;next.z=flat.y;transform.position=next;
        }
        private void FaceTowards(Vector3 direction,float dt)
        {
            direction.y=0;if(direction.sqrMagnitude<.0001f)return;
            transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(direction),turnSpeed*dt);
        }
        private void SetState(BearState next)
        {
            if(state==next)return;state=next;Trace("State "+next);
            if(next==BearState.Taunt)Trigger(roarParameter);
            if(next==BearState.WindUp)Trigger(alertParameter);
            if(next==BearState.Attack)PlayStrike();
            if(next==BearState.Patrol)patrolAngle=Mathf.Atan2(transform.position.x,transform.position.z)*Mathf.Rad2Deg;
        }
        private void LateUpdate()
        {
            if(animator==null || Time.deltaTime<=0)return;
            float distance=Vector3.Distance(transform.position,previousPosition);
            float speed=visualPositionKnown && distance<chaseSpeed*Time.deltaTime*3 ? distance/Time.deltaTime : 0;
            previousPosition=transform.position;visualPositionKnown=true;
            if(state!=BearState.Patrol && state!=BearState.Chase)speed=0;
            animator.SetFloat(speedParameter,Mathf.Min(speed,chaseSpeed),.13f,Time.deltaTime);
        }
        [System.Diagnostics.Conditional("UNITY_EDITOR"),System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void Trace(string message) => Debug.Log("[CircusBear "+Time.time.ToString("F2")+"] "+message,this);

        private void Trigger(string parameter)
        {if(animator!=null && !string.IsNullOrEmpty(parameter))animator.SetTrigger(parameter);}
    }
}
