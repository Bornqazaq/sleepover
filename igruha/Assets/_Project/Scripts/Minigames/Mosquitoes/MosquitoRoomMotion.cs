using UnityEngine;
namespace Igruha.Minigames.Mosquitoes
{
    public sealed class MosquitoRoomMotion : MonoBehaviour
    {
        [SerializeField] private Transform[] curtains;
        private Quaternion[] rest;
        private void Awake()
        {
            rest = new Quaternion[curtains.Length];
            for (int i = 0; i < rest.Length; i++) rest[i] = curtains[i].localRotation;
        }
        private void Update()
        {
            for (int i = 0; i < curtains.Length; i++)
                curtains[i].localRotation = rest[i] * Quaternion.Euler(0, 0, Mathf.Sin(Time.time * .55f + i) * .45f);
        }
    }
}
