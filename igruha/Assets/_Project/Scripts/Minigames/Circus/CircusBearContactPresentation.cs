using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>The impact event can arrive ahead of the bear's interpolated
    /// NetworkTransform. Hold only the unanimated visual wrapper at the server's
    /// contact pose until that transform catches up; the network root is untouched.</summary>
    [DefaultExecutionOrder(170)]
    public sealed class CircusBearContactPresentation : MonoBehaviour
    {
        private const float MinimumHoldSeconds=.05f;
        private const float MaximumHoldSeconds=.35f;
        private const float ReleaseSeconds=.18f;
        private const float PositionTolerance=.025f;
        private const float AngleTolerance=.75f;
        private PitBear bear;
        private Transform visual;
        private Vector3 originalPosition, contactPosition;
        private Quaternion originalRotation, contactRotation;
        private float startedAt, releaseAt=-1;
        public bool IsActive { get; private set; }

        public void Initialize(PitBear owner,Transform wrapper)
        {
            ResetPresentation();
            bear=owner;
            visual=wrapper!=transform?wrapper:null;
            if(visual==null)return;
            originalPosition=visual.localPosition;
            originalRotation=visual.localRotation;
        }

        public void BeginContact(Vector3 serverPosition,float serverYaw)
        {
            if(visual==null)return;
            Quaternion rotation=Quaternion.Euler(0,serverYaw,0);
            contactPosition=serverPosition+rotation*Vector3.Scale(originalPosition,transform.lossyScale);
            contactRotation=rotation*originalRotation;
            startedAt=Time.time;releaseAt=-1;IsActive=true;
            ApplyPose();
        }

        private void LateUpdate() => ApplyPose();

        private void ApplyPose()
        {
            if(!IsActive || visual==null)return;
            Vector3 baselinePosition=transform.TransformPoint(originalPosition);
            Quaternion baselineRotation=transform.rotation*originalRotation;
            float age=Time.time-startedAt;
            bool caughtUp=Vector3.Distance(baselinePosition,contactPosition)<=PositionTolerance &&
                Quaternion.Angle(baselineRotation,contactRotation)<=AngleTolerance;
            if(age>=MinimumHoldSeconds && caughtUp)
            {
                ResetPresentation();
                return;
            }
            if(releaseAt<0 && (age>=MaximumHoldSeconds || bear!=null && bear.State!=PitBear.BearState.Attack))
                releaseAt=Time.time;
            float blend=releaseAt<0?0:Mathf.SmoothStep(0,1,(Time.time-releaseAt)/ReleaseSeconds);
            if(blend>=1)
            {
                ResetPresentation();
                return;
            }
            visual.SetPositionAndRotation(Vector3.Lerp(contactPosition,baselinePosition,blend),
                Quaternion.Slerp(contactRotation,baselineRotation,blend));
        }

        public void ResetPresentation()
        {
            if(IsActive && visual!=null)
            {
                visual.localPosition=originalPosition;
                visual.localRotation=originalRotation;
            }
            IsActive=false;releaseAt=-1;
        }

        private void OnDisable() => ResetPresentation();
    }
}
