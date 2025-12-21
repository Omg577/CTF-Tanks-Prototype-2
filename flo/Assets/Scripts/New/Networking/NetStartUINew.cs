using Unity.Netcode;
using UnityEngine;

public class NetStartUINew : MonoBehaviour
{
    private void OnGUI()
    {
        const int w = 200, h = 50, pad = 10;

        if (NetworkManager.Singleton == null)
        {
            GUI.Label(new Rect(pad, pad, 500, 30), "No NetworkManager in scene.");
            return;
        }

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUI.Button(new Rect(pad, pad, w, h), "Start Host"))
                NetworkManager.Singleton.StartHost();

            if (GUI.Button(new Rect(pad, pad + h + pad, w, h), "Start Client"))
                NetworkManager.Singleton.StartClient();

            if (GUI.Button(new Rect(pad, pad + 2 * (h + pad), w, h), "Start Server"))
                NetworkManager.Singleton.StartServer();
        }
        else
        {
            GUI.Label(new Rect(pad, pad, 400, 30),
                $"Mode: {(NetworkManager.Singleton.IsHost ? "Host" : NetworkManager.Singleton.IsServer ? "Server" : "Client")}");
            GUI.Label(new Rect(pad, pad + 25, 400, 30),
                $"ClientId: {NetworkManager.Singleton.LocalClientId}");
        }
    }
}
