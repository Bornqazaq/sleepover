using Igruha.Core.Audio;
using Igruha.Minigames.SumoRing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class SumoFeedbackTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void MutualHeavyContactsKeepBothIncomingReactionsInEitherOrder(bool reverse)
        {
            var a = new GameObject("a"); var b = new GameObject("b");
            try
            {
                var fa = a.AddComponent<SumoFighter>(); var fb = b.AddComponent<SumoFighter>();
                var ab = new SumoCombatHit { Attacker = 0, Target = 1, Attack = SumoAttack.Heavy, Contact = SumoContact.Push, Direction = Vector3.forward, Slide = .52f };
                var ba = new SumoCombatHit { Attacker = 1, Target = 0, Attack = SumoAttack.Heavy, Contact = SumoContact.Push, Direction = Vector3.back, Slide = .52f };
                if (reverse) { fb.Feedback(ba, false); fa.Feedback(ba, true); fa.Feedback(ab, false); fb.Feedback(ab, true); }
                else { fa.Feedback(ab, false); fb.Feedback(ab, true); fb.Feedback(ba, false); fa.Feedback(ba, true); }
                Assert.That(fa.ContactAsTarget && fb.ContactAsTarget, Is.True);
                Assert.That(fa.ContactDirection, Is.EqualTo(Vector3.back));
                Assert.That(fb.ContactDirection, Is.EqualTo(Vector3.forward));
                var miss = new SumoCombatHit { Contact = SumoContact.Miss, Attack = SumoAttack.Quick };
                fb.Feedback(miss, false);
                Assert.That(fb.ContactAttack, Is.EqualTo(SumoAttack.Heavy));
                Assert.That(fb.ContactSlide, Is.EqualTo(.52f));
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        [Test]
        public void EveryCombatOutcomeHasAShippedSoundAndKeyOutcomesUseDifferentClips()
        {
            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>("Assets/_Project/Audio/SumoRing/SfxLibrary.asset");
            foreach (SumoContact contact in System.Enum.GetValues(typeof(SumoContact)))
            foreach (SumoAttack attack in System.Enum.GetValues(typeof(SumoAttack)))
            {
                string slot = SumoAudio.ContactSlot(new SumoCombatHit { Contact = contact, Attack = attack });
                if (contact == SumoContact.Miss) { Assert.That(slot, Is.Null); continue; }
                Assert.That(library.TryGet(slot, out var entry), Is.True, slot);
                Assert.That(entry.Clip, Is.Not.Null, slot); Assert.That(entry.Spatial, Is.True);
            }
            library.TryGet("sumo_hit", out var hit); library.TryGet("sumo_break", out var broken); library.TryGet("sumo_parry", out var parry);
            Assert.That(hit.Clip, Is.Not.EqualTo(broken.Clip)); Assert.That(parry.Clip, Is.Not.EqualTo(hit.Clip)); Assert.That(parry.Clip, Is.Not.EqualTo(broken.Clip));
        }
    }
}
