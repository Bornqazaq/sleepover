using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using static Igruha.EditorTools.InfectionQuarantineAssets;

namespace Igruha.EditorTools
{
    /// <summary>Continuous 360-degree city behind the authored near ruins, plus lower street infill.</summary>
    internal static class InfectionCityBackdrop
    {
        private const int InnerCount = 24;
        private const int OuterCount = 16;
        internal static void Audit()
        {
            var intersect = typeof(HandleUtility).GetMethod("IntersectRayMesh", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (intersect == null) throw new InvalidOperationException("Editor mesh intersection API unavailable");
            var meshes = GameObject.Find("_QuarantineArt/CityBackdropNear").GetComponentsInChildren<MeshFilter>();
            var origins = InfectionCourtyardLayout.Load().spawns.Select(p => p.Position).Concat(new[] { Vector3.zero });
            int tested = 0;
            foreach (var origin in origins)
            foreach (float height in new[] { 1.7f, 3.2f })
            for (int angle = 0; angle < 360; angle += 5)
            {
                var ray = new Ray(origin + Vector3.up * height, Quaternion.Euler(0, angle, 0) * Vector3.forward);
                bool covered = false;
                foreach (var mesh in meshes)
                {
                    object[] args = { ray, mesh.sharedMesh, mesh.transform.localToWorldMatrix, new RaycastHit() };
                    if (!(bool)intersect.Invoke(null, args)) continue;
                    covered = true;
                    break;
                }
                if (!covered) throw new InvalidOperationException("Backdrop gap from " + origin + " height=" + height + " heading=" + angle);
                tested++;
            }
            Debug.Log("City backdrop audit: " + tested + " mesh rays from 8 spawns + centre at eye/camera height, no gaps.");
        }

        internal static void Build(Transform root)
        {
            var inner = Group("CityBackdropNear", root);
            // Fronts are tangent to the ring. Their 19.4 m width overlaps the ~15.5 m spacing.
            for (int i = 0; i < InnerCount; i++)
            {
                float yaw = i * 360f / InnerCount;
                float a = yaw * Mathf.Deg2Rad;
                float radius = 59 + Mathf.Sin(i * 2.7f) * 1.5f;
                var go = Place("BackdropBlock" + (i % 3 + 1), inner,
                    new Vector3(Mathf.Sin(a) * radius, 0, Mathf.Cos(a) * radius), yaw);
                go.transform.localScale = new Vector3(1.08f, .85f + (i % 4) * .09f, 1);
            }
            Combine(inner);
            var outer = Group("CityBackdropFar", root);
            for (int i = 0; i < OuterCount; i++)
            {
                float yaw = i * 360f / OuterCount + 9;
                float a = yaw * Mathf.Deg2Rad;
                var go = Place("BackdropBlock" + ((i + 1) % 3 + 1), outer,
                    new Vector3(Mathf.Sin(a) * 86, 0, Mathf.Cos(a) * 86), yaw);
                go.transform.localScale = new Vector3(1.9f, 1.1f + (i % 4) * .13f, 1.3f);
            }
            Combine(outer);
            var street = Group("CityStreetInfill", root);
            Vector3[] garages = {new Vector3(-36,0,28),new Vector3(-38,0,-4),new Vector3(-26,0,-28),
                new Vector3(-4,0,-30),new Vector3(23,0,-30),new Vector3(37,0,-4),new Vector3(15,0,38)};
            float[] angles = {-40,-88,-140,178,133,88,-7};
            for (int i = 0; i < garages.Length; i++)
            {
                Place("GarageRow",street,garages[i],angles[i]);
                Place("RubblePile",street,garages[i]+new Vector3(5,0,-3),i*37,.85f);
                Place("Tree",street,garages[i]+new Vector3(-5,0,3),i*53,1.1f);
            }
            Place("Kiosk",street,new Vector3(-25,0,15),-68,1.1f);
            Place("Kiosk",street,new Vector3(1,0,-26),178);
            Place("Sedan",street,new Vector3(-14,0,-27),-24,1.1f);
            Place("Sedan",street,new Vector3(29,0,1),23);
            Place("Dumpster",street,new Vector3(-24,0,11),-77);
            Place("Pallet",street,new Vector3(3,0,-24),28,1.3f);
            Place("Tires",street,new Vector3(-27,0,17),30,1.4f);
            Combine(street);
        }
    }
}
