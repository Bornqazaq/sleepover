using UnityEngine;

namespace Igruha.Core.Spawning
{
    /// <summary>Роль точки спавна — под асимметричные и командные игры.</summary>
    public enum SpawnRole
    {
        /// <summary>Обычный игрок (утка, участник толпы).</summary>
        Default,
        /// <summary>Особая роль: охотник, ведущий, оператор.</summary>
        Special,
        TeamA,
        TeamB
    }

    /// <summary>Точка спавна. Расставляется в сцене, собирается SpawnPointSet.</summary>
    public sealed class SpawnPoint : MonoBehaviour
    {
        [SerializeField] private SpawnRole role = SpawnRole.Default;

        public SpawnRole Role => role;

        private void OnDrawGizmos()
        {
            Gizmos.color = role == SpawnRole.Special ? Color.red : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.35f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.6f);
        }
    }
}
