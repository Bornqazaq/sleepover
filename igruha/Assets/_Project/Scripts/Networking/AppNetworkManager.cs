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
            NetworkManager.Singleton.StartHost();
            Debug.Log("🟢 Started as HOST - NetworkManager ready");
        }
    }
}
