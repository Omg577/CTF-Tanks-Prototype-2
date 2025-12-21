using Unity.Netcode;
using UnityEngine;

public class NetStartUINew : MonoBehaviour
{
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        }
    }

    private void OnServerStarted()
    {
        Debug.Log("[NetStartUINew] OnServerStarted fired.");
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[NetStartUINew] OnClientConnected clientId={clientId}");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[NetStartUINew] OnClientDisconnected clientId={clientId}");
    }

    private void OnTransportFailure()
    {
        Debug.LogError("[NetStartUINew] OnTransportFailure fired!");
    }

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
            {
                Debug.Log("[NetStartUINew] Clicking Start Host...");
                bool ok = NetworkManager.Singleton.StartHost();
                Debug.Log($"[NetStartUINew] StartHost returned {ok}");
            }

            if (GUI.Button(new Rect(pad, pad + h + pad, w, h), "Start Client"))
            {
                Debug.Log("[NetStartUINew] Clicking Start Client...");
                bool ok = NetworkManager.Singleton.StartClient();
                Debug.Log($"[NetStartUINew] StartClient returned {ok}");
            }

            if (GUI.Button(new Rect(pad, pad + 2 * (h + pad), w, h), "Start Server"))
            {
                Debug.Log("[NetStartUINew] Clicking Start Server...");
                bool ok = NetworkManager.Singleton.StartServer();
                Debug.Log($"[NetStartUINew] StartServer returned {ok}");
            }
        }
        else
        {
            GUI.Label(new Rect(pad, pad, 450, 30),
                $"Mode: {(NetworkManager.Singleton.IsHost ? "Host" : NetworkManager.Singleton.IsServer ? "Server" : "Client")}");
            GUI.Label(new Rect(pad, pad + 25, 450, 30),
                $"LocalClientId: {NetworkManager.Singleton.LocalClientId}");
        }
    }
}
