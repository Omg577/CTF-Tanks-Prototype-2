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

    [Tooltip("If true, payload can only be picked up when it's at home or dropped. If false, same behavior anyway.")]
    [SerializeField] private bool allowPickupWhileMoving = true;

    // ---- Networked State (server writes, everyone reads) ----
    // 0 means no carrier
    private readonly NetworkVariable<ulong> carrierNetObjectId = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> isDropped = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ---- Non-networked server state ----
    private Vector3 _homePos;
    private Quaternion _homeRot;
    private float _dropTimer;

    // Public helpers
    public TeamIdNew OwnerTeam => ownerTeam;
    public bool IsCarried => carrierNetObjectId.Value != 0;
    public bool IsDropped => isDropped.Value;
    public ulong CarrierNetObjectId => carrierNetObjectId.Value;

    public bool IsHome
    {
        get
        {
            // Not perfectly robust but fine for MVP
            if (IsCarried || IsDropped) return false;
            return true;
        }
    }

    public override void OnNetworkSpawn()
    {
        // Capture home transform on server only
        if (IsServer)
        {
            _homePos = transform.position;
            _homeRot = transform.rotation;
        }

        // Ensure trigger collider for pickup
        var col = GetComponent<Collider>();
        if (!col.isTrigger)
        {
            Debug.LogWarning($"{name}: Payload collider should be trigger. Setting isTrigger=true.");
            col.isTrigger = true;
        }
    }

    private void Update()
    {
        if (!IsSpawned) return;

        // Server drives payload position/state
        if (!IsServer) return;

        if (IsCarried)
        {
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(carrierNetObjectId.Value, out var carrierObj))
            {
                Transform carryT = carrierObj.transform;

                // Prefer a CarrySocket marker if present
                var socket = carrierObj.GetComponentInChildren<CarrySocketNew>();
                if (socket != null) carryT = socket.transform;

                transform.position = carryT.position + carryOffset;
                transform.rotation = carryT.rotation;

                isDropped.Value = false;
                _dropTimer = 0f;
            }
            else
            {
                // Carrier disappeared (disconnect/despawn) -> drop
                DropInternal();
            }
        }
        else if (isDropped.Value)
        {
            _dropTimer += Time.deltaTime;
            if (_dropTimer >= autoReturnSeconds)
                ReturnHomeInternal();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        // Only player vehicles should pick up
        var teamComp = other.GetComponentInParent<TeamComponentNew>();
        if (teamComp == null) return;

        var carrierNO = other.GetComponentInParent<NetworkObject>();
        if (carrierNO == null) return;

        // Must be enemy to pick up
        if (teamComp.Team == ownerTeam) return;

        // Already carried
        if (IsCarried) return;

        // Optional gate
        if (!allowPickupWhileMoving)
        {
            // If you later add a "moving" state, gate here. MVP: no-op.
        }

        // Pick up
        carrierNetObjectId.Value = carrierNO.NetworkObjectId;
        isDropped.Value = false;
        _dropTimer = 0f;
    }

    // -------------------------
    // Server-side commands
    // -------------------------
    public void ServerDrop()
    {
        if (!IsServer) return;
        if (!IsCarried) return;
        DropInternal();
    }

    public void ServerReturnHome()
    {
        if (!IsServer) return;
        ReturnHomeInternal();
    }

    /// <summary>
    /// Force clear carrier. Useful if you add death later.
    /// </summary>
    public void ServerClearCarrier()
    {
        if (!IsServer) return;
        carrierNetObjectId.Value = 0;
    }

    // -------------------------
    // Internal state transitions
    // -------------------------
    private void DropInternal()
    {
        carrierNetObjectId.Value = 0;
        isDropped.Value = true;
        _dropTimer = 0f;
        // stays at current position
    }

    private void ReturnHomeInternal()
    {
        carrierNetObjectId.Value = 0;
        isDropped.Value = false;
        _dropTimer = 0f;
        transform.SetPositionAndRotation(_homePos, _homeRot);
    }
}
