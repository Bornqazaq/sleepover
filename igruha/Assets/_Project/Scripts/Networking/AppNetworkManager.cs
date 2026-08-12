using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Igruha.Networking;

public class AppNetworkManager : MonoBehaviour
{
    private void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("❌ NetworkManager.Singleton is NULL!");
            return;
        }

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            // Проверить launch arguments для определения режима
            bool isClientMode = System.Array.Exists(System.Environment.GetCommandLineArgs(),
                element => element.Equals("--client"));

            if (isClientMode)
            {
                // CLIENT mode: отправить версию протокола для Connection Approval (IGR-265)
                byte[] connectionPayload = ConnectionApprovalManager.GetConnectionPayload();
                NetworkManager.Singleton.NetworkConfig.ConnectionData = connectionPayload;

                NetworkManager.Singleton.StartClient();
                Debug.Log("🟢 Started as CLIENT - Connecting to Host (with protocol version)");
            }
            else
            {
                // HOST mode: запустить и слушать подключения
                // ConnectionApprovalManager настроит Connection Approval callback
                NetworkManager.Singleton.StartHost();
                Debug.Log("🟢 Started as HOST - NetworkManager ready (with Connection Approval)");
            }
        }

        // Sandbox загружается автоматически (позиция 1 в Build Settings)
        // После IGR-51: Players будут синхронизировать движение
        // После IGR-265: Connection Approval валидирует клиентов
    }
}
