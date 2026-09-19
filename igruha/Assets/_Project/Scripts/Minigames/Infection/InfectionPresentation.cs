using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Player;

namespace Igruha.Minigames.Infection
{
    /// <summary>Scene-owned audiovisual feedback; owns no round state and sends no network messages.</summary>
    /// <remarks>
    /// <b>Шаг заражённого — не украшение, а правило игры.</b> Спека даёт чистому
    /// право услышать погоню со спины, и это единственный сигнал, который в
    /// «Заражении» работает за спиной: краску видно только в лицо. Поэтому шаг
    /// подменяется у всех заражённых сразу, а не звучит у одного бегущего.
    ///
    /// Подмена идёт через общий слой (<see cref="CharacterFootsteps"/>), а не
    /// своим источником: так сохраняются шесть вариаций, разброс высоты,
    /// затухание по дистанции и тишина приседа — всё, что общий слой уже умеет
    /// и что пришлось бы повторить здесь.
    ///
    /// Своих RPC нет: фаза игрока реплицирована, и каждая машина приходит к
    /// подмене сама.
    /// </remarks>
    public sealed class InfectionPresentation : MonoBehaviour
    {
        /// <summary>Слот шага заражённого. Имя — из манифеста docs/art/infection-sfx.json.</summary>
        private const string InfectedStepSlot = "SFX_INFC_Step_Infected";

        [SerializeField] private InfectionPaintEffects template;
        [SerializeField] private AudioSource heartbeat;
        [SerializeField] private AudioSource finish;
        [SerializeField] private float dangerRadius = 5;

        [Tooltip("Библиотека игры. Подкладывается персонажам ради шага заражённого")]
        [SerializeField] private MinigameSfxLibrary library;

        private readonly List<InfectionState> states=new List<InfectionState>(8);
        private readonly List<InfectionPaintEffects> effects=new List<InfectionPaintEffects>(8);
        private readonly List<InfectionPaintView> paints=new List<InfectionPaintView>(8);

        /// <summary>Шаги участников и то, каким они звучали в прошлый раз. Индексы совпадают со <see cref="states"/>.</summary>
        private readonly List<CharacterFootsteps> steps=new List<CharacterFootsteps>(8);
        private readonly List<bool> stepsInfected=new List<bool>(8);

        private InfectionState local;
        private bool activeRound;
        public void Bind(InfectionState state,InfectionPaintView paint)
        {
            var effect=Instantiate(template,transform);effect.gameObject.SetActive(true);effect.Bind(state.transform);
            paint.AttachEffects(effect);states.Add(state);effects.Add(effect);paints.Add(paint);
            if(state.TryGetComponent<PlayerInputReader>(out var reader)&&reader.LocallyControlled)local=state;
            BindSteps(state);
        }

        /// <summary>
        /// Подготовить шаг участника: подложить его проигрывателю библиотеку игры.
        ///
        /// Проигрыватель на префабе персонажа один на все пятнадцать игр и знает
        /// только общий слой; шаг заражённого — слот этой игры, и без библиотеки
        /// подмена нашла бы пустое место.
        /// </summary>
        private void BindSteps(InfectionState state)
        {
            CharacterFootsteps walk=null;
            if(state!=null&&state.TryGetComponent(out CharacterFootsteps found))
            {
                walk=found;
                if(library!=null&&state.TryGetComponent(out MinigameAudioPlayer voice))voice.AddLibrary(library);
            }

            steps.Add(walk);stepsInfected.Add(false);
        }

        /// <summary>
        /// Свести звук шага с фазой игрока. Заражённый шлёпает, чистый ступает
        /// как обычно; отмытый в конце раунда возвращается к обычному шагу сам.
        /// </summary>
        private void TrackSteps()
        {
            for(int i=0;i<steps.Count&&i<states.Count;i++)
            {
                CharacterFootsteps walk=steps[i];
                if(walk==null)continue;

                bool infected=activeRound&&states[i]!=null&&states[i].Phase!=InfectionPhase.Clean;
                if(infected==stepsInfected[i])continue;

                stepsInfected[i]=infected;
                walk.SetSlotOverride(infected?InfectedStepSlot:string.Empty);
            }
        }

        /// <summary>Вернуть всем обычный шаг: раунд кончился или сцену выгружают.</summary>
        private void ReleaseSteps()
        {
            for(int i=0;i<steps.Count;i++)
            {
                if(steps[i]!=null)steps[i].SetSlotOverride(string.Empty);
                if(i<stepsInfected.Count)stepsInfected[i]=false;
            }
        }
        public void BeginRound(){activeRound=true;}
        public void EndRound()
        {
            activeRound=false;heartbeat.Stop();finish.Play();
            foreach(var effect in effects)if(effect!=null)effect.StopEffects();
            ReleaseSteps();
        }
        private void OnDestroy()
        {
            foreach(var paint in paints)if(paint!=null)paint.SetInfected(false);
            ReleaseSteps();
        }
        private void Update()
        {
            TrackSteps();

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
