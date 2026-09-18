using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.Infection
{
    /// <summary>Scene-owned audiovisual feedback; owns no round state and sends no network messages.</summary>
    public sealed class InfectionPresentation : MonoBehaviour
    {
        [SerializeField] private InfectionPaintEffects template;
        [SerializeField] private AudioSource heartbeat;
        [SerializeField] private AudioSource finish;
        [SerializeField] private float dangerRadius = 5;
        private readonly List<InfectionState> states=new List<InfectionState>(8);
        private readonly List<InfectionPaintEffects> effects=new List<InfectionPaintEffects>(8);
        private readonly List<InfectionPaintView> paints=new List<InfectionPaintView>(8);
        private InfectionState local;
        private bool activeRound;
        public void Bind(InfectionState state,InfectionPaintView paint)
        {
            var effect=Instantiate(template,transform);effect.gameObject.SetActive(true);effect.Bind(state.transform);
            paint.AttachEffects(effect);states.Add(state);effects.Add(effect);paints.Add(paint);
            if(state.TryGetComponent<PlayerInputReader>(out var reader)&&reader.LocallyControlled)local=state;
        }
        public void BeginRound(){activeRound=true;}
        public void EndRound()
        {
            activeRound=false;heartbeat.Stop();finish.Play();
            foreach(var effect in effects)if(effect!=null)effect.StopEffects();
        }
        private void OnDestroy(){foreach(var paint in paints)if(paint!=null)paint.SetInfected(false);}
        private void Update()
        {
            bool danger=false;
            if(activeRound&&local!=null&&local.Phase==InfectionPhase.Clean)
                for(int i=0;i<states.Count;i++)
                    if(states[i]!=null&&states[i]!=local&&states[i].Phase==InfectionPhase.Infected &&
                        (states[i].Position-local.Position).sqrMagnitude<dangerRadius*dangerRadius){danger=true;break;}
            if(danger&&!heartbeat.isPlaying)heartbeat.Play();
            else if(!danger&&heartbeat.isPlaying)heartbeat.Stop();
        }
    }
}
