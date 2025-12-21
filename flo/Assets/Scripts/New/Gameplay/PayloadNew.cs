using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class PayloadNew : NetworkBehaviour
{
    [Header("Identity")]
    [Tooltip("Which team this payload belongs to (its home base).")]
    [SerializeField] private TeamIdNew ownerTeam = TeamIdNew.TeamA;

    [Header("Return")]
    [SerializeField] private float autoReturnSeconds = 20f;

    [Header("Carry")]
    [Tooltip("Optional offset when carried (relative to CarrySocket).")]
    [SerializeField] private Vector3 carryOffset = Vector3.zero;

    [Header("Drop Safety")]
    [Tooltip("After dropping, payload cannot be picked up for this many seconds.")]
    [SerializeField] private float pickupLockSecondsAfterDrop = 0.6f;

    // Networked state
    private readonly NetworkVariable<ulong> carrierNetObjectId = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> isDropped = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Server-only state
    private Vector3 _homePos;
    private Quaternion _homeRot;
    private float _dropTimer;

    // Pickup lock after drop (server-only)
    private float _pickupLockTimer;

    public TeamIdNew OwnerTeam => ownerTeam;
    public bool IsCarried => carrierNetObjectId.Value != 0;
    public bool IsDropped => isDropped.Value;
    public ulong CarrierNetObjectId => carrierNetObjectId.Value;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _homePos = transform.position;
            _homeRot = transform.rotation;
        }

        var col = GetComponent<Collider>();
        if (!col.isTrigger)
        {
            Debug.LogWarning($"{name}: Payload collider should be trigger. Setting isTrigger=true.");
            col.isTrigger = true;
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer) return;

        if (_pickupLockTimer > 0f)
            _pickupLockTimer -= Time.deltaTime;

        if (IsCarried)
        {
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(carrierNetObjectId.Value, out var carrierObj))
            {
                Transform carryT = carrierObj.transform;
                var socket = carrierObj.GetComponentInChildren<CarrySocketNew>();
                if (socket != null) carryT = socket.transform;

                transform.position = carryT.position + carryOffset;
                transform.rotation = carryT.rotation;

                isDropped.Value = false;
                _dropTimer = 0f;
            }
            else
            {
                // Carrier disappeared -> drop
                DropInternal("carrier missing");
            }
        }
        else if (isDropped.Value)
        {
            _dropTimer += Time.deltaTime;
            if (_dropTimer >= autoReturnSeconds)
                ReturnHomeInternal("auto-return timer");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        TryPickup(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsServer) return;

        // IMPORTANT: This is what fixes “I dropped it and now I can’t re-pick it up”
        // If you dropped while overlapping, OnTriggerEnter won't fire again.
        // OnTriggerStay allows pickup once the lock expires.
        TryPickup(other);
    }

    private void TryPickup(Collider other)
    {
        if (IsCarried) return;

        // Lockout window right after drop
        if (_pickupLockTimer > 0f) return;

        var teamComp = other.GetComponentInParent<TeamComponentNew>();
        if (teamComp == null) return;

        var carrierNO = other.GetComponentInParent<NetworkObject>();
        if (carrierNO == null) return;

        // Enemy-only pickup
        if (teamComp.Team == ownerTeam) return;

        // Pick up
        carrierNetObjectId.Value = carrierNO.NetworkObjectId;
        isDropped.Value = false;
        _dropTimer = 0f;

        // (Optional) small lock reset is fine either way; leaving it at 0 keeps it responsive
        _pickupLockTimer = 0f;

        // Debug.Log($"[PayloadNew:{name}] picked up by carrier={carrierNetObjectId.Value}");
    }

    // -------------------------
    // Server-side commands
    // -------------------------
    public void ServerDrop()
    {
        if (!IsServer) return;
        if (!IsCarried) return;
        DropInternal("ServerDrop()");
    }

    public void ServerReturnHome()
    {
        if (!IsServer) return;
        ReturnHomeInternal("ServerReturnHome()");
    }

    // -------------------------
    // Internal transitions (server only)
    // -------------------------
    private void DropInternal(string reason)
    {
        carrierNetObjectId.Value = 0;
        isDropped.Value = true;
        _dropTimer = 0f;

        // Prevent immediate regrab in the same overlap
        _pickupLockTimer = pickupLockSecondsAfterDrop;

        // Debug.Log($"[PayloadNew:{name}] dropped ({reason}), lock={pickupLockSecondsAfterDrop:0.00}s");
    }

    private void ReturnHomeInternal(string reason)
    {
        carrierNetObjectId.Value = 0;
        isDropped.Value = false;
        _dropTimer = 0f;
        _pickupLockTimer = 0f;

        transform.SetPositionAndRotation(_homePos, _homeRot);

        // Debug.Log($"[PayloadNew:{name}] returned home ({reason})");
    }
}
