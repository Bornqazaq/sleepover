using Igruha.Core.Scenes;
using Igruha.Core.Vision;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>
    /// Закрывает поломки, найденные прогоном вшестером 20.09: порядок
    /// расстановки в сцене, замах луча и коллайдеры бака «Переноски».
    /// </summary>
    public sealed class RoundStartTests
    {
        private GameObject eye;
        private GameObject target;

        [SetUp]
        public void Build()
        {
            eye = new GameObject("Eye");
            var cone = eye.AddComponent<VisionCone>();
            var data = new SerializedObject(cone);
            data.FindProperty("coneAngle").floatValue = 30f;
            data.FindProperty("range").floatValue = 20f;
            data.ApplyModifiedPropertiesWithoutUndo();

            target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            target.transform.position = new Vector3(0f, 0f, 10f);

            // В режиме редактирования шаг физики не идёт, и Collider.bounds
            // остаётся там, где объект создали. Без синхронизации конус
            // проверяет цель в нуле и не видит ничего.
            Physics.SyncTransforms();
        }

        [TearDown]
        public void Clean()
        {
            Object.DestroyImmediate(eye);
            Object.DestroyImmediate(target);
        }

        private VisionCone Cone => eye.GetComponent<VisionCone>();
        private Collider Body => target.GetComponent<Collider>();

        [Test]
        public void ConeSeesTargetStraightAhead()
        {
            eye.transform.rotation = Quaternion.identity;
            Assert.That(Cone.CanSee(Body), Is.True);
        }

        [Test]
        public void ConeMissesTargetOutsideItsSector()
        {
            // Цель под 40° — вдвое дальше половины сектора в 15°.
            eye.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
            Assert.That(Cone.CanSee(Body), Is.False);
        }

        /// <summary>
        /// Такт, за который луч перелетел через цель, обязан её задеть.
        /// Ровно этого не хватало «Ангелам» после снятия потолка скорости:
        /// на экране луч по человеку прошёл, по расчёту — мимо.
        /// </summary>
        [Test]
        public void SweepCatchesTargetCrossedBetweenTicks()
        {
            eye.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
            Cone.SetSweep(60f);
            Assert.That(Cone.CanSee(Body), Is.True);
        }

        [Test]
        public void SweepCatchesTargetCrossedTurningBack()
        {
            eye.transform.rotation = Quaternion.Euler(0f, -40f, 0f);
            Cone.SetSweep(-60f);
            Assert.That(Cone.CanSee(Body), Is.True);
        }

        /// <summary>Замах не бесконечный: рывок роли не должен просветить пол-арены.</summary>
        [Test]
        public void SweepIsCappedSoATeleportDoesNotLightTheWholeArena()
        {
            eye.transform.rotation = Quaternion.Euler(0f, 179f, 0f);
            Cone.SetSweep(3600f);
            Assert.That(Cone.CanSee(Body), Is.False);
        }

        [Test]
        public void SweepAppliesOnlyToTheTickItWasSetFor()
        {
            eye.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
            Cone.SetSweep(60f);
            Assert.That(Cone.CanSee(Body), Is.True);

            Cone.SetSweep(0f);
            Assert.That(Cone.CanSee(Body), Is.False);
        }

        /// <summary>Без сети ждать события загрузки нечего — шлюз открыт сразу.</summary>
        [Test]
        public void PlacementGateIsOpenWithoutNetwork()
        {
            Assert.That(ScenePlacementGate.IsOpen, Is.True);
        }

        /// <summary>
        /// У бака «Переноски» два коллайдера, и ровно один из них — триггер.
        ///
        /// Код брал «первый коллайдер» и делал триггером его, то есть
        /// сплошное тело бака: сквозь бак проходили насквозь, а зона слива
        /// теряла бутыль по выходу из любого из двух триггеров — донесённая
        /// вода не наливалась вовсе.
        /// </summary>
        [Test]
        public void CarryItemTankKeepsSolidBodyAndOneTriggerZone()
        {
            const string path = "Assets/_Project/Prefabs/Minigames/CarryItem/Tank.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, $"нет префаба {path}");

            var colliders = prefab.GetComponents<Collider>();
            int triggers = 0;
            int solids = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].isTrigger)
                {
                    triggers++;
                }
                else
                {
                    solids++;
                }
            }

            Assert.That(triggers, Is.EqualTo(1), "зона слива должна быть ровно одна");
            Assert.That(solids, Is.GreaterThanOrEqualTo(1), "телу бака нужен сплошной коллайдер");
        }
    }
}
