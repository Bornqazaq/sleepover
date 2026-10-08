using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>Server-driven pursuit; clients reproduce the same telegraph before the impact.</summary>
    [DefaultExecutionOrder(190)] // Current player pose (150), then bear IK (180), then authoritative contact.
    public sealed class PitBear : MonoBehaviour
    {
        public const float ContactSeconds = .38f;
        public const float StrikeSeconds = 1.4f;
        public const float SwipeSeconds = .19f;
        private const float ContactStandOff = 2.6f;
        private const float MaximumLunge = 2.5f;
        private const float MaximumBackstep = 2.0f;
        private const float ContactReach = 2.85f;
        private const float ContactWindow = .09f;
        // Fur, muzzle and lifted paws must fit even while the body turns.
        // The masonry's inner face is slightly inside the logical pit radius.
        public const float BodyWallClearance = 2.5f;
        private const float WallBrakingDistance = .65f;
        private const float TurnStrideRadius = .55f;
        private const float StrikeAlignment = .9999f;
        private const float ContactTolerance = .06f;
        private const float PursuitLeadSeconds = .45f;
        private const float RepositionInwardBias = .7f;
        private const float StrikePreparationDistance = 3.15f;
        private const float StrikePreparationReleaseDistance = 3.8f;
        private static readonly int StrikeState = Animator.StringToHash("Strike");
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
        [SerializeField] private float wallMargin = BodyWallClearance;
        [SerializeField] private float acceleration = 6f;
        [Header("Attack — matches Bruno_Strike, 1.4 seconds")]
        [SerializeField] private float attackContactTime = ContactSeconds;
        [SerializeField] private float attackDuration = StrikeSeconds;
        [SerializeField] private float attackRecovery = .3f;

        public event Action<PlayerController, Vector3> Caught;
        public event Action<Vector3> ImpactShown;
        public BearState State => state;
        public PlayerController Target { get; private set; }
        public PlayerController AttackVictim => attackVictim;
        public int PresentationTargetId { get; set; } = -1;
        public Transform VisualRoot => visualRoot;
        public Transform PresentationRoot => visualRoot != null ? visualRoot : transform;
        public float AnimatorSpeed => currentSpeed;
        public float AttackAge => Mathf.Max(0, Time.time - visualAttackStartedAt);
        public PlayerController PresentationVictim => attackVictim != null ? attackVictim : Target != null ? Target : presentationVictim;
        public Vector3 ContactPoint { get; private set; }
        public bool HasContact { get; private set; }

        private sealed class Runner
        {
            internal PlayerController Player;
            internal Animator Animator;
            internal bool Ready;
            internal bool HeadStartGranted;
            internal Vector3 PreviousPosition, Velocity;
            internal float SampleSeconds;
        }
        private readonly List<Runner> runners = new List<Runner>(8);
        private static readonly int FlyBack = Animator.StringToHash("FlyBack");
        private static readonly int FallForward = Animator.StringToHash("FallForward");
        private float chaseSpeed = 5.5f, patrolSpeed = 2.1f, strikeRadius = 2.35f;
        private float windUpDuration = 3f, knockbackSpeed = 8f, pitRadius = 8.64f;
        private float windUpLeft, patrolAngle, currentSpeed, attackElapsed, recoveryLeft;
        private float patrolPauseIn = 9f, patrolPauseLeft;
        private bool hitEvaluated;
        private bool contactPending;
        private int contactPendingFrame;
        private uint contactVersionBeforeTick;
        private PlayerController attackVictim;
        private Vector3 attackDirection, previousPosition;
        private float attackTravel;
        private float plannedStandOff;
        private bool visualPositionKnown;
        private bool preparingStrike;
        private float previousVisualYaw;
        private float MovementRadius => Mathf.Max(.5f, pitRadius - Mathf.Max(wallMargin, BodyWallClearance));
        private BearState state = BearState.Patrol;
        private float visualAttackStartedAt;
        private CircusBearFeedback feedback;
        private CircusBearMotion motion;
        private CircusBearContactPresentation contactPresentation;
        private PlayerController presentationVictim;

        private void Awake()
        {
            feedback = GetComponent<CircusBearFeedback>();
            motion = GetComponentInChildren<CircusBearMotion>(true);
            contactPresentation = GetComponent<CircusBearContactPresentation>();
            if(contactPresentation==null)contactPresentation=gameObject.AddComponent<CircusBearContactPresentation>();
            contactPresentation.Initialize(this,visualRoot);
        }

        public void Configure(float chase, float patrol, float strike, float windUp, float knockback, float pit)
        {
            chaseSpeed=chase; patrolSpeed=patrol; strikeRadius=strike; windUpDuration=windUp;
            knockbackSpeed=knockback; pitRadius=pit; runners.Clear(); Target=null;
            currentSpeed=0; windUpLeft=0; attackVictim=null; state=BearState.Patrol;preparingStrike=false;
            patrolPauseIn=9; patrolPauseLeft=0; visualPositionKnown=false;
            presentationVictim=null;HasContact=false;contactPending=false;
            contactPresentation?.ResetPresentation();
        }

        public void SetPresentationTarget(PlayerController player) => presentationVictim=player;

        /// <summary>Called when the hatch opens, while the player is still above the pit.</summary>
        public void RegisterFallen(PlayerController player)
        {
            if(player==null)return;
            ForgetRunner(player);
            runners.Add(new Runner { Player=player, Animator=player.GetComponentInChildren<Animator>(), PreviousPosition=player.Position });
        }

        public void ForgetRunner(PlayerController player)
        {
            for(int i=runners.Count-1;i>=0;i--)
                if(runners[i].Player==null || runners[i].Player==player)runners.RemoveAt(i);
        }

        /// <summary>
        /// Pursuit starts as soon as the landed player regains movement.
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
                    recovering = player.IsKnockedDown || player.MovementLocked;
                }
                else
                {
                    RaycastHit hit;
                    onFloor &= Physics.Raycast(player.transform.position+Vector3.up*.25f,Vector3.down,out hit,.6f,
                        Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) && hit.point.y<=transform.position.y+.3f;
                }
                runner.Ready=onFloor && !recovering;
                if(runner.Ready)Trace("Ready "+player.name+"; movement available; chase begins");
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
        private static bool IsRecovery(int hash)=>hash==FlyBack || hash==FallForward;

        /// <summary>Only the authoritative minigame calls Tick; contact resolves after this frame's IK.</summary>
        public void Tick(float deltaTime,PlayerController nearest,bool someoneOnLowestCage)
        {
            if(deltaTime<=0)return;
            foreach(var runner in runners)
            {
                if(runner.Player==null)continue;
                runner.SampleSeconds+=deltaTime;
                if(runner.SampleSeconds<.1f)continue;
                Vector3 position=runner.Player.Position;
                Vector3 velocity=(position-runner.PreviousPosition)/runner.SampleSeconds;velocity.y=0;
                runner.PreviousPosition=position;
                // Remote bodies are kinematic: derive motion from replicated positions.
                // Clamp teleports and smooth packet steps before choosing an intercept.
                runner.Velocity=Vector3.Lerp(runner.Velocity,Vector3.ClampMagnitude(velocity,8),.7f);
                runner.SampleSeconds=0;
            }
            if(state==BearState.Attack){TickAttack(deltaTime);return;}
            if(state==BearState.Recovery)
            {
                currentSpeed=0;recoveryLeft-=deltaTime;
                if(recoveryLeft>0)return;
                SetState(BearState.Chase);
            }
            if(nearest==null)
            {
                Target=null;preparingStrike=false;
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
                Target=nearest;preparingStrike=false;
                Runner entrant=runners.Find(r=>r.Player==nearest);
                windUpLeft=entrant!=null && entrant.HeadStartGranted ? 0 : windUpDuration;
                if(entrant!=null)entrant.HeadStartGranted=true;
                currentSpeed=0;
                if(windUpLeft>0)SetState(BearState.WindUp);
                // This frame's delta belongs to the preceding state. Starting a
                // target on a long frame must still grant the full head start.
                if(windUpLeft>0)
                {
                    FaceTowards(nearest.transform.position-transform.position,deltaTime);
                    return;
                }
            }
            Vector3 delta=nearest.transform.position-transform.position;delta.y=0;
            var pursued=runners.Find(r=>r.Player==nearest);
            Vector3 targetVelocity=pursued!=null?pursued.Velocity:Vector3.zero;
            Vector3 intercept=delta+targetVelocity*PursuitLeadSeconds;
            if(Vector3.Dot(intercept,delta)<=0)intercept=delta;
            if(windUpLeft<=0 && TryMakeStrikeRoom(nearest,delta,targetVelocity,deltaTime))return;
            if(windUpLeft>0)
            {
                FaceTowards(intercept,deltaTime);
                windUpLeft-=deltaTime;currentSpeed=0;
                if(windUpLeft<=0)SetState(BearState.Chase);
                return;
            }
            SetState(BearState.Chase);
            // Keep the turn once preparation has begun. A single distance
            // threshold alternated pursuit/aim every frame as a runner moved
            // away during the turn, producing a jittering, endless orbit.
            preparingStrike=delta.magnitude<=(preparingStrike?StrikePreparationReleaseDistance:Mathf.Max(strikeRadius,StrikePreparationDistance));
            bool inStrikeRange=preparingStrike;
            if(inStrikeRange)FaceTowards(intercept,deltaTime);
            if(inStrikeRange && Vector3.Dot(transform.forward,intercept.normalized)>StrikeAlignment)
            {
                attackVictim=nearest;presentationVictim=nearest;attackDirection=transform.forward;attackElapsed=0;hitEvaluated=false;HasContact=false;contactPending=false;
                Vector3 expected=delta+(pursued!=null?pursued.Velocity:Vector3.zero)*attackContactTime;
                float standOff=motion!=null?motion.ApproachDistance(nearest):ContactStandOff;
                plannedStandOff=standOff;
                attackTravel=Mathf.Clamp(Vector3.Dot(expected,attackDirection)-standOff,-MaximumBackstep,MaximumLunge);
                currentSpeed=0;preparingStrike=false;SetState(BearState.Attack);return;
            }
            // Once there is room for a swipe, turn on planted feet before
            // committing. Accelerating toward a close runner while still facing
            // sideways makes a tight orbit that never reaches attack alignment.
            if(inStrikeRange)
            {
                currentSpeed=0;
                return;
            }
            // Chase the reachable inside lane. Steering at a runner outside our
            // body clearance continuously brakes into the rim instead of
            // gaining on them along the shorter inner circumference.
            float approach=motion!=null?motion.ApproachDistance(nearest):ContactStandOff;
            Vector3 route=InsideApproachDirection(transform.position+intercept,approach);
            FaceTowards(route,deltaTime);
            Steer(route,chaseSpeed,deltaTime);
        }

        private Vector3 InsideApproachDirection(Vector3 target,float standOff)
        {
            Vector3 radial=new Vector3(target.x,0,target.z);
            if(radial.magnitude>MovementRadius)target-=radial.normalized*standOff;
            Vector3 direction=target-transform.position;direction.y=0;
            return direction;
        }

        private bool TryMakeStrikeRoom(PlayerController victim,Vector3 delta,Vector3 targetVelocity,float dt)
        {
            if(motion==null || delta.sqrMagnitude>ContactReach*ContactReach || delta.sqrMagnitude<.0001f)return false;
            float standOff=motion.ApproachDistance(victim);
            Vector3 away=-delta.normalized;
            Vector3 retreat=victim.Position+away*standOff;
            float limit=MovementRadius;
            if(new Vector2(retreat.x,retreat.z).sqrMagnitude<=(limit-.03f)*(limit-.03f))return false;
            preparingStrike=false;
            // Make room before committing to a strike. A runner at the rim
            // needs a moving interception point, while a close stationary
            // runner can be circled with a slow inward step.
            Vector3 side=Vector3.Cross(Vector3.up,delta.normalized);
            Vector3 radial=new Vector3(transform.position.x,0,transform.position.z);
            if(Vector3.Dot(side,radial)>0)side=-side;
            // At the edge, walk inward around the runner rather than pushing
            // tangentially against a radial clamp every frame.
            side=(side-radial.normalized*RepositionInwardBias).normalized;
            float repositionSpeed=patrolSpeed;
            if(new Vector2(victim.Position.x,victim.Position.z).magnitude>MovementRadius)
            {
                side=InsideApproachDirection(victim.Position+targetVelocity*PursuitLeadSeconds,standOff);
                repositionSpeed=Mathf.Clamp(targetVelocity.magnitude,patrolSpeed,chaseSpeed);
            }
            SetState(BearState.Chase);
            currentSpeed=Mathf.Min(currentSpeed,repositionSpeed);
            FaceTowards(side,dt);
            Steer(side,repositionSpeed,dt);
            return true;
        }

        private void TickAttack(float dt)
        {
            float previous=attackElapsed;attackElapsed+=dt;
            // The final running step brakes into the swipe. A runner already
            // under the chest makes this a backward step, using the same authored
            // foot lifts; the committed direction never flips during the attack.
            float before=LungeProgress(previous/attackContactTime);
            float after=LungeProgress(attackElapsed/attackContactTime);
            MoveBy(attackDirection*((after-before)*attackTravel));currentSpeed=0;
            // Never use the preceding frame's paw distance here: the target and
            // bear have both moved since that sample. An entire skipped contact
            // window is a miss, rather than retrospective damage in a released pose.
            contactPending=!hitEvaluated && attackElapsed>=attackContactTime && attackElapsed<=attackContactTime+ContactWindow;
            contactPendingFrame=Time.frameCount;
            contactVersionBeforeTick=motion!=null?motion.ContactVersion:0;
            if(attackElapsed>attackContactTime+ContactWindow)
            {
                if(!hitEvaluated)Trace("Miss root="+transform.position.ToString("F3")+" yaw="+transform.eulerAngles.y.ToString("F2")+
                    " standOff="+plannedStandOff.ToString("F3")+" lunge="+attackTravel.ToString("F3")+
                    " wallRadius="+(pitRadius-wallMargin).ToString("F3")+" radial="+new Vector2(transform.position.x,transform.position.z).magnitude.ToString("F3")+
                    " "+(motion!=null?motion.DescribeContact():"no motion"));
                hitEvaluated=true;
            }
            if(attackElapsed>=attackDuration)
            {
                attackVictim=null;recoveryLeft=attackRecovery;
                SetState(BearState.Recovery);
            }
        }

        private void ResolveAttackContact()
        {
            if(!contactPending)return;
            contactPending=false;
            if(hitEvaluated || state!=BearState.Attack || attackVictim==null || contactPendingFrame!=Time.frameCount)return;
            if(motion==null || motion.ContactFrame!=Time.frameCount || motion.ContactVersion==contactVersionBeforeTick ||
                motion.ContactVictim!=attackVictim || motion.ContactDistance>ContactTolerance)return;
            Vector3 delta=attackVictim.transform.position-transform.position;
            float vertical=Mathf.Abs(delta.y);delta.y=0;
            if(vertical>=.95f || delta.magnitude>ContactReach || Vector3.Dot(attackDirection,delta.normalized)<=.82f)return;
            // A paw can touch a runner standing inside the bear. That is not a
            // valid hit pose, including when the pit wall blocked the backstep.
            if(!motion.HasBodyClearance(attackVictim))return;
            hitEvaluated=true;
            Vector3 impulse=(delta.sqrMagnitude>.001f?delta.normalized:attackDirection)+Vector3.up*.35f;
            var victim=attackVictim;ForgetRunner(victim);Target=null;
            Trace("Contact "+victim.name+" at "+attackElapsed.ToString("F2")+" s");
            Caught?.Invoke(victim,impulse.normalized*knockbackSpeed);
        }

        private static float LungeProgress(float t)
        {
            t=Mathf.Clamp01(t);
            return t+t*t-t*t*t;
        }

        public void ApplyNetworkState(BearState next,float animatorSpeed,float elapsed=0)
        {SetState(next,elapsed);currentSpeed=animatorSpeed;}

        public void PlayStrike()
        { PlayStrike(0); }

        private void PlayStrike(float elapsed)
        {
            HasContact=false;
            visualAttackStartedAt=Time.time-elapsed;
            if(animator!=null)
            {
                animator.ResetTrigger(roarParameter);animator.ResetTrigger(alertParameter);animator.ResetTrigger(strikeParameter);
                animator.CrossFadeInFixedTime(StrikeState,.065f,0,Mathf.Min(elapsed,attackDuration));
            }
            feedback?.Attack(elapsed);
        }

        /// <summary>One impact cue at the same instant as the player's fall.
        /// A contact RPC can precede a batched phase update: never show damage in an idle pose.</summary>
        public void ShowImpact(Vector3 point,bool synchronizePose)
        { ShowImpact(point,synchronizePose,transform.position,transform.eulerAngles.y); }

        public void ShowImpact(Vector3 point,bool synchronizePose,Vector3 serverBearPosition,float serverBearYaw)
        {
            ContactPoint=point;HasContact=true;
            if(synchronizePose)
            {
                state=BearState.Attack;
                visualAttackStartedAt=Time.time-attackContactTime;
                contactPresentation?.BeginContact(serverBearPosition,serverBearYaw);
                if(animator!=null)
                {
                    animator.Play(StrikeState,0,attackContactTime/attackDuration);
                    animator.Update(0);
                }
                motion?.ResetPresentationHistory();
            }
            feedback?.Impact(point);
            ImpactShown?.Invoke(point);
        }

        private void Patrol(float dt)
        {
            if(patrolPauseLeft>0){patrolPauseLeft-=dt;currentSpeed=0;return;}
            patrolPauseIn-=dt;
            if(patrolPauseIn<=0){patrolPauseLeft=1.2f;patrolPauseIn=9f;currentSpeed=0;return;}
            float radius=Mathf.Max(1,MovementRadius-.6f);
            patrolAngle+=patrolSpeed/radius*dt*Mathf.Rad2Deg;
            float variedRadius=radius-.3f+.3f*Mathf.Sin(patrolAngle*Mathf.Deg2Rad*1.7f);
            Vector3 target=Quaternion.Euler(0,patrolAngle+18,0)*Vector3.forward*variedRadius;
            Vector3 delta=target-transform.position;delta.y=0;FaceTowards(delta,dt);Steer(delta,patrolSpeed,dt);
        }
        private void Steer(Vector3 direction,float speed,float dt)
        {
            float alignment=direction.sqrMagnitude>.001f?Vector3.Dot(transform.forward,direction.normalized):0;
            float wanted=speed*Mathf.InverseLerp(.15f,.9f,alignment);
            Vector3 radial=new Vector3(transform.position.x,0,transform.position.z);
            float outward=Mathf.Max(0,Vector3.Dot(transform.forward,radial.normalized));
            float edgeSpeed=Mathf.Clamp01((MovementRadius-radial.magnitude)/WallBrakingDistance);
            wanted*=Mathf.Lerp(1,edgeSpeed,outward);
            currentSpeed=Mathf.MoveTowards(currentSpeed,wanted,acceleration*dt);
            MoveBy(transform.forward*(currentSpeed*dt));
        }
        private void MoveBy(Vector3 delta)
        {
            Vector3 next=transform.position+delta;Vector2 flat=new Vector2(next.x,next.z);
            flat=Vector2.ClampMagnitude(flat,MovementRadius);next.x=flat.x;next.z=flat.y;transform.position=next;
        }
        private void FaceTowards(Vector3 direction,float dt)
        {
            direction.y=0;if(direction.sqrMagnitude<.0001f)return;
            transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(direction),turnSpeed*dt);
        }
        private void SetState(BearState next,float elapsed=0)
        {
            if(state==next)return;state=next;Trace("State "+next);
            if(next==BearState.Taunt)Trigger(roarParameter);
            if(next==BearState.WindUp)Trigger(alertParameter);
            if(next==BearState.Attack)PlayStrike(elapsed);
            if(next==BearState.Patrol)patrolAngle=Mathf.Atan2(transform.position.x,transform.position.z)*Mathf.Rad2Deg;
        }
        private void LateUpdate()
        {
            ResolveAttackContact();
        }

        private void Update()
        {
            if(animator==null || Time.deltaTime<=0)return;
            Transform root=PresentationRoot;
            float distance=Vector3.Distance(root.position,previousPosition);
            float yaw=root.eulerAngles.y;
            bool continuous=visualPositionKnown && distance<2f;
            float speed=continuous ? distance/Time.deltaTime : 0;
            float turnSpeedForFeet=continuous ? Mathf.Abs(Mathf.DeltaAngle(previousVisualYaw,yaw))*Mathf.Deg2Rad/Time.deltaTime*TurnStrideRadius : 0;
            previousPosition=root.position;previousVisualYaw=yaw;visualPositionKnown=true;
            if(state==BearState.Attack || state==BearState.Recovery || state==BearState.Taunt)speed=0;
            else speed=Mathf.Max(speed,Mathf.Min(turnSpeedForFeet,patrolSpeed));
            animator.SetFloat(speedParameter,Mathf.Min(speed,chaseSpeed),.13f,Time.deltaTime);
        }
        [System.Diagnostics.Conditional("UNITY_EDITOR"),System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void Trace(string message) => Debug.Log("[CircusBear "+Time.time.ToString("F2")+"] "+message,this);

        private void Trigger(string parameter)
        {if(animator!=null && !string.IsNullOrEmpty(parameter))animator.SetTrigger(parameter);}
    }
}
