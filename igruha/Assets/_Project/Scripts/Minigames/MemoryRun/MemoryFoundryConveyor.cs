using System;
using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>Decorative production loop. Does not read or change the route.</summary>
    public sealed class MemoryFoundryConveyor : MonoBehaviour
    {
        [SerializeField] private Transform[] links = Array.Empty<Transform>();
        [SerializeField] private Transform[] cargo = Array.Empty<Transform>();
        [SerializeField] private Transform[] rollers = Array.Empty<Transform>();
        [SerializeField] private float length = 47.52f;
        [SerializeField] private float radius = .32f;
        [SerializeField] private float speed = .7f;
        [SerializeField] private Vector3[] topPath = Array.Empty<Vector3>();
        private float[] distances;

        private void Awake() => CachePath();
        private void OnValidate() => distances = null;

        private void CachePath()
        {
            if (topPath.Length < 2) return;
            distances = new float[topPath.Length];
            for (int i = 1; i < topPath.Length; i++)
                distances[i] = distances[i - 1] + Vector3.Distance(topPath[i - 1], topPath[i]);
            length = distances[distances.Length - 1];
        }

        private void Update() => Pose(NetworkClock.Now);

        public void Pose(double time)
        {
            if (distances == null) CachePath();
            if (length <= .001f || radius <= .001f) return;
            float perimeter = 2 * length + 2 * Mathf.PI * radius;
            float travel = (float)(time * speed % perimeter);
            for (int i = 0; i < links.Length; i++)
            {
                Sample(Mathf.Repeat(travel + i * perimeter / links.Length, perimeter), out var pos, out var rotation);
                links[i].SetLocalPositionAndRotation(pos, rotation);
            }
            float cargoTravel = (float)(time * speed % length);
            for (int i = 0; i < cargo.Length; i++)
            {
                SampleTop(Mathf.Repeat(cargoTravel + i * length / cargo.Length, length), out var pos, out var rotation);
                cargo[i].SetLocalPositionAndRotation(pos + rotation * Vector3.up * .052f, rotation);
            }
            var roll = Quaternion.Euler((float)(time * speed / radius * Mathf.Rad2Deg % 360), 0, 0);
            foreach (var roller in rollers) roller.localRotation = roll;
        }

        private void Sample(float distance, out Vector3 position, out Quaternion rotation)
        {
            float arc = Mathf.PI * radius;
            float angle;
            if (distance < length)
            {
                SampleTop(distance, out position, out rotation);
            }
            else if (distance < length + arc)
            {
                angle = (distance - length) / radius;
                SampleTop(length, out position, out rotation);
                position += new Vector3(0, radius * (Mathf.Cos(angle) - 1), radius * Mathf.Sin(angle));
                rotation = Quaternion.Euler(angle * Mathf.Rad2Deg, 0, 0);
            }
            else if (distance < 2 * length + arc)
            {
                SampleTop(2 * length + arc - distance, out position, out rotation);
                position += Vector3.down * (2 * radius);
                rotation *= Quaternion.Euler(180, 0, 0);
            }
            else
            {
                angle = (distance - 2 * length - arc) / radius;
                SampleTop(0, out position, out rotation);
                position += new Vector3(0, -radius * (1 + Mathf.Cos(angle)), -radius * Mathf.Sin(angle));
                rotation = Quaternion.Euler(180 + angle * Mathf.Rad2Deg, 0, 0);
            }
        }

        private void SampleTop(float distance, out Vector3 position, out Quaternion rotation)
        {
            if (distances == null)
            {
                position = new Vector3(0, 0, distance - length / 2); rotation = Quaternion.identity;
                return;
            }
            int segment = Array.BinarySearch(distances, distance);
            if (segment < 0) segment = ~segment;
            segment = Mathf.Clamp(segment, 1, distances.Length - 1);
            float span = distances[segment] - distances[segment - 1];
            position = Vector3.Lerp(topPath[segment - 1], topPath[segment],
                span > .0001f ? (distance - distances[segment - 1]) / span : 0);
            rotation = Quaternion.LookRotation(topPath[segment] - topPath[segment - 1], Vector3.up);
        }
    }
}
