using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class PayloadNew : NetworkBehaviour
{
    [Header("Identity")]
    [SerializeField] private TeamIdNew ownerTeam = TeamIdNew.TeamA;

    [Header("Return")]
    [SerializeField] private float autoReturnSeconds = 20f;

    [Header("Carry")]
    [SerializeField] private Vector3 carryOffset = Vector3.zero;

    [Header("Drop Safety")]
    [SerializeField] private float pickupLockSecondsAfterDrop = 0.6f;

    // Networked state
    private readonly NetworkVariable<ulong> carrierNetObjectId = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> isDropped = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // When the payload entered the dropped state (server network time)
    private readonly NetworkVariable<double> droppedAtServerTime = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Server-only state
    private Vector3 _homePos;
    private Quaternion _homeRot;
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
        if (!col.isTrigger) col.isTrigger = true;
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

                // carried means not dropped
                isDropped.Value = false;
            }
            else
            {
                DropInternal();
            }
        }
        else if (isDropped.Value)
        {
            // Auto return based on NetworkTime (not local Time.deltaTime)
            double now = NetworkManager.ServerTime.Time;
            double elapsed = now - droppedAtServerTime.Value;

            if (elapsed >= autoReturnSeconds)
                ReturnHomeInternal();
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
        TryPickup(other);
    }

    private void TryPickup(Collider other)
    {
        if (IsCarried) return;
        if (_pickupLockTimer > 0f) return;

        var teamComp = other.GetComponentInParent<TeamComponentNew>();
        if (teamComp == null) return;

        var carrierNO = other.GetComponentInParent<NetworkObject>();
        if (carrierNO == null) return;

        // Enemy-only pickup
        if (teamComp.Team == ownerTeam) return;

        carrierNetObjectId.Value = carrierNO.NetworkObjectId;
        isDropped.Value = false;
    }

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

    private void DropInternal()
    {
        carrierNetObjectId.Value = 0;
        isDropped.Value = true;
        droppedAtServerTime.Value = NetworkManager.ServerTime.Time;

        _pickupLockTimer = pickupLockSecondsAfterDrop;
    }

    private void ReturnHomeInternal()
    {
        carrierNetObjectId.Value = 0;
        isDropped.Value = false;
        droppedAtServerTime.Value = 0;

        _pickupLockTimer = 0f;
        transform.SetPositionAndRotation(_homePos, _homeRot);
    }

    // Client/UI helper: how many seconds until auto-return (0 if not dropped)
    public float GetReturnRemainingSeconds()
    {
        if (!IsSpawned) return 0f;
        if (!IsDropped) return 0f;
        if (NetworkManager.Singleton == null) return 0f;

        double now = NetworkManager.Singleton.ServerTime.Time;
        double elapsed = now - droppedAtServerTime.Value;
        return Mathf.Clamp(autoReturnSeconds - (float)elapsed, 0f, autoReturnSeconds);
    }
}
