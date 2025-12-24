using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class LocalPlayerCameraBinderNew : MonoBehaviour
{
    [SerializeField] private TopDownFollowCameraNew followCam;

    private bool _hooked;

    private void Awake()
    {
        if (followCam == null)
            followCam = FindFirstObjectByType<TopDownFollowCameraNew>();
    }

    private void OnEnable()
    {
        StartCoroutine(BindWhenNetworkManagerReady());
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null && _hooked)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
        _hooked = false;

        CancelInvoke(nameof(TryBindLocalPlayer));
    }

    private IEnumerator BindWhenNetworkManagerReady()
    {
        while (NetworkManager.Singleton == null)
            yield return null;

        if (!_hooked)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            _hooked = true;
        }

        // In case we enabled this after already being connected (host/editor cases)
        if (NetworkManager.Singleton.IsClient && NetworkManager.Singleton.IsConnectedClient)
        {
            OnClientConnected(NetworkManager.Singleton.LocalClientId);
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;

        // Only bind on the local client
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        InvokeRepeating(nameof(TryBindLocalPlayer), 0f, 0.25f);
    }

    private void TryBindLocalPlayer()
    {
        if (NetworkManager.Singleton == null) return;

        if (followCam == null)
            followCam = FindFirstObjectByType<TopDownFollowCameraNew>();

        if (followCam == null) return;

        // Find the local owned vehicle
        foreach (var no in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (no == null) continue;
            if (!no.IsOwner) continue;

            // Optional filter: only pick vehicles
            if (no.GetComponent<VehicleMovementNetcodeNew>() == null) continue;

            followCam.target = no.transform;
            CancelInvoke(nameof(TryBindLocalPlayer));
            return;
        }
    }
}
