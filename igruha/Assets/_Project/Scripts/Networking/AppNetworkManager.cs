using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

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
                NetworkManager.Singleton.StartClient();
                Debug.Log("🟢 Started as CLIENT - Connecting to Host");
            }
            else
            {
                NetworkManager.Singleton.StartHost();
                Debug.Log("🟢 Started as HOST - NetworkManager ready");
            }
        }

        // Sandbox загружается автоматически (позиция 1 в Build Settings)
        // После IGR-51: Players будут синхронизировать движение
    }
}
