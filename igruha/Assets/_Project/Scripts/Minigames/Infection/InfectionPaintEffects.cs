using UnityEngine;

namespace Igruha.Minigames.Infection
{
    /// <summary>Local paint particles and audio. Triggered only by the existing replicated paint state.</summary>
    public sealed class InfectionPaintEffects : MonoBehaviour
    {
        [SerializeField] private ParticleSystem splash;
        [SerializeField] private ParticleSystem droplets;
        [SerializeField] private AudioSource source;
        [SerializeField] private AudioClip splat;
        [SerializeField] private AudioClip zero;
        [SerializeField] private AudioClip wetStep;
        [SerializeField] private AudioSource zeroSource;
        [SerializeField] private float trailSpacing = .65f;
        private Transform target;
        private Vector3 previous;
        private bool painted;
        private Igruha.Core.Player.PlayerController player;
        private float nextStep;
        public void Bind(Transform avatar) { target=avatar; player=avatar.GetComponent<Igruha.Core.Player.PlayerController>(); transform.position=avatar.position; previous=avatar.position; }
        public void SetPainted(bool value)
        {
            painted=value;
            if(value){splash.Play();if(splat!=null)source.PlayOneShot(splat,.55f);}
            else {splash.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);droplets.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
        }
        public void SignalZero() { if(zero!=null)zeroSource.PlayOneShot(zero,.3f); }
        public void StopEffects() {painted=false;droplets.Stop(true,ParticleSystemStopBehavior.StopEmitting);}
        private void LateUpdate()
        {
            if(target==null){Destroy(gameObject);return;}
            transform.position=target.position;
            if(!painted){previous=target.position;return;}
            if((target.position-previous).sqrMagnitude<trailSpacing*trailSpacing)return;
            droplets.Emit(3);previous=target.position;
            if(wetStep!=null&&player!=null&&player.IsGrounded&&Time.time>=nextStep)
            { source.PlayOneShot(wetStep,.16f);nextStep=Time.time+.28f; }
        }
    }
}
