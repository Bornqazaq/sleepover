using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    public sealed class SumoCombatEffects : MonoBehaviour
    {
        private const int ArcPoints = 21;
        private const int PulsePoints = 25;
        private const float PulseSeconds = .26f;
        private SumoFighter fighter;
        private LineRenderer arc, pulse;
        private ParticleSystem flash;
        private GameObject effects;
        private Material strokeMaterial;
        private Camera view;
        private float pulseAt = float.NegativeInfinity;
        private Vector3 pulsePoint, pulseSide;
        private SumoContact pulseKind;
        private static readonly Color GuardColor = new Color(.2f, .9f, .9f, .7f);
        private static readonly Color CounterColor = new Color(1, .78f, .22f, 1);
        private static readonly Color BreakColor = new Color(1, .34f, .13f, 1);
        private static readonly Color HitColor = new Color(1, .89f, .66f, 1);
        public void Bind(SumoFighter owner, Material material)
        {
            fighter = owner;
            if (material == null) { enabled = false; return; }
            // The dust texture fades along the whole line; a solid stroke remains readable on sand.
            strokeMaterial = new Material(material) { mainTexture = Texture2D.whiteTexture };
            view = Camera.main;
            effects = new GameObject("SumoContactFX"); effects.transform.SetParent(transform, false);
            var arcObject = new GameObject("GuardArc"); arcObject.transform.SetParent(effects.transform, false);
            arc = arcObject.AddComponent<LineRenderer>(); arc.sharedMaterial = strokeMaterial; arc.positionCount = ArcPoints; arc.widthMultiplier = .055f; arc.useWorldSpace = true;
            arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; arc.receiveShadows = false;
            var pulseObject = new GameObject("ContactStroke"); pulseObject.transform.SetParent(effects.transform, false);
            pulse = pulseObject.AddComponent<LineRenderer>(); pulse.sharedMaterial = strokeMaterial; pulse.positionCount = PulsePoints;
            pulse.useWorldSpace = true; pulse.enabled = false; pulse.numCornerVertices = 2;
            pulse.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; pulse.receiveShadows = false;
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
            bool strong = hit.Attack != SumoAttack.Quick && hit.Contact != SumoContact.Block && hit.Contact != SumoContact.Parry;
            var main = flash.main; main.startColor = ContactColor(hit.Contact);
            main.startSize = new ParticleSystem.MinMaxCurve(strong ? .065f : .035f, strong ? .14f : .085f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(strong ? 1.1f : .5f, strong ? 2.7f : 1.5f);
            flash.Emit(strong ? 20 : hit.Contact == SumoContact.Parry ? 16 : 8);
            if (hit.Contact != SumoContact.Parry && hit.Contact != SumoContact.GuardBreak && hit.Contact != SumoContact.Block) return;
            pulseAt = Time.time; pulsePoint = hit.Point; pulseKind = hit.Contact;
            pulseSide = Vector3.Cross(Vector3.up, hit.Direction).normalized;
            if (pulseSide.sqrMagnitude < .01f) pulseSide = transform.right;
            UpdatePulse();
        }
        private static Color ContactColor(SumoContact contact) => contact == SumoContact.Parry || contact == SumoContact.Counter ? CounterColor
            : contact == SumoContact.GuardBreak ? BreakColor : contact == SumoContact.Block ? GuardColor : HitColor;
        private void UpdatePulse()
        {
            float age = (Time.time - pulseAt) / PulseSeconds;
            pulse.enabled = age >= 0 && age < 1;
            if (!pulse.enabled) return;
            Color color = ContactColor(pulseKind); color.a *= 1 - age;
            pulse.startColor = pulse.endColor = color; pulse.widthMultiplier = .065f * (1 - .65f * age);
            float radius = Mathf.Lerp(.2f, pulseKind == SumoContact.GuardBreak ? .58f : .48f, age);
            Vector3 right = view != null ? view.transform.right : pulseSide;
            Vector3 up = view != null ? view.transform.up : Vector3.up;
            Vector3 centre = pulsePoint - (view != null ? view.transform.forward * .12f : Vector3.zero);
            for (int i = 0; i < PulsePoints; i++)
            {
                float t = i / (float)(PulsePoints - 1);
                // Parry: closed gold ring. Block: short cyan arc. Break: split-looking orange zigzag.
                float angle = Mathf.Lerp(pulseKind == SumoContact.Block ? -.7f : 0, pulseKind == SumoContact.Block ? .7f : Mathf.PI * 2, t);
                float x = pulseKind == SumoContact.GuardBreak ? Mathf.Lerp(-1, 1, t) : Mathf.Sin(angle);
                float y = pulseKind == SumoContact.GuardBreak ? Mathf.Sin(t * Mathf.PI * 6) * .42f : Mathf.Cos(angle);
                pulse.SetPosition(i, centre + (right * x + up * y) * radius);
            }
        }
        private void LateUpdate()
        {
            if (fighter == null || arc == null) return;
            UpdatePulse();
            var s = fighter.VisualState;
            bool parry = fighter.ContactAsTarget && fighter.LastContact == SumoContact.Parry && NetworkClock.Now - fighter.ContactAt < .22;
            bool dash = s.Attack == SumoAttack.Dash && (s.Phase == SumoCombatPhase.Windup || s.Phase == SumoCombatPhase.Dash);
            arc.enabled = !fighter.Participant.Dead && (s.Phase == SumoCombatPhase.Guard || s.Phase == SumoCombatPhase.Charge || parry || dash);
            if (!arc.enabled) return;
            bool charge = s.Phase == SumoCombatPhase.Charge;
            float angle = dash ? 55 : parry ? 360 : fighter.Config.GuardArc;
            float radius = fighter.Capsule.radius + .25f;
            Color color = dash ? BreakColor : parry || charge ? CounterColor : GuardColor; arc.startColor = arc.endColor = color;
            for (int i = 0; i < ArcPoints; i++)
            {
                float yaw = s.Yaw + Mathf.Lerp(-angle * .5f, angle * .5f, i / (float)(ArcPoints - 1));
                arc.SetPosition(i, fighter.Motor.Position + new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad) * radius, .025f, Mathf.Cos(yaw * Mathf.Deg2Rad) * radius));
            }
        }
        private void OnDestroy() { if (effects != null) Destroy(effects); if (strokeMaterial != null) Destroy(strokeMaterial); }
    }
}
