using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class LocalPlayerCameraBinderNew : MonoBehaviour
{
    [SerializeField] private TopDownFollowCameraNew followCam;

    [Tooltip("How often to try rebinding if target is missing (seconds).")]
    [SerializeField] private float repollInterval = 0.25f;

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
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

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

        // In case enabled after already connected (host/editor cases)
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

        // Start (or restart) polling forever; it’s cheap and fixes vehicle replacement.
        CancelInvoke(nameof(TryBindLocalPlayer));
        InvokeRepeating(nameof(TryBindLocalPlayer), 0f, repollInterval);
    }

    private void TryBindLocalPlayer()
    {
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.IsClient || !NetworkManager.Singleton.IsConnectedClient) return;

        if (followCam == null)
            followCam = FindFirstObjectByType<TopDownFollowCameraNew>();

        if (followCam == null) return;

        // If we already have a valid target, keep it
        if (followCam.target != null)
            return;

        // Find the local owned vehicle (robust: use OwnerClientId)
        ulong localId = NetworkManager.Singleton.LocalClientId;

        foreach (var no in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (no == null) continue;
            if (no.OwnerClientId != localId) continue;

            // Only pick vehicles
            if (no.GetComponent<VehicleMovementNetcodeNew>() == null) continue;

            followCam.target = no.transform;
            return;
        }
    }
}
