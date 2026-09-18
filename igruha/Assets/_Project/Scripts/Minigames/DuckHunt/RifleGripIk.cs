using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>Role-only two-bone arm fitting after Animator evaluation.
    /// Targets are palm contacts on the prop, not humanoid IK effector axes.
    /// Original clips, rigs and player prefabs are not modified.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class RifleGripIk : MonoBehaviour
    {
        private Animator animator;
        private Arm right, left;
        private AnimatorCullingMode previousCulling;
        private bool ownsCulling;
        public Transform GripPoint { get; set; }
        public Transform ForePoint { get; set; }
        public float Weight { get; set; }

        private sealed class Arm
        {
            public Transform Upper, Lower, Hand;
            public Quaternion InversePalmBasis;
            public Vector3 PalmOffset;
        }

        private void Awake()
        {
            animator=GetComponent<Animator>();
            if(animator==null || !animator.isHuman)return;
            right=Measure(HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,
                HumanBodyBones.RightIndexProximal,HumanBodyBones.RightThumbProximal);
            left=Measure(HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,
                HumanBodyBones.LeftIndexProximal,HumanBodyBones.LeftThumbProximal);
        }

        private Arm Measure(HumanBodyBones upper,HumanBodyBones lower,HumanBodyBones hand,HumanBodyBones index,HumanBodyBones thumb)
        {
            var arm=new Arm{Upper=animator.GetBoneTransform(upper),Lower=animator.GetBoneTransform(lower),
                Hand=animator.GetBoneTransform(hand),InversePalmBasis=Quaternion.identity};
            var finger=animator.GetBoneTransform(index);var thumbBone=animator.GetBoneTransform(thumb);
            if(arm.Hand==null || finger==null || thumbBone==null)return arm;
            Vector3 fingers=Quaternion.Inverse(arm.Hand.rotation)*(finger.position-arm.Hand.position);
            Vector3 towardsThumb=Quaternion.Inverse(arm.Hand.rotation)*(thumbBone.position-arm.Hand.position);
            arm.InversePalmBasis=Quaternion.Inverse(Quaternion.LookRotation(fingers.normalized,Vector3.ProjectOnPlane(towardsThumb,fingers).normalized));
            arm.PalmOffset=fingers*.65f+towardsThumb*.15f;
            return arm;
        }

        private void LateUpdate()
        {
            // Non-serialized arm references are lost on a Play Mode domain reload.
            if(animator==null || right==null || left==null)Awake();
            if(Weight<=0 || GripPoint==null || ForePoint==null || animator==null || !animator.isActiveAndEnabled)
            { RestoreCulling();return; }
            // Animator's offscreen transform culling otherwise leaves skinning in the old pose.
            // Only while this role is equipped; original prefab/rig settings stay untouched.
            if(!ownsCulling){previousCulling=animator.cullingMode;ownsCulling=true;}
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            if(GripPoint.parent==ForePoint.parent)
                for(int i=0;i<4;i++)
                {
                    FitWeaponReach(left,ForePoint,new Vector3(.86f,.50f,0));
                    FitWeaponReach(right,GripPoint,new Vector3(-.35f,.86f,.36f));
                }
            Fit(right,GripPoint,new Vector3(-.35f,.86f,.36f),Vector3.forward,new Vector3(.30f,-.24f,-.10f));
            Fit(left,ForePoint,new Vector3(.86f,.50f,0),Vector3.forward,new Vector3(-.30f,-.24f,-.14f));
        }

        private void FitWeaponReach(Arm arm,Transform contact,Vector3 fingers)
        {
            if(arm==null || arm.Upper==null || arm.Lower==null || arm.Hand==null)return;
            Quaternion rotation=contact.rotation*Quaternion.LookRotation(fingers,Vector3.forward)*arm.InversePalmBasis;
            Vector3 wrist=contact.position-rotation*arm.PalmOffset;
            Vector3 delta=wrist-arm.Upper.position;
            float reach=(Vector3.Distance(arm.Upper.position,arm.Lower.position)+Vector3.Distance(arm.Lower.position,arm.Hand.position))*.98f;
            if(delta.magnitude>reach)
                contact.parent.position-=delta.normalized*(delta.magnitude-reach)*Mathf.Clamp01(Weight);
        }

        private void RestoreCulling()
        {
            if(ownsCulling && animator!=null)animator.cullingMode=previousCulling;
            ownsCulling=false;
        }

        private void OnDisable()=>RestoreCulling();

        private void Fit(Arm arm,Transform contact,Vector3 fingers,Vector3 thumb,Vector3 elbowHint)
        {
            if(arm==null || arm.Upper==null || arm.Lower==null || arm.Hand==null)return;
            float weight=Mathf.Clamp01(Weight);
            Quaternion handRotation=contact.rotation*Quaternion.LookRotation(fingers,thumb)*arm.InversePalmBasis;
            Vector3 wrist=contact.position-handRotation*arm.PalmOffset;
            Vector3 shoulder=arm.Upper.position;
            float upperLength=Vector3.Distance(shoulder,arm.Lower.position);
            float lowerLength=Vector3.Distance(arm.Lower.position,arm.Hand.position);
            Vector3 toWrist=wrist-shoulder;
            float distance=Mathf.Clamp(toWrist.magnitude,Mathf.Abs(upperLength-lowerLength)+.001f,upperLength+lowerLength-.001f);
            Vector3 direction=toWrist.normalized;
            Vector3 bend=Vector3.ProjectOnPlane(contact.position+contact.rotation*elbowHint-shoulder,direction).normalized;
            if(bend.sqrMagnitude<.001f)bend=Vector3.ProjectOnPlane(transform.right,direction).normalized;
            float along=(upperLength*upperLength-lowerLength*lowerLength+distance*distance)/(2*distance);
            float height=Mathf.Sqrt(Mathf.Max(0,upperLength*upperLength-along*along));
            Vector3 elbow=shoulder+direction*along+bend*height;
            Quaternion upperRotation=Quaternion.FromToRotation(arm.Lower.position-shoulder,elbow-shoulder)*arm.Upper.rotation;
            arm.Upper.rotation=Quaternion.Slerp(arm.Upper.rotation,upperRotation,weight);
            Quaternion lowerRotation=Quaternion.FromToRotation(arm.Hand.position-arm.Lower.position,shoulder+direction*distance-arm.Lower.position)*arm.Lower.rotation;
            arm.Lower.rotation=Quaternion.Slerp(arm.Lower.rotation,lowerRotation,weight);
            arm.Hand.rotation=Quaternion.Slerp(arm.Hand.rotation,handRotation,weight);
        }
    }
}
