using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class LocalPlayerCameraBinderNew : MonoBehaviour
{
    [SerializeField] private CameraFollowNew followCam;

    [Tooltip("How often to try rebinding if target is missing (seconds).")]
    [SerializeField] private float repollInterval = 0.25f;

    [Header("Target Selection")]
    [Tooltip("If true, try to follow a child named 'CameraTarget' under the vehicle first.")]
    [SerializeField] private bool preferCameraTargetChild = true;

    [Tooltip("If true, fall back to following the vehicle's 'Visual' child if found.")]
    [SerializeField] private bool preferVisualChild = true;

    [Tooltip("If true, call ForceSnap() right after binding to remove any initial drift.")]
    [SerializeField] private bool forceSnapOnBind = true;

    private bool _hooked;

    private void Awake()
    {
        if (followCam == null)
            followCam = FindFirstObjectByType<CameraFollowNew>();
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

        CancelInvoke(nameof(TryBindLocalPlayer));
        InvokeRepeating(nameof(TryBindLocalPlayer), 0f, repollInterval);
    }

    private void TryBindLocalPlayer()
    {
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.IsClient || !NetworkManager.Singleton.IsConnectedClient) return;

        if (followCam == null)
            followCam = FindFirstObjectByType<CameraFollowNew>();

        if (followCam == null) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;

        // Clear missing/destroyed target
        if (followCam.target != null && !followCam.target)
            followCam.target = null;

        // If we already have a target, ensure it still belongs to a local-owned vehicle
        if (followCam.target != null)
        {
            var maybeNo = followCam.target.GetComponentInParent<NetworkObject>();
            if (maybeNo != null && maybeNo.OwnerClientId == localId && maybeNo.GetComponent<VehicleMovementNetcodeNew>() != null)
                return;

            followCam.target = null; // stale, rebind
        }

        // Find the local owned vehicle
        foreach (var no in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (no == null) continue;
            if (no.OwnerClientId != localId) continue;

            if (no.GetComponent<VehicleMovementNetcodeNew>() == null) continue;

            Transform t = null;

            if (preferCameraTargetChild)
            {
                var camTarget = no.transform.Find("CameraTarget");
                if (camTarget != null) t = camTarget;
            }

            if (t == null && preferVisualChild)
            {
                var visual = no.transform.Find("Visual");
                if (visual != null) t = visual;
            }

            if (t == null) t = no.transform;

            followCam.target = t;

            if (forceSnapOnBind)
                followCam.ForceSnap();

            return;
        }
    }
}
