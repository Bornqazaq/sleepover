using System.Collections.Generic;
using Igruha.Core.Player;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>
    /// IGR-596: удар в упор не валил цель, если вокруг много мебели.
    ///
    /// Кандидаты удара брались первыми восемью коллайдерами сферы, триггеры
    /// в том числе. Неподвижное физика выдаёт раньше подвижного, и в яме
    /// хаба у телевизора (пол, стенки, диван, кресла, колонки и триггер
    /// зоны дивана) все восемь мест доставались мебели — игрок в выборку
    /// не попадал вовсе. Замер 21.09: треть ударов в упор у телевизора.
    /// </summary>
    public sealed class PunchTargetingTests
    {
        private const float PushRadius = 1.2f;
        private const int FurnitureCount = 20;

        /// <summary>
        /// Площадка вдали от начала координат: тест может пойти в открытой
        /// сцене, а у хаба в нуле стоят пол ямы и зона дивана.
        /// </summary>
        private static readonly Vector3 Origin = new Vector3(5000f, 5000f, 5000f);

        private readonly List<GameObject> spawned = new List<GameObject>();
        private Collider targetBody;

        [SetUp]
        public void Build()
        {
            // Мебель вокруг бьющего: двадцать неподвижных тел в радиусе удара.
            for (int i = 0; i < FurnitureCount; i++)
            {
                float angle = i * Mathf.PI * 2f / FurnitureCount;
                float distance = 0.6f + 0.4f * (i % 2);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.localScale = Vector3.one * 0.2f;
                cube.transform.position = Origin + new Vector3(Mathf.Cos(angle) * distance, 0.1f, Mathf.Sin(angle) * distance);
                spawned.Add(cube);
            }

            // Зона во всю яму — как SofaZone.
            var zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            zone.transform.position = Origin;
            zone.transform.localScale = new Vector3(8f, 2.4f, 8f);
            zone.GetComponent<Collider>().isTrigger = true;
            spawned.Add(zone);

            // Цель — подвижная капсула в упор перед бьющим.
            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            target.transform.position = Origin + new Vector3(0f, 1f, 0.9f);
            var body = target.AddComponent<Rigidbody>();
            body.isKinematic = true;
            targetBody = target.GetComponent<Collider>();
            spawned.Add(target);

            // В режиме редактирования шаг физики не идёт — без синхронизации
            // запрос видит объекты там, где их создали.
            Physics.SyncTransforms();
        }

        [TearDown]
        public void Clean()
        {
            foreach (GameObject item in spawned)
            {
                Object.DestroyImmediate(item);
            }

            spawned.Clear();
        }

        [Test]
        public void TargetSurvivesCrowdedFurniture()
        {
            var buffer = new Collider[8];
            int count = PlayerPushAbility.CollectSolidCandidates(Origin, PushRadius, ref buffer);

            Assert.That(Contains(buffer, count, targetBody), Is.True,
                $"цель выпала из выборки: найдено {count}, буфер {buffer.Length}");
        }

        [Test]
        public void EveryFurnitureBodyIsKeptToo()
        {
            var buffer = new Collider[8];
            int count = PlayerPushAbility.CollectSolidCandidates(Origin, PushRadius, ref buffer);

            // Двадцать тел мебели и цель — ни одного не потеряно.
            Assert.That(count, Is.EqualTo(FurnitureCount + 1));
            Assert.That(buffer.Length, Is.GreaterThan(count), "буфер должен вырасти с запасом");
        }

        [Test]
        public void TriggersDoNotTakeSlots()
        {
            var buffer = new Collider[8];
            int count = PlayerPushAbility.CollectSolidCandidates(Origin, PushRadius, ref buffer);

            for (int i = 0; i < count; i++)
            {
                Assert.That(buffer[i].isTrigger, Is.False, $"в выборке триггер {buffer[i].name}");
            }
        }

        [Test]
        public void EmptyBufferIsReplacedInsteadOfLooping()
        {
            var buffer = new Collider[0];
            int count = PlayerPushAbility.CollectSolidCandidates(Origin, PushRadius, ref buffer);

            Assert.That(Contains(buffer, count, targetBody), Is.True);
        }

        private static bool Contains(Collider[] buffer, int count, Collider wanted)
        {
            for (int i = 0; i < count; i++)
            {
                if (buffer[i] == wanted)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
