using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    public sealed class SumoCombatEffects : MonoBehaviour
    {
        private const int ArcPoints = 21;
        private SumoFighter fighter;
        private LineRenderer arc;
        private ParticleSystem flash;
        private GameObject effects;
        private static readonly Color GuardColor = new Color(.2f, .9f, .9f, .7f);
        private static readonly Color CounterColor = new Color(1, .78f, .22f, 1);
        public void Bind(SumoFighter owner, Material material)
        {
            fighter = owner;
            if (material == null) { enabled = false; return; }
            effects = new GameObject("SumoContactFX"); effects.transform.SetParent(transform, false);
            var arcObject = new GameObject("GuardArc"); arcObject.transform.SetParent(effects.transform, false);
            arc = arcObject.AddComponent<LineRenderer>(); arc.sharedMaterial = material; arc.positionCount = ArcPoints; arc.widthMultiplier = .055f; arc.useWorldSpace = true;
            arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; arc.receiveShadows = false;
            var burst = new GameObject("Contact"); burst.transform.SetParent(effects.transform, false); flash = burst.AddComponent<ParticleSystem>(); flash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = flash.main; main.playOnAwake = false; main.loop = false; main.startLifetime = .22f; main.startSpeed = new ParticleSystem.MinMaxCurve(.6f, 1.8f); main.startSize = new ParticleSystem.MinMaxCurve(.035f, .1f); main.maxParticles = 40; main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = .25f;
            var emission = flash.emission; emission.enabled = false;
            var shape = flash.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .06f;
            var size = flash.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
            flash.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        }
        public void Show(SumoCombatHit hit)
        {
            if (flash == null || hit.Contact == SumoContact.Miss) return;
            flash.transform.position = hit.Point;
            var main = flash.main; main.startColor = hit.Contact == SumoContact.Parry || hit.Contact == SumoContact.Counter ? CounterColor : hit.Contact == SumoContact.Block ? GuardColor : new Color(1, .89f, .66f);
            flash.Emit(hit.Contact == SumoContact.Parry ? 16 : 8);
        }
        private void LateUpdate()
        {
            if (fighter == null || arc == null) return;
            var s = fighter.State;
            bool parry = fighter.ContactAsTarget && fighter.LastContact == SumoContact.Parry && NetworkClock.Now - fighter.ContactAt < .22;
            arc.enabled = !fighter.Participant.Dead && (s.Phase == SumoCombatPhase.Guard || s.Phase == SumoCombatPhase.Charge || parry);
            if (!arc.enabled) return;
            bool charge = s.Phase == SumoCombatPhase.Charge;
            float angle = parry ? 360 : fighter.Config.GuardArc;
            float radius = fighter.Capsule.radius + .25f;
            Color color = parry || charge ? CounterColor : GuardColor; arc.startColor = arc.endColor = color;
            for (int i = 0; i < ArcPoints; i++)
            {
                float yaw = s.Yaw + Mathf.Lerp(-angle * .5f, angle * .5f, i / (float)(ArcPoints - 1));
                arc.SetPosition(i, fighter.Motor.Position + new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad) * radius, .025f, Mathf.Cos(yaw * Mathf.Deg2Rad) * radius));
            }
        }
        private void OnDestroy() { if (effects != null) Destroy(effects); }
    }
}
