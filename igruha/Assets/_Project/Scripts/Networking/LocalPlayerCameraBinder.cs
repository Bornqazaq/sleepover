using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Igruha.Core.Player;

namespace Igruha.Networking
{
    /// <summary>
    /// Наводит камеру машины на своего персонажа.
    ///
    /// Персонажи спавнятся в рантайме, поэтому цель камеры нельзя прописать в сцене:
    /// каждый игрок должен смотреть за своим. Привязка повторяется после загрузки
    /// сцены, потому что хост спавнится ещё в Boot — до появления игровой камеры,
    /// а подключившийся клиент наоборот получает сцену раньше своего персонажа.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class LocalPlayerCameraBinder : NetworkBehaviour
    {
        private PlayerController playerController;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!IsOwner)
            {
                return;
            }

            BindCamera();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }

            base.OnNetworkDespawn();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindCamera();

        private void BindCamera()
        {
            // В игровой сцене ожидается один риг; если появятся сплит-скрины,
            // выбор камеры придётся делать явным
            var rig = FindFirstObjectByType<CinemachineCamera>();
            if (rig == null)
            {
                return;
            }

            rig.Follow = transform;
            rig.LookAt = transform;

            // Мотор считает ввод относительно камеры — без ссылки он берёт
            // Camera.main, но после смены сцены её нужно обновить
            if (Camera.main != null)
            {
                playerController.SetCameraReference(Camera.main.transform);
            }

            Debug.Log($"🎥 [{name}] Камера наведена на своего персонажа");
        }
    }
}
