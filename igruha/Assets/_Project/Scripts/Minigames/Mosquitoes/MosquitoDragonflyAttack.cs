using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    // Cosmetic only. The server has already killed the victim before this is created.
    public sealed class MosquitoDragonflyAttack : MonoBehaviour
    {
        private Vector3 prey;
        private float age;
        private readonly List<Transform> wings = new List<Transform>();
        private readonly List<Quaternion> wingRotations = new List<Quaternion>();
        private GameObject captured;
        public static void Show(GameObject prefab, Vector3 point, MosquitoBody victim)
        {
            if (prefab == null) return;
            var go = Instantiate(prefab); go.name = "DragonflyCapture";
            var attack = go.AddComponent<MosquitoDragonflyAttack>(); attack.prey = point;
            foreach (Transform t in go.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("DragonflyWing")) { attack.wings.Add(t); attack.wingRotations.Add(t.localRotation); }
            // Copy only the victim's visual. No network object or player logic survives death.
            if (victim != null)
            {
                var skin = victim.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skin != null)
                {
                    Transform visual = skin.transform;
                    while (visual.parent != victim.transform && visual.parent != null) visual = visual.parent;
                    attack.captured = Instantiate(visual.gameObject, go.transform);
                    attack.captured.transform.localPosition = new Vector3(0, -.04f, .24f);
                    foreach(var renderer in attack.captured.GetComponentsInChildren<Renderer>()) renderer.enabled=true;
                    attack.captured.SetActive(false);
                }
            }
            go.transform.position = point + new Vector3(1.4f, .25f, -.65f);
        }
        private void Update()
        {
            age += Time.deltaTime;
            if (age < .16f)
            {
                Vector3 from = prey + new Vector3(1.4f, .25f, -.65f);
                transform.position = Vector3.Lerp(from, prey, age / .16f);
                transform.rotation = Quaternion.LookRotation(prey - from);
            }
            else
            {
                if (captured != null) { captured.SetActive(true); captured.transform.localScale *= Mathf.Exp(-Time.deltaTime * 9); }
                Vector3 escape = new Vector3(2, 1.2f, 1);
                transform.position = prey + escape * Mathf.Pow((age - .16f) / .85f, 1.2f);
                transform.rotation = Quaternion.LookRotation(escape);
            }
            for (int i = 0; i < wings.Count; i++) wings[i].localRotation = wingRotations[i] * Quaternion.Euler(0, 0, Mathf.Sin(age * 140) * 24);
            if (age > 1.1f) Destroy(gameObject);
        }
    }
}
