using UnityEngine;

public class AppNetworkManager : MonoBehaviour
{
    private void Start()
    {
        var networkManager = Unity.Netcode.NetworkManager.Singleton;

        if (!networkManager.IsClient && !networkManager.IsServer)
        {
            networkManager.StartHost();
            Debug.Log("🟢 Started as HOST");
        }
    }
}
